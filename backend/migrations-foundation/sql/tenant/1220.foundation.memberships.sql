-- Site memberships — per-tenant per-site role assignments.

CREATE TABLE public.memberships (
    id         uuid                     DEFAULT gen_random_uuid() NOT NULL,
    user_id    uuid                     NOT NULL,
    site_id    uuid                     NOT NULL,
    role       character varying(50)    DEFAULT 'read_only'::character varying NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,

    CONSTRAINT memberships_pkey PRIMARY KEY (id),
    CONSTRAINT memberships_user_id_site_id_key UNIQUE (user_id, site_id),
    CONSTRAINT memberships_role_check
        CHECK (((role)::text = ANY ((ARRAY['read_only'::character varying, 'writer'::character varying, 'site_admin'::character varying])::text[]))),
    CONSTRAINT memberships_user_id_fkey
        FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE,
    CONSTRAINT memberships_site_id_fkey
        FOREIGN KEY (site_id) REFERENCES public.sites(id) ON DELETE CASCADE
);

CREATE INDEX idx_memberships_user_id ON public.memberships USING btree (user_id);
CREATE INDEX idx_memberships_site_id ON public.memberships USING btree (site_id);

CREATE TRIGGER update_memberships_updated_at
    BEFORE UPDATE ON public.memberships
    FOR EACH ROW EXECUTE FUNCTION public.update_updated_at_column();
