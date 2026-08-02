-- @migration-class: expand

-- Moves the per-type side tables onto `resources`, so a resource type stops needing a table.
--
-- `spaces` and `person_profiles` are the last two places where adding a resource type means
-- adding schema. Every column they hold is a property of a resource; they exist only because
-- spaces and people were modelled before the generic resource was. A tenant that invents
-- "vehicle" gets no equivalent, so vehicles cannot carry a site, a code or a contact — not
-- because that is meaningless for a vehicle, but because nobody wrote the table.
--
-- Expand half: add the columns, add the type flags that replace the hard-coded key checks,
-- backfill, and leave both side tables in place and authoritative. Readers move to the new
-- columns in the accompanying code change; 1710 drops the tables once nothing reads them.
-- Splitting it this way keeps this file revertible — it adds and copies, destroying nothing —
-- and gives the orkyo-saas quota query, which reads `spaces` directly from the control plane,
-- a window in which both shapes work.
--
-- Rollback: drop the added columns from `resources` and `resource_types`. The side tables are
-- still the source of truth at this point, so nothing is lost.

BEGIN;

-- ── Type flags ────────────────────────────────────────────────────────────────
-- Three behaviours are currently selected by comparing a type key to a string literal, in
-- SQL and in C#. That is why a tenant-defined type can never opt into them: the check names
-- 'space' and 'person' specifically. Making them declared properties of the type moves the
-- decision into data, where a tenant can reach it.
ALTER TABLE public.resource_types
    ADD COLUMN IF NOT EXISTS has_geometry            BOOLEAN NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS has_directory_profile   BOOLEAN NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS single_group_membership BOOLEAN NOT NULL DEFAULT false;

COMMENT ON COLUMN public.resource_types.has_geometry IS
    'Resources of this type can be placed on a floorplan (code, geometry, capacity). '
    'Replaces the hard-coded key = ''space'' test.';
COMMENT ON COLUMN public.resource_types.has_directory_profile IS
    'Resources of this type carry directory details (email, job title, department, linked '
    'user account). Replaces the hard-coded key = ''person'' test.';
COMMENT ON COLUMN public.resource_types.single_group_membership IS
    'A resource of this type may belong to at most one group. Enforced by '
    'enforce_single_group_membership(); replaces the key = ''space'' lookup in that trigger.';

UPDATE public.resource_types SET has_geometry = true, single_group_membership = true
 WHERE key = 'space';
UPDATE public.resource_types SET has_directory_profile = true
 WHERE key = 'person';

-- ── Floorplan columns ─────────────────────────────────────────────────────────
ALTER TABLE public.resources
    ADD COLUMN IF NOT EXISTS code        VARCHAR(63),
    ADD COLUMN IF NOT EXISTS is_physical BOOLEAN NOT NULL DEFAULT false,
    ADD COLUMN IF NOT EXISTS geometry    JSONB,
    ADD COLUMN IF NOT EXISTS properties  JSONB   NOT NULL DEFAULT '{}'::jsonb,
    ADD COLUMN IF NOT EXISTS capacity    INTEGER NOT NULL DEFAULT 1;

-- ── Directory columns ─────────────────────────────────────────────────────────
-- email stays CITEXT so the case-insensitive uniqueness semantics people rely on survive the
-- move. Npgsql has no CITEXT type handler, so every read still casts to text explicitly.
ALTER TABLE public.resources
    ADD COLUMN IF NOT EXISTS email          CITEXT,
    ADD COLUMN IF NOT EXISTS notes          TEXT,
    ADD COLUMN IF NOT EXISTS linked_user_id UUID REFERENCES public.users(id)      ON DELETE SET NULL,
    ADD COLUMN IF NOT EXISTS job_title_id   UUID REFERENCES public.job_titles(id) ON DELETE SET NULL,
    ADD COLUMN IF NOT EXISTS department_id  UUID REFERENCES public.departments(id) ON DELETE SET NULL;

COMMENT ON COLUMN public.resources.notes IS
    'Free-text notes, encrypted at rest by IEncryptionService before it reaches this column.';

