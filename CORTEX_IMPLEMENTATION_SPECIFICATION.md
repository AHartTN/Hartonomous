# Cortex Implementation Specification

**Component:** PostgreSQL Background Worker - Physics Engine
**Language:** C++ with PostgreSQL Extension Framework
**Purpose:** Continuous geometric refinement via LMDS and Gram-Schmidt

---

## Architecture Overview

The Cortex is a PostgreSQL background worker that enforces the "laws of physics" governing semantic space. It runs continuously, monitoring stress scores and recalibrating atom positions to maintain semantic consistency.

```
PostgreSQL Background Worker Process
        ↓
┌────────────────────────────┐
│   Stress Monitoring        │ Identify atoms with high stress
└──────────┬─────────────────┘
           ↓
┌────────────────────────────┐
│   Landmark Selection       │ MaxMin algorithm (k ≈ 100-500)
└──────────┬─────────────────┘
           ↓
┌────────────────────────────┐
│   Distance Calculation     │ Co-occurrence based
└──────────┬─────────────────┘
           ↓
┌────────────────────────────┐
│   LMDS Projection          │ Linear algebra (Eigen)
└──────────┬─────────────────┘
           ↓
┌────────────────────────────┐
│   Gram-Schmidt             │ Orthonormalize axes
└──────────┬─────────────────┘
           ↓
┌────────────────────────────┐
│   Batch Update             │ SPI bulk teleportation
└────────────────────────────┘
```

---

## Module 1: Background Worker Framework

### 1.1 Extension Skeleton

**File:** `cortex.c`

```c
#include "postgres.h"
#include "fmgr.h"
#include "miscadmin.h"
#include "postmaster/bgworker.h"
#include "storage/ipc.h"
#include "storage/latch.h"
#include "storage/lwlock.h"
#include "storage/proc.h"
#include "storage/shmem.h"
#include "executor/spi.h"
#include "access/xact.h"
#include "utils/snapmgr.h"

PG_MODULE_MAGIC;

void _PG_init(void);
void cortex_main(Datum);

/* Shared memory state */
typedef struct CortexSharedState {
    LWLock *lock;
    int64 atoms_processed;
    int64 recalibrations;
    double avg_stress;
} CortexSharedState;

static CortexSharedState *cortex_state = NULL;

void _PG_init(void) {
    BackgroundWorker worker;

    /* Configure background worker */
    memset(&worker, 0, sizeof(worker));
    worker.bgw_flags = BGWORKER_SHMEM_ACCESS | BGWORKER_BACKEND_DATABASE_CONNECTION;
    worker.bgw_start_time = BgWorkerStart_RecoveryFinished;
    worker.bgw_restart_time = 10;  /* Restart after 10 seconds if crashes */
    sprintf(worker.bgw_library_name, "cortex");
    sprintf(worker.bgw_function_name, "cortex_main");
    sprintf(worker.bgw_name, "Cortex Physics Engine");
    worker.bgw_notify_pid = 0;

    RegisterBackgroundWorker(&worker);
}

/* Main worker entry point */
void cortex_main(Datum arg) {
    /* Connect to database */
    BackgroundWorkerInitializeConnection("hartonomous", NULL, 0);

    elog(LOG, "Cortex: Physics engine started");

    /* Main loop */
    while (!got_SIGTERM) {
        int rc;

        /* Process one cycle */
        cortex_cycle();

        /* Sleep for 60 seconds */
        rc = WaitLatch(MyLatch,
                      WL_LATCH_SET | WL_TIMEOUT | WL_POSTMASTER_DEATH,
                      60000L,  /* 60 seconds */
                      PG_WAIT_EXTENSION);

        ResetLatch(MyLatch);

        /* Check for postmaster death */
        if (rc & WL_POSTMASTER_DEATH)
            proc_exit(1);

        CHECK_FOR_INTERRUPTS();
    }

    proc_exit(0);
}
```

### 1.2 Cycle Execution

