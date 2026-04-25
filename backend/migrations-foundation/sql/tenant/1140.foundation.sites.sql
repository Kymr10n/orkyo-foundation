-- Sites — top-level deployment locations within a tenant. Folds V007 idx_sites_name.

CREATE TABLE public.sites (
    id                              uuid                     DEFAULT gen_random_uuid() NOT NULL,
    name                            character varying(255)   NOT NULL,
    code                            character varying(63)    NOT NULL,
    description                     text,
    address                         text,
    attributes                      jsonb,
    floorplan_image_path            character varying(500),
    floorplan_mime_type             character varying(100),
    floorplan_file_size_bytes       bigint,
    floorplan_width_px              integer,
    floorplan_height_px             integer,
    floorplan_uploaded_at           timestamp with time zone,
    floorplan_uploaded_by_user_id   uuid,
    created_at                      timestamp with time zone DEFAULT now() NOT NULL,
    updated_at                      timestamp with time zone DEFAULT now() NOT NULL,

    CONSTRAINT sites_pkey PRIMARY KEY (id),
    CONSTRAINT sites_code_key UNIQUE (code),
    CONSTRAINT sites_floorplan_uploaded_by_user_id_fkey
        FOREIGN KEY (floorplan_uploaded_by_user_id) REFERENCES public.users(id) ON DELETE SET NULL
);

CREATE INDEX idx_sites_code           ON public.sites USING btree (code);
CREATE INDEX idx_sites_name           ON public.sites USING btree (name);
CREATE INDEX idx_sites_floorplan_path ON public.sites USING btree (floorplan_image_path) WHERE (floorplan_image_path IS NOT NULL);

CREATE TRIGGER update_sites_updated_at
    BEFORE UPDATE ON public.sites
    FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();
