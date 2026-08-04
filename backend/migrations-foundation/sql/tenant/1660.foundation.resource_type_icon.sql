-- @migration-class: expand
-- Per-type nav/list icon. Holds a lucide-react icon name; the frontend resolves it
-- against a curated allow-list and falls back to a default when the name is unknown
-- or NULL, so an unrecognised value degrades to the old behaviour rather than
-- breaking the nav. Deliberately not a FK or CHECK: the valid set is a property of
-- the frontend bundle, not of the database.

ALTER TABLE public.resource_types
    ADD COLUMN icon VARCHAR(50);

-- Seed the system types with the icons their dedicated pages already use.
UPDATE public.resource_types SET icon = 'Box'    WHERE key = 'space';
UPDATE public.resource_types SET icon = 'Users'  WHERE key = 'person';
UPDATE public.resource_types SET icon = 'Wrench' WHERE key = 'tool';