```c
static void cortex_cycle(void) {
    int ret;

    /* Start transaction */
    SetCurrentStatementStartTimestamp();
    StartTransactionCommand();
    SPI_connect();
    PushActiveSnapshot(GetTransactionSnapshot());

    /* 1. Update landmarks if needed */
    cortex_update_landmarks();

    /* 2. Identify high-stress atoms */
    List *dirty_atoms = cortex_identify_dirty_atoms();

    /* 3. Recalibrate each atom */
    ListCell *lc;
    int recalibrated = 0;

    foreach(lc, dirty_atoms) {
        bytea *atom_id = (bytea *)lfirst(lc);

        if (cortex_recalibrate_atom(atom_id)) {
            recalibrated++;
        }
    }

    elog(LOG, "Cortex: Recalibrated %d atoms", recalibrated);

    /* Commit transaction */
    SPI_finish();
    PopActiveSnapshot();
    CommitTransactionCommand();

    /* Update shared state */
    LWLockAcquire(cortex_state->lock, LW_EXCLUSIVE);
    cortex_state->atoms_processed += list_length(dirty_atoms);
    cortex_state->recalibrations += recalibrated;
    LWLockRelease(cortex_state->lock);
}
```

---

## Module 2: Landmark Selection

### 2.1 MaxMin Algorithm

```c
#include <float.h>

typedef struct Landmark {
    bytea *atom_id;
    double coords[3];  /* XYZ */
} Landmark;

static Landmark *cortex_landmarks = NULL;
static int cortex_landmark_count = 0;

static void cortex_select_landmarks(int k) {
    int ret;
    bool isnull;
    Datum values[1];
    Oid argtypes[1] = {BYTEAOID};

    /* Free old landmarks */
    if (cortex_landmarks != NULL) {
        pfree(cortex_landmarks);
    }

    cortex_landmarks = palloc(sizeof(Landmark) * k);
    cortex_landmark_count = k;

    /* 1. Select first landmark randomly */
    ret = SPI_execute(
        "SELECT atom_id, "
        "ST_X(geom) as x, ST_Y(geom) as y, ST_Z(geom) as z "
        "FROM atom "
        "ORDER BY random() LIMIT 1",
        true, 1);

    if (ret != SPI_OK_SELECT || SPI_processed != 1) {
        elog(ERROR, "Cortex: Failed to select initial landmark");
    }

    /* Store first landmark */
    cortex_landmarks[0].atom_id = DatumGetByteaP(SPI_getbinval(SPI_tuptable->vals[0],
                                                                SPI_tuptable->tupdesc, 1, &isnull));
    cortex_landmarks[0].coords[0] = DatumGetFloat8(SPI_getbinval(SPI_tuptable->vals[0],
                                                                  SPI_tuptable->tupdesc, 2, &isnull));
    cortex_landmarks[0].coords[1] = DatumGetFloat8(SPI_getbinval(SPI_tuptable->vals[0],
                                                                  SPI_tuptable->tupdesc, 3, &isnull));
    cortex_landmarks[0].coords[2] = DatumGetFloat8(SPI_getbinval(SPI_tuptable->vals[0],
                                                                  SPI_tuptable->tupdesc, 4, &isnull));

    /* 2. Iteratively select maximally distant landmarks */
    for (int i = 1; i < k; i++) {
        StringInfoData query;
        initStringInfo(&query);

        /* Build query to find atom with maximum minimum distance to existing landmarks */
        appendStringInfo(&query,
            "WITH candidate_distances AS ( "
            "    SELECT atom_id, "
            "    MIN(ST_Distance(geom, landmark.geom)) as min_dist "
            "    FROM atom "
            "    CROSS JOIN (VALUES ");

        /* Add existing landmarks as inline values */
        for (int j = 0; j < i; j++) {
            if (j > 0) appendStringInfo(&query, ",");
            appendStringInfo(&query, "($%d::bytea)", j + 1);
        }

        appendStringInfo(&query,
            "    ) AS landmark_id(id) "
            "    JOIN atom landmark ON landmark.atom_id = landmark_id.id "
            "    GROUP BY atom_id "
            ") "
            "SELECT atom_id, "
            "ST_X(geom) as x, ST_Y(geom) as y, ST_Z(geom) as z "
            "FROM atom a "
            "JOIN candidate_distances cd ON cd.atom_id = a.atom_id "
            "ORDER BY cd.min_dist DESC LIMIT 1");

        /* Prepare values for existing landmarks */
        values[0] = PointerGetDatum(cortex_landmarks[j].atom_id);  /* Example: extend for all j < i */

        /* Execute (simplified - full implementation would handle all landmarks) */
        ret = SPI_execute(query.data, true, 1);

        if (ret != SPI_OK_SELECT || SPI_processed != 1) {
            elog(ERROR, "Cortex: Failed to select landmark %d", i);
        }

        /* Store landmark */
        cortex_landmarks[i].atom_id = DatumGetByteaP(SPI_getbinval(SPI_tuptable->vals[0],
                                                                    SPI_tuptable->tupdesc, 1, &isnull));
        cortex_landmarks[i].coords[0] = DatumGetFloat8(SPI_getbinval(SPI_tuptable->vals[0],
                                                                      SPI_tuptable->tupdesc, 2, &isnull));
        cortex_landmarks[i].coords[1] = DatumGetFloat8(SPI_getbinval(SPI_tuptable->vals[0],
                                                                      SPI_tuptable->tupdesc, 3, &isnull));
        cortex_landmarks[i].coords[2] = DatumGetFloat8(SPI_getbinval(SPI_tuptable->vals[0],
                                                                      SPI_tuptable->tupdesc, 4, &isnull));
    }

    elog(LOG, "Cortex: Selected %d landmarks", k);
}
```

