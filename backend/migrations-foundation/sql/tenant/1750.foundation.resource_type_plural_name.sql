-- @migration-class: expand

-- Gives a resource type a plural name, so a list of them can be labelled correctly.
--
-- display_name is singular by design — it labels one resource ("Edit Car", the Tool slot on a
-- request). But a sidebar entry, a utilization tab and a management page title all name a
-- *collection*, and until now they had only the singular to work with: the Tools tab read
-- "Tool" and the People one read "Person".
--
-- Stored rather than derived. English pluralisation is irregular exactly where it matters here
-- ("Person" → "Persons" is wrong), tenants name types in their own language, and a naive
-- `|| 's'` would be a rule the product could never let them correct.
--
-- Rollback: drop the column.

BEGIN;

ALTER TABLE public.resource_types
    ADD COLUMN IF NOT EXISTS display_name_plural VARCHAR(100);

COMMENT ON COLUMN public.resource_types.display_name_plural IS
    'Plural form of display_name, for labels that name a collection (sidebar entry, '
    'utilization tab, page title). display_name stays singular, for labels that name one.';

-- Backfill: the three seeded types by hand, everything else by appending "s". That is a decent
-- guess for the simple English nouns tenants have been able to create so far, and it is now
-- editable, so a wrong guess is a correction rather than a defect.
UPDATE public.resource_types SET display_name_plural = 'People' WHERE key = 'person';
UPDATE public.resource_types SET display_name_plural = 'Spaces' WHERE key = 'space';
UPDATE public.resource_types SET display_name_plural = 'Tools'  WHERE key = 'tool';

UPDATE public.resource_types
   SET display_name_plural = display_name || 's'
 WHERE display_name_plural IS NULL;

ALTER TABLE public.resource_types
    ALTER COLUMN display_name_plural SET NOT NULL;

COMMIT;
