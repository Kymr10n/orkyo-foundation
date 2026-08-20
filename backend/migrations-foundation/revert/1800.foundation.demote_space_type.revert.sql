-- Re-promotes the space type to a system type. Safe to run whether or not the tenant renamed it
-- in the meantime — the key is immutable, so it still finds the row.

UPDATE public.resource_types
SET is_system = true,
    updated_at = CURRENT_TIMESTAMP
WHERE key = 'space'
  AND is_system = false;
