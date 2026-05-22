-- @migration-class: contract

-- PostgreSQL-backed generic assets. Site floorplans are the first asset type
-- and replace the legacy filesystem-backed sites.floorplan_* columns.

BEGIN;

CREATE TABLE public.assets (
    id                  uuid                     DEFAULT gen_random_uuid() NOT NULL,
    tenant_id           uuid                     NOT NULL,
    owner_type          character varying(50)    NOT NULL,
    owner_id            uuid                     NOT NULL,
    asset_type          character varying(50)    NOT NULL,
    file_name           character varying(255)   NOT NULL,
    content_type        character varying(100)   NOT NULL,
    size_bytes          bigint                   NOT NULL,
    checksum_sha256     character varying(64)    NOT NULL,
    width_px            integer,
    height_px           integer,
    storage_kind        character varying(30)    NOT NULL,
    data                bytea,
    external_uri        text,
    created_at          timestamp with time zone DEFAULT now() NOT NULL,
    updated_at          timestamp with time zone DEFAULT now() NOT NULL,
    created_by_user_id  uuid,
    updated_by_user_id  uuid,

    CONSTRAINT assets_pkey PRIMARY KEY (id),
    CONSTRAINT assets_owner_type_check CHECK (owner_type IN ('site')),
    CONSTRAINT assets_asset_type_check CHECK (asset_type IN ('floorplan')),
    CONSTRAINT assets_storage_kind_check CHECK (storage_kind IN ('postgres')),
    CONSTRAINT assets_size_bytes_check CHECK (size_bytes > 0),
    CONSTRAINT assets_checksum_sha256_check CHECK (checksum_sha256 ~ '^[0-9a-f]{64}$'),
    CONSTRAINT assets_postgres_storage_check CHECK (
        storage_kind <> 'postgres' OR (data IS NOT NULL AND external_uri IS NULL)
    ),
    CONSTRAINT assets_created_by_user_fkey
        FOREIGN KEY (created_by_user_id) REFERENCES public.users(id) ON DELETE SET NULL,
    CONSTRAINT assets_updated_by_user_fkey
        FOREIGN KEY (updated_by_user_id) REFERENCES public.users(id) ON DELETE SET NULL
);

CREATE INDEX idx_assets_tenant_owner
    ON public.assets (tenant_id, owner_type, owner_id);

CREATE INDEX idx_assets_tenant_asset_type
    ON public.assets (tenant_id, asset_type);

CREATE UNIQUE INDEX ux_assets_owner_asset_type_floorplan
    ON public.assets (tenant_id, owner_type, owner_id, asset_type)
    WHERE asset_type = 'floorplan';

CREATE TRIGGER assets_updated_at
    BEFORE UPDATE ON public.assets
    FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();

DROP INDEX IF EXISTS public.idx_sites_floorplan_path;

ALTER TABLE public.sites
    DROP CONSTRAINT IF EXISTS sites_floorplan_uploaded_by_user_id_fkey,
    DROP COLUMN IF EXISTS floorplan_image_path,
    DROP COLUMN IF EXISTS floorplan_mime_type,
    DROP COLUMN IF EXISTS floorplan_file_size_bytes,
    DROP COLUMN IF EXISTS floorplan_width_px,
    DROP COLUMN IF EXISTS floorplan_height_px,
    DROP COLUMN IF EXISTS floorplan_uploaded_at,
    DROP COLUMN IF EXISTS floorplan_uploaded_by_user_id;

COMMIT;
