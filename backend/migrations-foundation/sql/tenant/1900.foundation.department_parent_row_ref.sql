-- @migration-class: data

-- The department Parent column becomes a real reference again.
--
-- 1820 kept the parent as a name in a text cell and said what that meant: nothing prevents a
-- cycle, a dangling parent name, or two siblings sharing a name. 1890 added the `row_ref` column
-- type, and this migration puts the seeded Departments list on it — the column changes type and
-- every stored name is rewritten as the id of the row it names.
--
-- WHAT IS PRESERVED: every parent that names exactly one other department in the same list. 1820
-- carried the old primary keys over as row ids and copied the parent straight off the foreign
-- key, so on a tenant that has not renamed a department by hand since, every value matches and
-- the tree comes back exactly as the old FK held it.
--
-- WHAT IS LOST, deliberately: a parent that matches no department, or matches two, is removed
-- rather than kept. Keeping the text would leave a cell that fails row_ref validation on every
-- later save of that row — a whole row is validated as one document — so the row could be read
-- and never edited again. The value being dropped was descriptive only in 1820's own words, and
-- dropping it is the smaller harm than a row nobody can save.
--
-- Cycles from the free-text era are broken the same way and for the same reason: the old FK
-- forbade them, the text column did not, and the service refuses to save a row whose parent chain
-- comes back to it.
--
-- A tenant that renamed or deleted the seeded definition gets no changes rather than an error.

BEGIN;

DO $$
DECLARE
    dept_def UUID;
BEGIN
    SELECT id INTO dept_def
    FROM public.list_definitions
    WHERE name = 'Departments' AND scope = 'organization';

    IF dept_def IS NULL THEN RETURN; END IF;

    UPDATE public.list_columns
       SET data_type = 'row_ref',
           description = 'The unit this one sits under.'
     WHERE list_definition_id = dept_def
       AND key = 'parent'
       AND data_type = 'text';

    IF NOT FOUND THEN RETURN; END IF;

    -- Name to id, per instance. A name matching exactly one other row becomes that row's id;
    -- anything else loses the cell. `count(p.id)` counts matches, so no match counts zero.
    UPDATE public.list_rows r
       SET values = CASE
                        WHEN m.matches = 1
                            THEN jsonb_set(r.values, '{parent}', to_jsonb(m.parent_id::text))
                        ELSE r.values - 'parent'
                    END
      FROM (
          SELECT c.id                    AS row_id,
                 count(p.id)             AS matches,
                 min(p.id::text)::uuid   AS parent_id
            FROM public.list_rows c
            JOIN public.list_instances i ON i.id = c.list_instance_id
            LEFT JOIN public.list_rows p
                   ON p.list_instance_id = c.list_instance_id
                  AND p.id <> c.id
                  AND p.values ->> 'name' = c.values ->> 'parent'
           WHERE i.list_definition_id = dept_def
             AND c.values ? 'parent'
           GROUP BY c.id
      ) m
     WHERE r.id = m.row_id;

    -- Break what the text era allowed and the service now refuses. Only a row that its own parent
    -- chain returns to is stripped; a row hanging below a cycle keeps its parent.
    WITH RECURSIVE walk (start_id, current_id, path) AS (
        SELECT r.id, (r.values ->> 'parent')::uuid, ARRAY[r.id]
          FROM public.list_rows r
          JOIN public.list_instances i ON i.id = r.list_instance_id
         WHERE i.list_definition_id = dept_def
           AND r.values ? 'parent'
        UNION ALL
        SELECT w.start_id, (p.values ->> 'parent')::uuid, w.path || p.id
          FROM walk w
          JOIN public.list_rows p ON p.id = w.current_id
         WHERE p.values ? 'parent'
           AND NOT (p.id = ANY (w.path))
    )
    UPDATE public.list_rows r
       SET values = r.values - 'parent'
     WHERE r.id IN (SELECT start_id FROM walk WHERE current_id = start_id);
END $$;

COMMIT;
