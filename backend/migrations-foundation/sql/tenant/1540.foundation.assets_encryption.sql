-- @migration-class: expand

-- At-rest encryption metadata for asset blobs (site floorplans).
-- After this lands, newly written assets store an Orkyo binary encryption
-- envelope in `data`, described by enc_algorithm / enc_key_version.
-- A NULL enc_algorithm marks a legacy plaintext blob (pre-backfill); the
-- application reads those as-is and a backfill task encrypts them in place.

BEGIN;

ALTER TABLE public.assets
    ADD COLUMN IF NOT EXISTS enc_algorithm   text    NULL,
    ADD COLUMN IF NOT EXISTS enc_key_version integer NULL;

COMMIT;
