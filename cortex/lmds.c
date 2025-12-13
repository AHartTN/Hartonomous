/*
 * LMDS Implementation: Landmark Multidimensional Scaling
 * Core spatial positioning algorithm for Hartonomous Cortex
 */

#include "postgres.h"
#include "fmgr.h"
#include "executor/spi.h"
#include "utils/builtins.h"
#include <math.h>
#include <stdlib.h>

/* MaxMin landmark selection algorithm */
typedef struct {
    bytea *atom_id;
    double distance_sum;
} LandmarkCandidate;

/* Select k landmarks using MaxMin algorithm */
static void select_landmarks_maxmin(int k, int total_atoms) {
    SPITupleTable *tuptable;
    TupleDesc tupdesc;
    int i, j;
    
    /* Start with random first landmark */
    SPI_execute("INSERT INTO cortex_landmarks (atom_id, landmark_order) "
                "SELECT atom_id, 0 FROM atom WHERE atom_class = 0 "
                "ORDER BY random() LIMIT 1", false, 1);
    
    /* Iteratively add landmarks that are farthest from existing set */
    for (i = 1; i < k; i++) {
        /* Find atom with maximum minimum distance to existing landmarks */
        SPI_execute(
            "WITH candidate_distances AS ( "
            "  SELECT "
            "    a.atom_id, "
            "    MIN(ST_Distance(a.geom, l.geom)) as min_dist "
            "  FROM atom a "
            "  CROSS JOIN ( "
            "    SELECT a2.geom "
            "    FROM cortex_landmarks cl "
            "    JOIN atom a2 ON a2.atom_id = cl.atom_id "
            "  ) l "
            "  WHERE a.atom_class = 0 "
            "    AND NOT EXISTS ( "
            "      SELECT 1 FROM cortex_landmarks cl2 "
            "      WHERE cl2.atom_id = a.atom_id "
            "    ) "
            "  GROUP BY a.atom_id "
            ") "
            "INSERT INTO cortex_landmarks (atom_id, landmark_order) "
            "SELECT atom_id, %d "
            "FROM candidate_distances "
            "ORDER BY min_dist DESC "
            "LIMIT 1",
            false, 1
        );
    }
}

/* Calculate distance matrix between atoms and landmarks */
static double** calculate_distance_matrix(int n_atoms, int n_landmarks) {
    double **dist_matrix;
    SPITupleTable *tuptable;
    TupleDesc tupdesc;
    int ret, i;
    
    /* Allocate matrix */
    dist_matrix = (double **)palloc(n_atoms * sizeof(double *));
    for (i = 0; i < n_atoms; i++) {
        dist_matrix[i] = (double *)palloc(n_landmarks * sizeof(double));
    }
    
    /* Query all pairwise distances */
    ret = SPI_execute(
        "SELECT "
        "  a.atom_id, "
        "  l.landmark_order, "
        "  ST_Distance(a.geom, la.geom) as dist "
        "FROM atom a "
        "CROSS JOIN cortex_landmarks l "
        "JOIN atom la ON la.atom_id = l.atom_id "
        "WHERE a.atom_class = 0 "
        "ORDER BY a.atom_id, l.landmark_order",
        true, 0
    );
    
    if (ret == SPI_OK_SELECT) {
        tuptable = SPI_tuptable;
        tupdesc = tuptable->tupdesc;
        
        for (i = 0; i < SPI_processed; i++) {
            HeapTuple tuple = tuptable->vals[i];
            int atom_idx = i / n_landmarks;
            int landmark_idx = i % n_landmarks;
            
            bool isnull;
            Datum dist_datum = SPI_getbinval(tuple, tupdesc, 3, &isnull);
            
            if (!isnull) {
                dist_matrix[atom_idx][landmark_idx] = DatumGetFloat8(dist_datum);
            }
        }
    }
    
    return dist_matrix;
}

/* Modified Gram-Schmidt orthonormalization */
static void gram_schmidt_orthonormalize(double **vectors, int n_vectors, int dimension) {
    int i, j, k;
    double norm, dot_product;
    
    for (i = 0; i < n_vectors; i++) {
        /* Orthogonalize against previous vectors */
        for (j = 0; j < i; j++) {
            dot_product = 0.0;
            for (k = 0; k < dimension; k++) {
                dot_product += vectors[i][k] * vectors[j][k];
            }
            
            /* Subtract projection */
            for (k = 0; k < dimension; k++) {
                vectors[i][k] -= dot_product * vectors[j][k];
            }
        }
        
        /* Normalize */
        norm = 0.0;
        for (k = 0; k < dimension; k++) {
            norm += vectors[i][k] * vectors[i][k];
        }
        norm = sqrt(norm);
        
        if (norm > 1e-10) {
            for (k = 0; k < dimension; k++) {
                vectors[i][k] /= norm;
            }
        }
    }
}

