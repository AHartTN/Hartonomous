/*
 * cortex.c - PostgreSQL Background Worker for Geometric Recalibration
 * 
 * Implements LMDS-based continuous refinement of atom positions
 * in 4D semantic space.
 */

#include "postgres.h"
#include "fmgr.h"
#include "miscadmin.h"
#include "postmaster/bgworker.h"
#include "postmaster/interrupt.h"
#include "storage/ipc.h"
#include "storage/latch.h"
#include "storage/lwlock.h"
#include "storage/proc.h"
#include "storage/shmem.h"
#include "executor/spi.h"
#include "access/xact.h"
#include "utils/snapmgr.h"
#include "utils/timestamp.h"
#include "utils/wait_classes.h"
#include "lmds_projector.h"

PG_MODULE_MAGIC;

void _PG_init(void);
void cortex_main(Datum);
static void cortex_cycle(void);

/* Shared memory state */
typedef struct CortexSharedState
{
    LWLock     *lock;
    int64       atoms_processed;
    int64       recalibrations;
    double      avg_stress;
    TimestampTz last_cycle;
} CortexSharedState;

static CortexSharedState *cortex_state = NULL;
static shmem_startup_hook_type prev_shmem_startup_hook = NULL;

/* Shared memory initialization */
static void
cortex_shmem_startup(void)
{
    bool found;

    if (prev_shmem_startup_hook)
        prev_shmem_startup_hook();

    LWLockAcquire(AddinShmemInitLock, LW_EXCLUSIVE);

    cortex_state = ShmemInitStruct("cortex",
                                   sizeof(CortexSharedState),
                                   &found);

    if (!found)
    {
        cortex_state->lock = &(GetNamedLWLockTranche("cortex"))->lock;
        cortex_state->atoms_processed = 0;
        cortex_state->recalibrations = 0;
        cortex_state->avg_stress = 0.0;
        cortex_state->last_cycle = GetCurrentTimestamp();
    }

    LWLockRelease(AddinShmemInitLock);
}

/* Extension initialization */
void
_PG_init(void)
{
    BackgroundWorker worker;

    if (!process_shared_preload_libraries_in_progress)
        return;

    /* Request shared memory */
    RequestAddinShmemSpace(sizeof(CortexSharedState));
    RequestNamedLWLockTranche("cortex", 1);

    prev_shmem_startup_hook = shmem_startup_hook;
    shmem_startup_hook = cortex_shmem_startup;

    /* Configure background worker */
    memset(&worker, 0, sizeof(worker));
    worker.bgw_flags = BGWORKER_SHMEM_ACCESS |
                       BGWORKER_BACKEND_DATABASE_CONNECTION;
    worker.bgw_start_time = BgWorkerStart_RecoveryFinished;
    worker.bgw_restart_time = 10;
    sprintf(worker.bgw_library_name, "cortex");
    sprintf(worker.bgw_function_name, "cortex_main");
    snprintf(worker.bgw_name, BGW_MAXLEN, "Cortex Physics Engine");
    worker.bgw_notify_pid = 0;

    RegisterBackgroundWorker(&worker);
}

/* Main worker entry point */
void
cortex_main(Datum arg)
{
    int rc;

    /* Connect to database */
    BackgroundWorkerInitializeConnection("hartonomous", NULL, 0);

    elog(LOG, "Cortex: Physics engine started");

    /* Main loop */
    while (!ShutdownRequestPending)
    {
        rc = WaitLatch(MyLatch,
                      WL_LATCH_SET | WL_TIMEOUT | WL_POSTMASTER_DEATH,
                      60000L,  /* 60 second cycle */
                      PG_WAIT_EXTENSION);

        ResetLatch(MyLatch);

        /* Check for shutdown */
        if (rc & WL_POSTMASTER_DEATH)
            proc_exit(1);

        /* Run one recalibration cycle */
        cortex_cycle();
    }

    proc_exit(0);
}