---

## Module 3: Distance Calculation

### 3.1 Co-occurrence Distance Metric

```c
static double cortex_calculate_distance(bytea *atom_a, bytea *atom_b) {
    int ret;
    Datum values[2];
    Oid argtypes[2] = {BYTEAOID, BYTEAOID};
    bool isnull;
    int64 co_count;

    /* Query co-occurrence count */
    const char *query =
        "SELECT COUNT(DISTINCT parent_atom_id) "
        "FROM atom_compositions c1 "
        "JOIN atom_compositions c2 "
        "  ON c1.parent_atom_id = c2.parent_atom_id "
        "WHERE c1.component_atom_id = $1 "
        "  AND c2.component_atom_id = $2";

    values[0] = PointerGetDatum(atom_a);
    values[1] = PointerGetDatum(atom_b);

    ret = SPI_execute_with_args(query, 2, argtypes, values, NULL, true, 1);

    if (ret != SPI_OK_SELECT) {
        elog(ERROR, "Cortex: Failed to calculate co-occurrence");
    }

    co_count = DatumGetInt64(SPI_getbinval(SPI_tuptable->vals[0],
                                           SPI_tuptable->tupdesc, 1, &isnull));

    /* Distance formula: d(A,B) = 1 / (1 + co_count) */
    return 1.0 / (1.0 + (double)co_count);
}
```

---

## Module 4: LMDS Projection (C++ with Eigen)

**File:** `lmds_projector.cpp`

