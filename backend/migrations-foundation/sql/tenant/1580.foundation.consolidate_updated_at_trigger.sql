-- @migration-class: contract

-- Consolidate the duplicate updated_at trigger function on requests.
--
-- update_requests_updated_at() (CURRENT_TIMESTAMP) and the shared
-- update_updated_at_column() (NOW()) are functionally identical — NOW() and
-- CURRENT_TIMESTAMP are the same function in Postgres. Only the requests table
-- used the bespoke variant. Re-point the trigger at the shared function and drop
-- the now-unused one. No behavioral change to requests.updated_at.
--
-- update_requests_updated_at() has no other dependents (verified: the requests
-- trigger was its only caller). Rollback: recreate update_requests_updated_at()
-- from migration 1100 and re-point trigger_requests_updated_at at it.

BEGIN;

DROP TRIGGER IF EXISTS trigger_requests_updated_at ON public.requests;

CREATE TRIGGER trigger_requests_updated_at
    BEFORE UPDATE ON public.requests
    FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();

DROP FUNCTION IF EXISTS public.update_requests_updated_at();

COMMIT;
