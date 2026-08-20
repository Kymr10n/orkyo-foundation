-- @migration-class: data

-- The last built-in types stop being built in.
--
-- 1800 demoted space; person and tool stayed system because the directory machinery and the
-- seed still leaned on them. Both reasons are gone: directory behaviour hangs off
-- has_directory_profile (1700/1710), and the seed defines its own types. Pre-configured types
-- now come from the application-layer catalog (Configuration → Type catalog), which
-- instantiates ordinary tenant types — so no row may claim to be system any more.
--
-- After this, is_system has no true value and no writer. The column stays: dropping it is a
-- contract migration and a compiled-model change, out of scope here.

UPDATE public.resource_types
SET is_system = false, updated_at = CURRENT_TIMESTAMP
WHERE is_system = true;
