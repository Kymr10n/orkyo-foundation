-- @migration-class: expand

-- An index for the custom-field predicates that 1770-1830 introduced.
--
-- Three query paths filter resources on custom_fields with jsonb operators and
-- until now none of them had an index:
--
--   * ListInstanceRepository.DeleteRowAsync strips a deleted list row's id from
--     every lookup value with `jsonb_exists(custom_fields, key)` (the `?`
--     operator) and `custom_fields -> key @> to_jsonb(rowId)`.
--   * ResourceCustomFieldRepository uses the same shape when a field is deleted.
--   * refresh_search_resource() (1830) resolves organization labels through the
--     picked row ids in custom_fields.
--
-- Without the index each of these is a sequential scan over every resource of
-- the type. With organization lists live (1820), a single department-row delete
-- pays that cost.
--
-- Default jsonb_ops, not jsonb_path_ops: jsonb_path_ops only supports `@>`,
-- and the delete paths also use `?` (jsonb_exists).
--
-- CONCURRENTLY cannot run inside a transaction. The runner does not wrap
-- scripts, so this runs in autocommit (the 1720 precedent).

CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_resources_custom_fields_gin
    ON public.resources USING GIN (custom_fields);
