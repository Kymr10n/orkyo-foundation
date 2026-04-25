-- Tenant-level / site-level invite tokens.

CREATE TABLE public.invites (
    id                    uuid                     DEFAULT gen_random_uuid() NOT NULL,
    email                 character varying(320)   NOT NULL,
    invited_by_user_id    uuid,
    scope                 character varying(20)    NOT NULL,
    site_id               uuid,
    role                  character varying(50)    NOT NULL,
    token_hash            character varying(255)   NOT NULL,
    expires_at            timestamp with time zone NOT NULL,
    accepted_at           timestamp with time zone,
    created_at            timestamp with time zone DEFAULT now() NOT NULL,

    CONSTRAINT invites_pkey PRIMARY KEY (id),
    CONSTRAINT invites_scope_check
        CHECK (((scope)::text = ANY ((ARRAY['tenant'::character varying, 'site'::character varying])::text[]))),
    CONSTRAINT invites_invited_by_user_id_fkey
        FOREIGN KEY (invited_by_user_id) REFERENCES public.users(id) ON DELETE SET NULL,
    CONSTRAINT invites_site_id_fkey
        FOREIGN KEY (site_id) REFERENCES public.sites(id) ON DELETE CASCADE
);

CREATE INDEX idx_invites_email      ON public.invites USING btree (email);
CREATE INDEX idx_invites_expires_at ON public.invites USING btree (expires_at);
CREATE INDEX idx_invites_token_hash ON public.invites USING btree (token_hash);
