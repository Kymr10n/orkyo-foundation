-- @migration-class: contract

-- Retires the second attribute system. Resources described their attributes two ways:
-- criteria (matchable — a request can require them, the solver reasons over them) and
-- resource_type_fields (descriptive only, values in resources.metadata_json). Every attribute
-- had to be modelled twice, or the modeller had to guess up front which one a value would
-- eventually be matched on.
--
-- Migration 1670 gave criteria everything fields had and they lacked — a Date data type,
-- value constraints, and per-type required/order/visibility — so fields carry nothing unique
-- anymore. One way to describe a resource attribute, and it is the one the solver understands.
--
-- Data loss: none in practice. resource_type_fields was introduced by migration 1650 on this
-- same unreleased branch, nothing seeds it, and metadata_json sat unused from 1300 until 1650
-- wired it up. The only possible rows are ones a tenant hand-created through the settings UI
-- between 1650 and here, which no released build ever exposed.
--
-- 1650 is deliberately left in place rather than deleted: the runner stores a checksum per
-- applied migration, so a file that vanished after being applied locally is a worse problem
-- than a pair of migrations that cancel out.
--
-- Rollback: recreate the table from 1650.foundation.resource_type_fields and re-add
-- resources.metadata_json JSONB. Values are not recoverable, matching the note above.

BEGIN;

DROP TABLE IF EXISTS public.resource_type_fields;

ALTER TABLE public.resources
    DROP COLUMN IF EXISTS metadata_json;

COMMIT;
