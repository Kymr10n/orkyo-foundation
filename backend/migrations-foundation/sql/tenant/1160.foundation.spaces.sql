-- Spaces — physical or logical sub-units of a site. Folds V008 capacity column.

CREATE TABLE public.spaces (
    id          uuid                     DEFAULT gen_random_uuid() NOT NULL,
    site_id     uuid                     NOT NULL,
    code        character varying(63),
    name        character varying(255)   NOT NULL,
    description text,
    is_physical boolean                  DEFAULT false NOT NULL,
    geometry    jsonb,
    properties  jsonb                    DEFAULT '{}'::jsonb,
    group_id    uuid,
    capacity    integer                  DEFAULT 1 NOT NULL,
    created_at  timestamp with time zone DEFAULT now() NOT NULL,
    updated_at  timestamp with time zone DEFAULT now() NOT NULL,

    CONSTRAINT spaces_pkey PRIMARY KEY (id),
    CONSTRAINT spaces_site_id_fkey  FOREIGN KEY (site_id)  REFERENCES public.sites(id)        ON DELETE CASCADE,
    CONSTRAINT spaces_group_id_fkey FOREIGN KEY (group_id) REFERENCES public.space_groups(id) ON DELETE SET NULL,
    CONSTRAINT check_physical_has_geometry
        CHECK ((((is_physical = true) AND (geometry IS NOT NULL)) OR (is_physical = false)))
);

CREATE INDEX idx_spaces_site_id   ON public.spaces USING btree (site_id);
CREATE INDEX idx_spaces_code      ON public.spaces USING btree (code) WHERE (code IS NOT NULL);
CREATE INDEX idx_spaces_site_code ON public.spaces USING btree (site_id, code);
CREATE INDEX idx_spaces_group_id  ON public.spaces USING btree (group_id);

CREATE TRIGGER update_spaces_updated_at
    BEFORE UPDATE ON public.spaces
    FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();
