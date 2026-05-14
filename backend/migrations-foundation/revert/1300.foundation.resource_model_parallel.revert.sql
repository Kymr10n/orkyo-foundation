-- Revert migration 1300: drop all Phase 1 resource model objects.
-- Safe to run on a fresh DB; all objects are dropped IF EXISTS.

DROP TABLE IF EXISTS public.criteria_applicability_phase1;
DROP TABLE IF EXISTS public.resource_capabilities_phase1;
DROP TABLE IF EXISTS public.criterion_resource_types;
DROP TABLE IF EXISTS public.resource_assignments;
DROP TABLE IF EXISTS public.resources;
DROP TABLE IF EXISTS public.resource_types;