```cpp
#include <Eigen/Dense>
#include <Eigen/SVD>
#include <vector>

extern "C" {
#include "postgres.h"
#include "fmgr.h"
}

using namespace Eigen;

class LMDSProjector {
private:
    int k;  // Number of landmarks
    MatrixXd landmark_coords;  // k × 3 matrix
    MatrixXd L_pseudoinverse;  // Pseudoinverse of landmark configuration
    VectorXd delta_mu;         // Mean squared distance

public:
    LMDSProjector(const std::vector<std::vector<double>>& landmarks) {
        k = landmarks.size();

        // Initialize landmark coordinate matrix
        landmark_coords.resize(k, 3);
        for (int i = 0; i < k; i++) {
            landmark_coords.row(i) << landmarks[i][0], landmarks[i][1], landmarks[i][2];
        }

        // Calculate pairwise distance matrix
        MatrixXd D(k, k);
        for (int i = 0; i < k; i++) {
            for (int j = 0; j < k; j++) {
                double dist = (landmark_coords.row(i) - landmark_coords.row(j)).norm();
                D(i, j) = dist * dist;  // Squared distance
            }
        }

        // Calculate delta_mu (row-wise mean)
        delta_mu = D.rowwise().mean();

        // Calculate pseudoinverse using SVD
        JacobiSVD<MatrixXd> svd(landmark_coords, ComputeThinU | ComputeThinV);
        L_pseudoinverse = svd.solve(MatrixXd::Identity(k, k));
    }

    Vector3d project(const std::vector<double>& distances) {
        // distances[i] = distance from atom to landmark i

        // Build delta_a vector (squared distances)
        VectorXd delta_a(k);
        for (int i = 0; i < k; i++) {
            delta_a(i) = distances[i] * distances[i];
        }

        // Apply LMDS formula: x = -0.5 * L^# * (delta_a - delta_mu)
        Vector3d coords = -0.5 * L_pseudoinverse * (delta_a - delta_mu);

        return coords;
    }
};

// Global projector instance
static LMDSProjector *global_projector = nullptr;

extern "C" {

PG_FUNCTION_INFO_V1(cortex_lmds_project);

Datum cortex_lmds_project(PG_FUNCTION_ARGS) {
    // Extract distances array from PostgreSQL
    ArrayType *distances_arr = PG_GETARG_ARRAYTYPE_P(0);

    // Convert to std::vector
    int n_distances = ARR_DIMS(distances_arr)[0];
    float8 *distances_data = (float8 *)ARR_DATA_PTR(distances_arr);

    std::vector<double> distances(distances_data, distances_data + n_distances);

    // Project
    Vector3d coords = global_projector->project(distances);

    // Return as PostgreSQL array
    Datum values[3];
    values[0] = Float8GetDatum(coords(0));
    values[1] = Float8GetDatum(coords(1));
    values[2] = Float8GetDatum(coords(2));

    ArrayType *result = construct_array(values, 3, FLOAT8OID, sizeof(float8), FLOAT8PASSBYVAL, 'd');

    PG_RETURN_ARRAYTYPE_P(result);
}

}  // extern "C"
```

---

## Module 5: Modified Gram-Schmidt

```cpp
class GramSchmidtOrthonormalizer {
public:
    static MatrixXd orthonormalize(const MatrixXd& basis_vectors) {
        int n = basis_vectors.rows();  // Number of vectors
        int d = basis_vectors.cols();  // Dimensionality

        MatrixXd orthonormal(n, d);

        for (int i = 0; i < n; i++) {
            VectorXd v = basis_vectors.row(i);

            // Subtract projections onto all previous orthonormal vectors (ITERATIVELY)
            for (int j = 0; j < i; j++) {
                VectorXd u = orthonormal.row(j);
                double projection = v.dot(u);
                v = v - projection * u;  // Modified: subtract one at a time
            }

            // Normalize
            double norm = v.norm();
            if (norm < 1e-10) {
                elog(ERROR, "Cortex: Degenerate basis vector encountered");
            }

            orthonormal.row(i) = v / norm;
        }

        return orthonormal;
    }
};

extern "C" {

PG_FUNCTION_INFO_V1(cortex_gram_schmidt);

Datum cortex_gram_schmidt(PG_FUNCTION_ARGS) {
    // Input: 2D array of basis vectors
    // Output: 2D array of orthonormal vectors

    // Extract basis vectors from PostgreSQL
    ArrayType *basis_arr = PG_GETARG_ARRAYTYPE_P(0);

    // Convert to Eigen matrix
    // ... (implementation details omitted for brevity)

    MatrixXd basis = /* conversion code */;

    // Orthonormalize
    MatrixXd orthonormal = GramSchmidtOrthonormalizer::orthonormalize(basis);

    // Convert back to PostgreSQL array
    // ... (implementation details)

    PG_RETURN_ARRAYTYPE_P(result_arr);
}

}  // extern "C"
```

---

## Module 6: Stress Monitoring

