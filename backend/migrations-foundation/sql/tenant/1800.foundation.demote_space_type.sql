-- @migration-class: data

-- The built-in space type stops being built in.
--
-- Since 1700 the behaviour that used to hang off the space *key* hangs off type flags
-- (has_geometry, single_group_membership), and by now every code path that defaulted to the key
-- resolves the tenant's placeable types instead: the solver's target, the request-create default,
-- schedule recalculation, export, presets and the workstation import. Nothing behavioural is left
-- that needs a type called "space" to exist.
--
-- Demotion, not deletion: is_system=false makes the row an ordinary tenant type — renamable to
-- "Rooms", deactivatable, and deletable through the same path as any tenant type once nothing
-- references it. Existing resources, groups, targets and assignments are untouched; they now
-- belong to a type the tenant owns. Deleting the row here would be refused by the database anyway
-- (request_target_resource_types restricts it) and would take tenants' rooms and booking history
-- with it, which is theirs to decide, not a migration's.
--
-- person and tool stay system types: person's directory machinery (job titles, departments,
-- user linking) is genuinely built in, and tool remains the untyped default the narrative uses.

UPDATE public.resource_types
SET is_system = false,
    updated_at = CURRENT_TIMESTAMP
WHERE key = 'space'
  AND is_system = true;
