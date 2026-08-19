-- @migration-class: expand

-- Departments and job titles become organization-scoped lists.
--
-- Until now they were two bespoke tables with their own repositories, endpoints and settings
-- pages, referenced from resources by foreign key. 1810 gave list definitions an owner, which
-- makes them able to carry organization master data, and this migration moves the two of them
-- across so that "the organization's reference data" is one mechanism a tenant can extend
-- instead of two hardcoded tables plus a wish.
--
-- WHAT IS LOST, deliberately and with the cost accepted up front:
--
--   * The department tree. `departments.parent_department_id` is a real self-referencing FK with
--     a cycle check and sibling-name uniqueness. A list row is flat JSON. The parent survives as
--     a text column so no information disappears from view, but the database stops enforcing it:
--     nothing prevents a cycle, a dangling parent name, or two siblings sharing a name.
--   * Referential integrity from resources. `department_id` and `job_title_id` are foreign keys;
--     the replacement is a `list_lookup` custom field whose value is a JSON array of row ids,
--     validated on write by the application and by nothing at rest. Deleting a row that resources
--     still point at leaves those references dangling.
--   * The 500-row cap per instance now applies. A tenant with more than 500 departments cannot
--     model them this way.
--
-- WHAT IS PRESERVED: every name, code, description and active flag, and every person's
-- assignment. Row ids are carried over verbatim from the old primary keys, so a value copied into
-- `resources.custom_fields` is the same identifier the FK held. That makes the migration
-- auditable after the fact and the contract step a pure drop.
--
-- The custom fields land on the tenant's directory type, preferring `person` when several
-- declare a profile. A tenant that deactivated its person type gets the definitions and no
-- fields, which is the correct outcome rather than an error.

BEGIN;

-- ---------------------------------------------------------------------------------------------
-- Definitions, columns and one shared instance each.
-- ---------------------------------------------------------------------------------------------

DO $$
DECLARE
    dept_def      UUID;
    dept_instance UUID;
    dept_name_col UUID;
    job_def       UUID;
    job_instance  UUID;
    job_name_col  UUID;
    person_type   UUID;
BEGIN
    INSERT INTO public.list_definitions (name, description, scope)
    VALUES ('Departments', 'Organizational units.', 'organization')
    RETURNING id INTO dept_def;

    INSERT INTO public.list_columns (list_definition_id, key, label, data_type, is_required, sort_order)
    VALUES (dept_def, 'name', 'Name', 'text', true, 0)
    RETURNING id INTO dept_name_col;

    INSERT INTO public.list_columns (list_definition_id, key, label, description, data_type, is_required, sort_order)
    VALUES
        (dept_def, 'code', 'Code', NULL, 'text', false, 1),
        (dept_def, 'description', 'Description', NULL, 'text', false, 2),
        -- The former parent, by name. Descriptive only: the tree is no longer enforced.
        (dept_def, 'parent', 'Parent', 'The unit this one sat under before departments became a list.', 'text', false, 3),
        (dept_def, 'active', 'Active', NULL, 'boolean', false, 4);

    UPDATE public.list_definitions SET display_column_id = dept_name_col WHERE id = dept_def;

    INSERT INTO public.list_instances (list_definition_id, kind, name)
    VALUES (dept_def, 'shared', 'Departments')
    RETURNING id INTO dept_instance;

    INSERT INTO public.list_definitions (name, description, scope)
    VALUES ('Job Titles', 'Roles a person holds.', 'organization')
    RETURNING id INTO job_def;

    INSERT INTO public.list_columns (list_definition_id, key, label, data_type, is_required, sort_order)
    VALUES (job_def, 'name', 'Name', 'text', true, 0)
    RETURNING id INTO job_name_col;

    INSERT INTO public.list_columns (list_definition_id, key, label, data_type, is_required, sort_order)
    VALUES
        (job_def, 'description', 'Description', 'text', false, 1),
        (job_def, 'active', 'Active', 'boolean', false, 2);

    UPDATE public.list_definitions SET display_column_id = job_name_col WHERE id = job_def;

    INSERT INTO public.list_instances (list_definition_id, kind, name)
    VALUES (job_def, 'shared', 'Job Titles')
    RETURNING id INTO job_instance;

    -- -----------------------------------------------------------------------------------------
    -- Rows, keeping the old primary keys as row ids.
    -- -----------------------------------------------------------------------------------------

    INSERT INTO public.list_rows (id, list_instance_id, values, created_at, updated_at)
    SELECT
        d.id,
        dept_instance,
        jsonb_strip_nulls(jsonb_build_object(
            'name',        d.name,
            'code',        d.code,
            'description', d.description,
            'parent',      parent.name,
            'active',      d.is_active
        )),
        d.created_at,
        d.updated_at
    FROM public.departments d
    LEFT JOIN public.departments parent ON parent.id = d.parent_department_id;

    INSERT INTO public.list_rows (id, list_instance_id, values, created_at, updated_at)
    SELECT
        j.id,
        job_instance,
        jsonb_strip_nulls(jsonb_build_object(
            'name',        j.name,
            'description', j.description,
            'active',      j.is_active
        )),
        j.created_at,
        j.updated_at
    FROM public.job_titles j;

    -- -----------------------------------------------------------------------------------------
    -- Lookup fields on the directory type, and every person's current assignment.
    -- -----------------------------------------------------------------------------------------

    SELECT id INTO person_type
    FROM public.resource_types
    WHERE has_directory_profile
    ORDER BY (key = 'person') DESC, key
    LIMIT 1;

    IF person_type IS NOT NULL THEN
        INSERT INTO public.resource_custom_fields
            (resource_type_id, key, label, data_type, is_required, sort_order, list_instance_id)
        VALUES
            (person_type, 'department', 'Department', 'list_lookup', false, 100, dept_instance),
            (person_type, 'job_title',  'Job Title',  'list_lookup', false, 101, job_instance);

        -- One statement per field rather than one combined object, so a resource holding only one
        -- of the two keeps exactly that. `||` merges at the top level, which is what is wanted:
        -- every other custom field on the document survives untouched.
        UPDATE public.resources r
        SET custom_fields = COALESCE(r.custom_fields, '{}'::jsonb)
            || jsonb_build_object('department', jsonb_build_array(r.department_id::text))
        WHERE r.department_id IS NOT NULL
          AND r.resource_type_id = person_type;

        UPDATE public.resources r
        SET custom_fields = COALESCE(r.custom_fields, '{}'::jsonb)
            || jsonb_build_object('job_title', jsonb_build_array(r.job_title_id::text))
        WHERE r.job_title_id IS NOT NULL
          AND r.resource_type_id = person_type;
    END IF;
END $$;

COMMIT;