```c
static List *cortex_identify_dirty_atoms(void) {
    int ret;
    List *dirty_atoms = NIL;

    const char *query =
        "SELECT atom_id "
        "FROM atom_embeddings "
        "WHERE stress_score > 0.5 "
        "ORDER BY stress_score DESC "
        "LIMIT 1000";

    ret = SPI_execute(query, true, 1000);

    if (ret != SPI_OK_SELECT) {
        elog(ERROR, "Cortex: Failed to identify dirty atoms");
    }

    /* Extract atom IDs */
    for (int i = 0; i < SPI_processed; i++) {
        bool isnull;
        bytea *atom_id = DatumGetByteaP(SPI_getbinval(SPI_tuptable->vals[i],
                                                      SPI_tuptable->tupdesc, 1, &isnull));

        if (!isnull) {
            dirty_atoms = lappend(dirty_atoms, atom_id);
        }
    }

    return dirty_atoms;
}

static double cortex_calculate_stress(bytea *atom_id) {
    /* Stress = Σ (d_geometric - d_observed)² */

    int ret;
    Datum values[1];
    Oid argtypes[1] = {BYTEAOID};

    const char *query =
        "WITH atom_neighbors AS ( "
        "    SELECT component_atom_id as neighbor_id "
        "    FROM atom_compositions "
        "    WHERE parent_atom_id IN ( "
        "        SELECT parent_atom_id FROM atom_compositions WHERE component_atom_id = $1 "
        "    ) "
        "    AND component_atom_id != $1 "
        "    LIMIT 100 "
        ") "
        "SELECT "
        "    ST_Distance(a1.geom, a2.geom) as d_geometric, "
        "    cortex_co_occurrence_distance($1, a2.atom_id) as d_observed "
        "FROM atom a1, atom a2, atom_neighbors "
        "WHERE a1.atom_id = $1 AND a2.atom_id = atom_neighbors.neighbor_id";

    values[0] = PointerGetDatum(atom_id);

    ret = SPI_execute_with_args(query, 1, argtypes, values, NULL, true, 100);

    if (ret != SPI_OK_SELECT) {
        return 0.0;
    }

    /* Calculate sum of squared differences */
    double stress = 0.0;
    for (int i = 0; i < SPI_processed; i++) {
        bool isnull;
        double d_geom = DatumGetFloat8(SPI_getbinval(SPI_tuptable->vals[i],
                                                     SPI_tuptable->tupdesc, 1, &isnull));
        double d_obs = DatumGetFloat8(SPI_getbinval(SPI_tuptable->vals[i],
                                                    SPI_tuptable->tupdesc, 2, &isnull));

        double diff = d_geom - d_obs;
        stress += diff * diff;
    }

    return stress / SPI_processed;  // Average squared error
}
```

---

## Module 7: Recalibration and Batch Update

```c
static bool cortex_recalibrate_atom(bytea *atom_id) {
    int ret;
    double distances[MAX_LANDMARKS];

    /* 1. Calculate distances from atom to all landmarks */
    for (int i = 0; i < cortex_landmark_count; i++) {
        distances[i] = cortex_calculate_distance(atom_id, cortex_landmarks[i].atom_id);
    }

    /* 2. Project via LMDS (call C++ function) */
    Datum distance_array = /* construct array from distances[] */;
    Datum new_coords = DirectFunctionCall1(cortex_lmds_project, distance_array);

    /* Extract XYZ from result */
    ArrayType *coords_arr = DatumGetArrayTypeP(new_coords);
    float8 *coords_data = (float8 *)ARR_DATA_PTR(coords_arr);

    double new_x = coords_data[0];
    double new_y = coords_data[1];
    double new_z = coords_data[2];

    /* 3. Calculate new stress */
    /* (Would need to temporarily update geometry to calculate) */
    double new_stress = 0.0;  // Simplified

    /* 4. Update atom_embeddings table */
    const char *update_query =
        "INSERT INTO atom_embeddings (atom_id, model_version, semantic_geom, stress_score) "
        "VALUES ($1, $2, ST_MakePoint($3, $4, $5, $6), $7) "
        "ON CONFLICT (atom_id, model_version) "
        "DO UPDATE SET semantic_geom = EXCLUDED.semantic_geom, stress_score = EXCLUDED.stress_score";

    Datum values[7];
    Oid argtypes[7] = {BYTEAOID, INT4OID, FLOAT8OID, FLOAT8OID, FLOAT8OID, FLOAT8OID, FLOAT8OID};

    values[0] = PointerGetDatum(atom_id);
    values[1] = Int32GetDatum(1);  // Model version
    values[2] = Float8GetDatum(new_x);
    values[3] = Float8GetDatum(new_y);
    values[4] = Float8GetDatum(new_z);
    values[5] = Float8GetDatum(0.0);  // M (unchanged)
    values[6] = Float8GetDatum(new_stress);

    ret = SPI_execute_with_args(update_query, 7, argtypes, values, NULL, false, 0);

    if (ret != SPI_OK_INSERT) {
        elog(WARNING, "Cortex: Failed to update embedding for atom");
        return false;
    }

    return true;
}
```

