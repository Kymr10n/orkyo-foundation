-- @migration-class: expand

-- The journal kept one row per job and overwrote it on every start, so the last
-- success was destroyed by the next attempt: from the first failure the
-- orkyo_worker_job_last_success_timestamp_seconds series vanished and the stale-job
-- alerts (no absent() by design) could never fire. That is how the user-lifecycle job
-- failed every 30 seconds for four days in October 2026 while the worker read "healthy".
--
-- last_succeeded_at survives starts and failures; consecutive_failures lets the
-- coordinator back off and lets the exporter emit a gauge that is always present.

BEGIN;

ALTER TABLE public.worker_job_runs
    ADD COLUMN IF NOT EXISTS last_succeeded_at    timestamptz,
    ADD COLUMN IF NOT EXISTS consecutive_failures integer NOT NULL DEFAULT 0;

UPDATE public.worker_job_runs
   SET last_succeeded_at = completed_at
 WHERE last_succeeded_at IS NULL
   AND result = 'succeeded'
   AND completed_at IS NOT NULL;

COMMIT;