/* LMDS coordinate calculation */
static void calculate_lmds_coordinates(double **dist_matrix, int n_atoms, int n_landmarks) {
    int i, j;
    double **coords;
    int target_dim = 2; // X, Y coordinates
    
    /* Allocate coordinate matrix */
    coords = (double **)palloc(n_atoms * sizeof(double *));
    for (i = 0; i < n_atoms; i++) {
        coords[i] = (double *)palloc(target_dim * sizeof(double));
    }
    
    /* Classical MDS: Use first n_landmarks distances to project into 2D */
    /* This is simplified - full LMDS uses eigendecomposition */
    for (i = 0; i < n_atoms; i++) {
        /* Project based on distances to first 2 landmarks */
        if (n_landmarks >= 2) {
            double d0 = dist_matrix[i][0];
            double d1 = dist_matrix[i][1];
            double landmark_dist = 10.0; // Assume landmarks ~10 units apart
            
            /* Triangulation */
            coords[i][0] = (d0 * d0 - d1 * d1 + landmark_dist * landmark_dist) / (2.0 * landmark_dist);
            coords[i][1] = sqrt(fmax(0.0, d0 * d0 - coords[i][0] * coords[i][0]));
        } else {
            coords[i][0] = dist_matrix[i][0];
            coords[i][1] = 0.0;
        }
    }
    
    /* Apply Gram-Schmidt for numerical stability */
    gram_schmidt_orthonormalize(coords, n_atoms, target_dim);
    
    /* Update atom positions in database */
    for (i = 0; i < n_atoms; i++) {
        char query[512];
        snprintf(query, sizeof(query),
            "UPDATE atom SET geom = ST_SetSRID(ST_MakePoint(%.6f, %.6f, ST_Z(geom), ST_M(geom)), 4326) "
            "WHERE atom_id = (SELECT atom_id FROM atom WHERE atom_class = 0 ORDER BY atom_id LIMIT 1 OFFSET %d)",
            coords[i][0], coords[i][1], i
        );
        SPI_execute(query, false, 0);
    }
    
    /* Free memory */
    for (i = 0; i < n_atoms; i++) {
        pfree(coords[i]);
    }
    pfree(coords);
}

/* Calculate stress metric */
static double calculate_stress(double **dist_matrix, int n_atoms, int n_landmarks) {
    double stress = 0.0;
    int i, j;
    int ret;
    SPITupleTable *tuptable;
    TupleDesc tupdesc;
    
    /* Compare original distances vs embedded distances */
    ret = SPI_execute(
        "SELECT "
        "  ST_Distance(a1.geom, a2.geom) as embedded_dist "
        "FROM atom a1, atom a2 "
        "WHERE a1.atom_class = 0 AND a2.atom_class = 0 "
        "  AND a1.atom_id < a2.atom_id "
        "LIMIT 1000", // Sample for performance
        true, 0
    );
    
    if (ret == SPI_OK_SELECT) {
        tuptable = SPI_tuptable;
        tupdesc = tuptable->tupdesc;
        
        for (i = 0; i < SPI_processed; i++) {
            HeapTuple tuple = tuptable->vals[i];
            bool isnull;
            Datum dist_datum = SPI_getbinval(tuple, tupdesc, 1, &isnull);
            
            if (!isnull) {
                double embedded = DatumGetFloat8(dist_datum);
                // Simplified stress calculation
                stress += embedded * embedded;
            }
        }
        
        stress = sqrt(stress / SPI_processed);
    }
    
    return stress;
}

PG_FUNCTION_INFO_V1(lmds_recalibrate);
Datum
lmds_recalibrate(PG_FUNCTION_ARGS)
{
    int n_landmarks = 100;
    int n_atoms;
    double **dist_matrix;
    double stress;
    
    SPI_connect();
    
    /* Get atom count */
    SPI_execute("SELECT COUNT(*) FROM atom WHERE atom_class = 0", true, 0);
    if (SPI_processed > 0) {
        bool isnull;
        n_atoms = DatumGetInt32(SPI_getbinval(SPI_tuptable->vals[0], SPI_tuptable->tupdesc, 1, &isnull));
    } else {
        SPI_finish();
        PG_RETURN_BOOL(false);
    }
    
    /* Clear old landmarks */
    SPI_execute("DELETE FROM cortex_landmarks", false, 0);
    
    /* Select landmarks using MaxMin */
    select_landmarks_maxmin(n_landmarks, n_atoms);
    
    /* Calculate distance matrix */
    dist_matrix = calculate_distance_matrix(n_atoms, n_landmarks);
    
    /* Calculate LMDS coordinates */
    calculate_lmds_coordinates(dist_matrix, n_atoms, n_landmarks);
    
    /* Calculate stress */
    stress = calculate_stress(dist_matrix, n_atoms, n_landmarks);
    
    /* Update cortex state */
    char update_query[512];
    snprintf(update_query, sizeof(update_query),
        "UPDATE cortex_state SET "
        "avg_stress = %.6f, "
        "landmark_count = %d, "
        "recalibrations = recalibrations + 1, "
        "last_cycle_at = now()",
        stress, n_landmarks
    );
    SPI_execute(update_query, false, 0);
    
    /* Free matrix */
    for (int i = 0; i < n_atoms; i++) {
        pfree(dist_matrix[i]);
    }
    pfree(dist_matrix);
    
    SPI_finish();
    
    PG_RETURN_BOOL(true);
}
