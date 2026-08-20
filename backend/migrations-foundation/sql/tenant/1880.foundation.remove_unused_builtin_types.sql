-- @migration-class: data

-- Remove the 1300-seeded types where nothing ever used them.
--
-- A fresh database gets its three rows from 1300 (immutable) and deletes them again here, so
-- new tenants start with an empty type table and pick from the catalog. A tenant that used a
-- type keeps it: the catalog links by key, so a kept person/tool row simply reads as an
-- already-activated catalog entry, and a kept space row stays an ordinary tenant type.
--
-- "Used" is any tenant-made reference: resources, groups, request targets, criterion
-- applicability (the 1470 backfill means a tenant with criteria keeps their types — that is
-- intent, not an accident), an availability event scoped to the type, or a custom field. The
-- department/job_title fields are exempt from that last check because 1820 created them on
-- every database that had a person type at migration time — without the exemption, person
-- would be immortal everywhere, including on fresh databases.

DELETE FROM public.resource_types rt
WHERE rt.key IN ('space', 'person', 'tool')
  AND NOT EXISTS (SELECT 1 FROM public.resources r WHERE r.resource_type_id = rt.id)
  AND NOT EXISTS (SELECT 1 FROM public.resource_groups g WHERE g.resource_type_id = rt.id)
  AND NOT EXISTS (SELECT 1 FROM public.request_target_resource_types t WHERE t.resource_type_id = rt.id)
  AND NOT EXISTS (SELECT 1 FROM public.criterion_resource_types c WHERE c.resource_type_id = rt.id)
  AND NOT EXISTS (SELECT 1 FROM public.availability_event_scopes s
                  WHERE s.target_type = 'resource_type' AND s.target_id = rt.id)
  AND NOT EXISTS (SELECT 1 FROM public.resource_custom_fields f
                  WHERE f.resource_type_id = rt.id
                    AND f.key NOT IN ('department', 'job_title'));
