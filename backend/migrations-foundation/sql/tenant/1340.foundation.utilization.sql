-- @migration-class: none
-- Phase 5: Utilization — no schema changes required.
-- Utilization is computed live from existing tables (resource_assignments,
-- off_time_resources, resources). This migration exists for sequencing only.

SELECT 1;
