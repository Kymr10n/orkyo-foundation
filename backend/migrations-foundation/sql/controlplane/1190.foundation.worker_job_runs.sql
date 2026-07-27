-- @migration-class: expand

-- Journal for scheduled worker jobs (tenant lifecycle, GDPR user lifecycle,
-- announcement broadcasts). Replaces the workers' instance-local "last run"
-- fields, which reset on every restart (immediately re-running the daily GDPR
-- pass) and made replicas double-run every job. WorkerJobCoordinator reads
-- completed_at under a per-job Postgres advisory lock to decide due-ness, so
-- restarts resume the schedule and concurrent instances mutually exclude.
-- One row per job, upserted in place — no growth, no pruning needed.

BEGIN;

CREATE TABLE IF NOT EXISTS public.worker_job_runs (
    job_name     text        PRIMARY KEY,
    started_at   timestamptz NOT NULL,
    completed_at timestamptz,
    result       text
);

COMMIT;
