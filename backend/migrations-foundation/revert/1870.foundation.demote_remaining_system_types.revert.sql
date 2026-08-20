-- Reverts 1870. After 1800 only person and tool can have been demoted by it.

UPDATE public.resource_types
SET is_system = true, updated_at = CURRENT_TIMESTAMP
WHERE key IN ('person', 'tool');