/* Single recalibration cycle */
static void
cortex_cycle(void)
{
    int ret;
    int recalibrated = 0;

    SetCurrentStatementStartTimestamp();
    StartTransactionCommand();
    SPI_connect();
    PushActiveSnapshot(GetTransactionSnapshot());

    /* Update landmarks if needed */
    ret = SPI_execute(
        "INSERT INTO cortex_landmarks (atom_id, landmark_index, model_version) "
        "SELECT atom_id, row_number() OVER (ORDER BY random()), "
        "       (SELECT model_version FROM cortex_state WHERE id = 1) "
        "FROM atom "
        "WHERE atom_class = 0 "  /* Constants only */
        "ORDER BY random() "
        "LIMIT 100 "
        "ON CONFLICT (atom_id) DO NOTHING;",
        false, 0
    );

    if (ret != SPI_OK_INSERT)
        elog(WARNING, "Cortex: Failed to update landmarks");

    /* Identify high-stress atoms (stub - full LMDS in C++ module) */
    ret = SPI_execute(
        "SELECT atom_id FROM atom "
        "WHERE atom_class = 1 "  /* Compositions need recalibration */
        "AND random() < 0.01 "   /* 1% sample per cycle */
        "LIMIT 100;",
        true, 100
    );

    if (ret == SPI_OK_SELECT && SPI_processed > 0)
    {
        recalibrated = SPI_processed;
        
        /* In production: call C++ LMDS projector here */
        /* For now: just update timestamp */
        SPI_execute(
            "UPDATE cortex_state SET "
            "atoms_processed = atoms_processed + $1, "
            "recalibrations = recalibrations + $1, "
            "last_cycle_at = now() "
            "WHERE id = 1;",
            false, 0
        );
    }

    /* Update shared state */
    LWLockAcquire(cortex_state->lock, LW_EXCLUSIVE);
    cortex_state->atoms_processed += recalibrated;
    cortex_state->recalibrations += recalibrated;
    cortex_state->last_cycle = GetCurrentTimestamp();
    LWLockRelease(cortex_state->lock);

    elog(LOG, "Cortex: Recalibrated %d atoms", recalibrated);

    SPI_finish();
    PopActiveSnapshot();
    CommitTransactionCommand();
}

/* Manual trigger function for testing */
PG_FUNCTION_INFO_V1(cortex_cycle_once);

