ALTER TABLE public.sites
    ADD COLUMN IF NOT EXISTS floorplan_image_path character varying(500),
    ADD COLUMN IF NOT EXISTS floorplan_mime_type character varying(100),
    ADD COLUMN IF NOT EXISTS floorplan_file_size_bytes bigint,
    ADD COLUMN IF NOT EXISTS floorplan_width_px integer,
    ADD COLUMN IF NOT EXISTS floorplan_height_px integer,
    ADD COLUMN IF NOT EXISTS floorplan_uploaded_at timestamp with time zone,
    ADD COLUMN IF NOT EXISTS floorplan_uploaded_by_user_id uuid;

ALTER TABLE public.sites
    ADD CONSTRAINT sites_floorplan_uploaded_by_user_id_fkey
        FOREIGN KEY (floorplan_uploaded_by_user_id) REFERENCES public.users(id) ON DELETE SET NULL;

CREATE INDEX IF NOT EXISTS idx_sites_floorplan_path
    ON public.sites USING btree (floorplan_image_path)
    WHERE (floorplan_image_path IS NOT NULL);

DROP TABLE IF EXISTS public.assets;
