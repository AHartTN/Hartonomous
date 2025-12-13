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
    cortex_cycle();
    PG_RETURN_BOOL(true);
}
