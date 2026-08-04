-- @migration-class: expand
-- Makes criteria a superset of the resource_type_fields system, so resources need only
-- ONE way to describe an attribute.
--
-- Two parallel systems existed: criteria (matchable — a request can require them, the
-- solver reasons over them) and resource_type_fields (descriptive only, values in
-- resources.metadata_json). Every attribute had to be modelled twice or the modeller had
-- to guess which one a value would eventually be matched on. Criteria are the right
-- survivor: they already carry applicability, unit, enum values, in-use immutability and
-- the whole matching path. What they lacked is added here.
--
-- The retirement of resource_type_fields is a separate contract migration.

-- ── Date data type ────────────────────────────────────────────────────────────
-- Fields could express a date (purchase date, inspection due); criteria could not.
-- Values are stored as JSONB strings in yyyy-MM-dd, matching the field system.
ALTER TABLE public.criteria
    DROP CONSTRAINT criteria_data_type_check;

ALTER TABLE public.criteria
    ADD CONSTRAINT criteria_data_type_check
        CHECK (data_type IN ('Boolean', 'Number', 'String', 'Enum', 'Date'));

-- ── Value constraints ─────────────────────────────────────────────────────────
-- {"min":..,"max":..,"maxLength":..,"regex":".."} — the shape resource_type_fields
-- used, kept identical so the semantics transfer unchanged. NULL = unconstrained,
-- which is every criterion that exists today.
ALTER TABLE public.criteria
    ADD COLUMN validation_json JSONB;

COMMENT ON COLUMN public.criteria.validation_json IS
    'Optional value constraints: min/max (Number), maxLength/regex (String). NULL = unconstrained.';

-- ── Per-type presentation ─────────────────────────────────────────────────────
-- These belong on the join table, not on criteria: the same criterion can be
-- mandatory for one resource type and optional for another (a serial number may be
-- required on a tool but not on a space), which the field system could not express
-- at all because a field belonged to exactly one type.
ALTER TABLE public.criterion_resource_types
    ADD COLUMN is_required  BOOLEAN NOT NULL DEFAULT false,
    ADD COLUMN sort_order   INT     NOT NULL DEFAULT 0,
    ADD COLUMN show_on_form BOOLEAN NOT NULL DEFAULT true;

COMMENT ON COLUMN public.criterion_resource_types.show_on_form IS
    'Render this criterion on the resource create/edit form for this type. False keeps it '
    'assignable via the capabilities editor without crowding the form.';

-- Replaces idx_rtf_type: the form and list queries read a type''s criteria in display order.
CREATE INDEX CONCURRENTLY idx_criterion_resource_types_type_order
    ON public.criterion_resource_types (resource_type_id, sort_order);
