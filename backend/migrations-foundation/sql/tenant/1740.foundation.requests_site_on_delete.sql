-- @migration-class: expand

-- Makes deleting a site work when requests are scoped to it.
--
-- requests.site_id (migration 1550) references sites(id) with no delete action, so deleting a
-- site that any request is scoped to fails with a raw foreign-key violation. Nothing guards
-- against it in SiteService, so the user meets a constraint error rather than a decision.
--
-- SET NULL, matching what migration 1700 chose for resources.home_site_id. A NULL site_id is
-- not a degraded state here — 1550 defines it as "site-neutral (schedulable anywhere)", which
-- is exactly what a request becomes when the site it was scoped to no longer exists. Deleting
-- a site should not delete the work planned at it.
--
-- The alternative, RESTRICT, would at least be honest, but it turns "delete this site" into a
-- task of hunting down every request that mentions it, and the product offers no view for that.
--
-- Rollback: drop the constraint and re-add it without the delete action.

BEGIN;

ALTER TABLE public.requests DROP CONSTRAINT IF EXISTS requests_site_id_fkey;

ALTER TABLE public.requests
    ADD CONSTRAINT requests_site_id_fkey
    FOREIGN KEY (site_id) REFERENCES public.sites(id) ON DELETE SET NULL;

COMMIT;