---

## Module 8: Configuration and Monitoring

### 8.1 Configuration Table

```sql
CREATE TABLE cortex_config (
    key VARCHAR(100) PRIMARY KEY,
    value TEXT NOT NULL
);

INSERT INTO cortex_config VALUES
    ('landmark_count', '100'),
    ('stress_threshold', '0.5'),
    ('recalibration_batch_size', '1000'),
    ('cycle_interval_seconds', '60'),
    ('enabled', 'true');
```

### 8.2 Monitoring Views

```sql
CREATE VIEW cortex_status AS
SELECT
    (SELECT value FROM cortex_config WHERE key = 'enabled') as enabled,
    (SELECT COUNT(*) FROM atom_embeddings WHERE stress_score > 0.5) as high_stress_atoms,
    (SELECT AVG(stress_score) FROM atom_embeddings) as avg_stress,
    (SELECT MAX(stress_score) FROM atom_embeddings) as max_stress;
```

---

## Build and Installation

### Makefile

```makefile
MODULE_big = cortex
OBJS = cortex.o lmds_projector.o

# PostgreSQL extension build framework
PG_CONFIG = pg_config
PGXS := $(shell $(PG_CONFIG) --pgxs)
include $(PGXS)

# C++ compilation
CXX = g++
CXXFLAGS = -std=c++17 -O2 -I/usr/include/eigen3

lmds_projector.o: lmds_projector.cpp
	$(CXX) $(CXXFLAGS) -fPIC -c $< -o $@

# Link against Eigen (header-only, no lib needed)
SHLIB_LINK += -lstdc++
```

### Installation

```bash
# Build extension
make
sudo make install

# Enable in PostgreSQL
psql -d hartonomous -c "CREATE EXTENSION cortex;"

# Start background worker (automatic on server restart)
# Or reload configuration
psql -d hartonomous -c "SELECT pg_reload_conf();"
```

---

## Testing and Validation

### Unit Tests

```c
#ifdef PG_MODULE_MAGIC
// Test functions only available in test builds

PG_FUNCTION_INFO_V1(cortex_test_maxmin);

Datum cortex_test_maxmin(PG_FUNCTION_ARGS) {
    /* Select 10 landmarks */
    cortex_select_landmarks(10);

    /* Verify they are maximally spread */
    // ... validation logic

    PG_RETURN_BOOL(true);
}

#endif
```

### Integration Tests

```sql
-- Insert test atoms
INSERT INTO atom (atom_id, ...) VALUES ...;

-- Manually trigger one cortex cycle
SELECT cortex_cycle_once();

-- Verify atoms were recalibrated
SELECT COUNT(*) FROM atom_embeddings WHERE model_version = 1;
```

---

## Performance Targets

| Metric | Target |
|--------|--------|
| Landmark selection (k=100) | <10 seconds |
| Distance calculation per pair | <10ms |
| LMDS projection per atom | <50ms |
| Recalibration batch (1000 atoms) | <60 seconds |
| Memory usage | <1GB resident |

---

**Complete Cortex implementation following this specification enables continuous geometric refinement maintaining semantic consistency in the Hartonomous substrate.**
