-- @migration-class: contract

-- The per-tenant feedback table (tenant migration 1240) is dead. Feedback was relocated to the
-- control-plane in 0.6.9 (migration 1170.foundation.feedback, keyed by tenant_id) so a site-admin
-- can triage every submission in one place; the repository writes and reads the control-plane table
-- exclusively. Nothing reads or writes the per-tenant copy anymore — drop it.
--
-- Rollback: recreate the table from 1240.foundation.feedback (no data to preserve — it received no
-- writes after the 0.6.9 relocation).

BEGIN;

DROP TABLE IF EXISTS public.feedback;

COMMIT;
