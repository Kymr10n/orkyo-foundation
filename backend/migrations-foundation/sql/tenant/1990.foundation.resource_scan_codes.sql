-- @migration-class: expand

-- QR stickers on resources (docs/qr-resource-linking-spec.md).
--
-- A user sticks an existing QR label on a resource, links it from the resource dialog, and
-- a later scan finds the resource. Orkyo does not print these codes, so a code is the decoded
-- text exactly as the sticker carries it: opaque, case-sensitive, never parsed or followed.
--
-- A table, not resources.external_reference: that column is the ERP/serial number, is not
-- unique, and is cleared on duplicate. Here one resource can carry several codes (a
-- replacement sticker, both sides of a machine) and one code names exactly one resource. The
-- tenant database is the scope, so two tenants can own the same sticker text.
--
-- resource_types.scan_codes_enabled is the per-type switch. Existing types get it on except
-- types with directory details (people), which rarely carry a sticker. Switching it off keeps
-- the links; a scan then refuses to name the resource.
--
-- created_by_user_id has no foreign key, like updated_by_user_id in 1920 and 1940: the actor
-- is informational and must not block a link made by a principal with no tenant user row.
--
-- Rollback: drop the table and the column.

ALTER TABLE public.resource_types
    ADD COLUMN IF NOT EXISTS scan_codes_enabled BOOLEAN NOT NULL DEFAULT true;

COMMENT ON COLUMN public.resource_types.scan_codes_enabled IS
    'Resources of this type can have QR codes linked, and a scan of such a code finds them.';

UPDATE public.resource_types SET scan_codes_enabled = NOT has_directory_profile;

CREATE TABLE public.resource_scan_codes (
    id                 uuid                     DEFAULT gen_random_uuid() NOT NULL,
    resource_id        uuid                     NOT NULL,
    code               text                     NOT NULL,
    created_by_user_id uuid                     NULL,
    created_at         timestamp with time zone DEFAULT now() NOT NULL,

    CONSTRAINT resource_scan_codes_pkey PRIMARY KEY (id),
    CONSTRAINT resource_scan_codes_resource_fkey
        FOREIGN KEY (resource_id) REFERENCES public.resources(id) ON DELETE CASCADE,
    CONSTRAINT resource_scan_codes_code_length CHECK (char_length(code) BETWEEN 1 AND 512),
    CONSTRAINT resource_scan_codes_code_unique UNIQUE (code)
);

COMMENT ON TABLE public.resource_scan_codes IS
    'QR codes linked to a resource. The code is the decoded sticker text, unique per tenant.';

-- "Which codes does this resource carry" for the edit dialog, and the cascade on delete.
-- The runner does not wrap scripts, so CONCURRENTLY is legal here.
CREATE INDEX CONCURRENTLY idx_resource_scan_codes_resource
    ON public.resource_scan_codes (resource_id);