-- ── Backfill ──────────────────────────────────────────────────────────────────
-- The side tables are keyed by the resource id, so both are single-pass joins.
UPDATE public.resources r
   SET code        = s.code,
       is_physical = s.is_physical,
       geometry    = s.geometry,
       properties  = s.properties,
       capacity    = s.capacity
  FROM public.spaces s
 WHERE s.id = r.id;

UPDATE public.resources r
   SET email          = pp.email,
       notes          = pp.notes,
       linked_user_id = pp.linked_user_id,
       job_title_id   = pp.job_title_id,
       department_id  = pp.department_id
  FROM public.person_profiles pp
 WHERE pp.resource_id = r.id;

-- A space's site lived in spaces.site_id while every other resource used home_site_id, so
-- readers carried COALESCE(s.site_id, r.home_site_id) everywhere. One column now: a space is
-- simply a resource whose home site is set and which is not allowed to travel. 1550 already
-- set cross_site_allowed = false for every space, so that half needs no backfill.
UPDATE public.resources r
   SET home_site_id = s.site_id
  FROM public.spaces s
 WHERE s.id = r.id AND r.home_site_id IS DISTINCT FROM s.site_id;

-- ── Site deletion ─────────────────────────────────────────────────────────────
-- spaces.site_id was ON DELETE CASCADE and resources.home_site_id had no action at all, so
-- deleting a site deleted the spaces row and stranded the resources row behind it — an
-- orphan resource pointing at nothing. Neither behaviour survives the fold as-is: CASCADE
-- would now delete the resource itself, which resource_assignments (ON DELETE RESTRICT)
-- would refuse, turning a working delete into a hard error.
--
-- SET NULL keeps the resource and clears its site, which is what the orphan case was already
-- approximating, minus the corruption.
ALTER TABLE public.resources DROP CONSTRAINT IF EXISTS resources_home_site_id_fkey;
ALTER TABLE public.resources
    ADD CONSTRAINT resources_home_site_id_fkey
    FOREIGN KEY (home_site_id) REFERENCES public.sites(id) ON DELETE SET NULL;

-- ── Constraints ───────────────────────────────────────────────────────────────
-- Carried over from spaces.check_physical_has_geometry. NOT VALID so the ALTER takes no full
-- table scan under lock; validated separately below, which takes only a SHARE UPDATE
-- EXCLUSIVE lock and does not block writes.
ALTER TABLE public.resources
    ADD CONSTRAINT resources_physical_has_geometry_check
    CHECK (NOT is_physical OR geometry IS NOT NULL) NOT VALID;

COMMIT;

ALTER TABLE public.resources VALIDATE CONSTRAINT resources_physical_has_geometry_check;

-- ── Indexes ───────────────────────────────────────────────────────────────────
-- Outside the transaction so these can be CONCURRENTLY: the backfill above does not need
-- them, only the read paths that move onto these columns do. The runner does not wrap
-- scripts in a transaction, so statements after COMMIT run in autocommit.
--
-- These mirror the side tables' indexes, minus the ones the fold makes redundant:
-- idx_spaces_site_id is already covered by resources' site index, and person_profiles' PK
-- is now just the resources PK.
CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_resources_code
    ON public.resources (code) WHERE code IS NOT NULL;

CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_resources_site_code
    ON public.resources (home_site_id, code) WHERE code IS NOT NULL;

CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_resources_home_site
    ON public.resources (home_site_id) WHERE home_site_id IS NOT NULL;

CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_resources_linked_user
    ON public.resources (linked_user_id) WHERE linked_user_id IS NOT NULL;

CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_resources_job_title
    ON public.resources (job_title_id) WHERE job_title_id IS NOT NULL;

CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_resources_department
    ON public.resources (department_id) WHERE department_id IS NOT NULL;

-- person_profiles.linked_user_id was UNIQUE: one person row per user account. Preserved as a
-- partial unique index rather than a constraint, so the NULLs (resources with no linked
-- account, i.e. almost all of them) stay out of the index entirely.
CREATE UNIQUE INDEX CONCURRENTLY IF NOT EXISTS uq_resources_linked_user
    ON public.resources (linked_user_id) WHERE linked_user_id IS NOT NULL;
