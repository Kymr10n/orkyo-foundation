-- @migration-class: data
--
-- Backfill rule (spec: criterion applicability scope).
-- After this migration, the API contract requires every criterion to carry
-- at least one resource_type applicability tag. Criteria created before this
-- requirement existed have no entries in criterion_resource_types and were
-- treated as universally applicable by an open-world fallback in
-- CriteriaRepository.GetByResourceTypeAsync.
--
-- The fallback is being removed in the same release. To preserve the practical
-- effect for the dominant historical use case (criteria as space capabilities),
-- this migration tags every untagged criterion with 'space'. Admins re-tag for
-- People/Tools applicability via the Edit dialog as needed.

INSERT INTO public.criterion_resource_types (criterion_id, resource_type_id)
SELECT c.id, rt.id
FROM public.criteria c
CROSS JOIN public.resource_types rt
WHERE rt.key = 'space'
  AND NOT EXISTS (
      SELECT 1
      FROM public.criterion_resource_types crt
      WHERE crt.criterion_id = c.id
  )
ON CONFLICT DO NOTHING;