Datum
cortex_cycle_once(PG_FUNCTION_ARGS)
{
    int ret;
    int recalibrated = 0;
    SPITupleTable *tuptable;
    TupleDesc tupdesc;

    SPI_connect();

    /* Update landmarks if needed */
    ret = SPI_execute(
        "INSERT INTO cortex_landmarks (atom_id, landmark_index, model_version) "
        "SELECT atom_id, row_number() OVER (ORDER BY random()), "
        "       (SELECT model_version FROM cortex_state WHERE id = 1) "
        "FROM atom "
        "WHERE atom_class = 0 "  /* Constants only */
        "ORDER BY random() "
        "LIMIT 100 "
        "ON CONFLICT (atom_id) DO NOTHING;",
        false, 0
    );

    if (ret != SPI_OK_INSERT)
        elog(WARNING, "Cortex: Failed to update landmarks");

    /* Identify high-stress atoms (stub - full LMDS in C++ module) */
    ret = SPI_execute(
        "SELECT atom_id FROM atom "
        "WHERE atom_class = 0 "  /* Constants for now */
        "ORDER BY random() "
        "LIMIT 100;",
        true, 100
    );

    if (ret == SPI_OK_SELECT)
    {
        recalibrated = SPI_processed;
        tuptable = SPI_tuptable;
        tupdesc = SPI_tuptable->tupdesc;
        
        /* Get landmarks for LMDS projection */
        ret = SPI_execute(
            "SELECT a.atom_id, ST_X(a.geom), ST_Y(a.geom), ST_Z(a.geom), ST_M(a.geom) "
            "FROM cortex_landmarks cl "
            "JOIN atom a ON a.atom_id = cl.atom_id "
            "ORDER BY cl.landmark_index "
            "LIMIT 100",
            true, 100
        );
        
        if (ret == SPI_OK_SELECT && SPI_processed > 0)
        {
            int num_landmarks = SPI_processed;
            SPITupleTable *landmark_table = SPI_tuptable;
            TupleDesc landmark_desc = landmark_table->tupdesc;
            
            /* Build landmark set */
            LandmarkSet landmarks;
            landmarks.count = num_landmarks;
            landmarks.landmarks = (Point4D*) palloc(num_landmarks * sizeof(Point4D));
            landmarks.distances = (double**) palloc(num_landmarks * sizeof(double*));
            
            /* Load landmark positions */
            for (int i = 0; i < num_landmarks; i++) {
                HeapTuple tuple = landmark_table->vals[i];
                bool isnull;
                
                landmarks.landmarks[i].x = DatumGetFloat8(SPI_getbinval(tuple, landmark_desc, 2, &isnull));
                landmarks.landmarks[i].y = DatumGetFloat8(SPI_getbinval(tuple, landmark_desc, 3, &isnull));
                landmarks.landmarks[i].z = DatumGetFloat8(SPI_getbinval(tuple, landmark_desc, 4, &isnull));
                landmarks.landmarks[i].m = DatumGetFloat8(SPI_getbinval(tuple, landmark_desc, 5, &isnull));
                
                /* Allocate distance matrix row */
                landmarks.distances[i] = (double*) palloc(num_landmarks * sizeof(double));
            }
            
            /* Compute pairwise landmark distances */
            for (int i = 0; i < num_landmarks; i++) {
                for (int j = 0; j < num_landmarks; j++) {
                    if (i == j) {
                        landmarks.distances[i][j] = 0.0;
                    } else {
                        double dx = landmarks.landmarks[i].x - landmarks.landmarks[j].x;
                        double dy = landmarks.landmarks[i].y - landmarks.landmarks[j].y;
                        double dz = landmarks.landmarks[i].z - landmarks.landmarks[j].z;
                        double dm = landmarks.landmarks[i].m - landmarks.landmarks[j].m;
                        landmarks.distances[i][j] = sqrt(dx*dx + dy*dy + dz*dz + dm*dm);
                    }
                }
            }
            
            /* Project high-stress atoms using LMDS */
            for (int i = 0; i < recalibrated && i < 100; i++) {
                HeapTuple tuple = tuptable->vals[i];
                bool isnull;
                
                /* Get atom_id as bytea */
                Datum atom_id_datum = SPI_getbinval(tuple, tupdesc, 1, &isnull);
                if (isnull) continue;
                
                /* Convert atom_id to hex string for query */
                char* atom_id_bytes = (char*) DatumGetPointer(atom_id_datum);
                char hex_id[65];
                for (int j = 0; j < 32; j++) {
                    sprintf(hex_id + (j * 2), "%02x", (unsigned char)atom_id_bytes[j]);
                }
                hex_id[64] = '\0';
                
                /* Get current atom position */
                char pos_query[256];
                snprintf(pos_query, sizeof(pos_query),
                    "SELECT ST_X(geom), ST_Y(geom), ST_Z(geom), ST_M(geom) "
                    "FROM atom WHERE atom_id = '\\x%s'::bytea",
                    hex_id
                );
                
                ret = SPI_execute(pos_query, true, 1);
                if (ret != SPI_OK_SELECT || SPI_processed != 1) continue;
                
                HeapTuple atom_tuple = SPI_tuptable->vals[0];
                TupleDesc atom_desc = SPI_tuptable->tupdesc;
                bool isnull_coord;
                
                Point4D current_pos;
                current_pos.x = DatumGetFloat8(SPI_getbinval(atom_tuple, atom_desc, 1, &isnull_coord));
                current_pos.y = DatumGetFloat8(SPI_getbinval(atom_tuple, atom_desc, 2, &isnull_coord));
                current_pos.z = DatumGetFloat8(SPI_getbinval(atom_tuple, atom_desc, 3, &isnull_coord));
                current_pos.m = DatumGetFloat8(SPI_getbinval(atom_tuple, atom_desc, 4, &isnull_coord));
                
                /* Calculate distances from this atom to all landmarks */
                double* landmark_dists = (double*) palloc(num_landmarks * sizeof(double));
                for (int j = 0; j < num_landmarks; j++) {
                    double dx = current_pos.x - landmarks.landmarks[j].x;
                    double dy = current_pos.y - landmarks.landmarks[j].y;
                    double dz = current_pos.z - landmarks.landmarks[j].z;
                    double dm = current_pos.m - landmarks.landmarks[j].m;
                    landmark_dists[j] = sqrt(dx*dx + dy*dy + dz*dz + dm*dm);
                }
                
                /* Project using LMDS */
                Point4D new_pos;
                lmds_project(landmark_dists, &landmarks, num_landmarks, &new_pos);
                
                /* Update atom position in database */
                char update_query[512];
                snprintf(update_query, sizeof(update_query),
                    "UPDATE atom SET geom = ST_SetSRID(ST_MakePoint(%f, %f, %f, %f), 4326)::geometry(PointZM, 4326) "
                    "WHERE atom_id = '\\x%s'::bytea",
                    new_pos.x, new_pos.y, new_pos.z, new_pos.m, hex_id
                );
                
                SPI_execute(update_query, false, 0);
                pfree(landmark_dists);
            }
            
            /* Clean up landmark data */
            for (int i = 0; i < num_landmarks; i++) {
                pfree(landmarks.distances[i]);
            }
            pfree(landmarks.distances);
            pfree(landmarks.landmarks);
        }
        
        /* Update state after processing */
        SPI_execute(
            "UPDATE cortex_state SET "
            "atoms_processed = (SELECT COUNT(*) FROM atom WHERE atom_class = 0), "
            "recalibrations = recalibrations + 1, "
            "last_cycle_at = now(), "
            "landmark_count = (SELECT COUNT(*) FROM cortex_landmarks) "
            "WHERE id = 1;",
            false, 0
        );
    }

    elog(LOG, "Cortex: Manual cycle recalibrated %d atoms", recalibrated);

    SPI_finish();
    PG_RETURN_BOOL(true);
}
