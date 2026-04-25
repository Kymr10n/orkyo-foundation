-- ToS (Terms of Service) acceptances — tracks which users accepted which versions.

CREATE TABLE public.tos_acceptances (
    id                  uuid                     DEFAULT gen_random_uuid() NOT NULL,
    user_id             uuid                     NOT NULL,
    tos_version         character varying(20)    NOT NULL,
    accepted_at         timestamp with time zone DEFAULT now() NOT NULL,
    accepted_ip         character varying(45),
    accepted_user_agent text,

    CONSTRAINT tos_acceptances_pkey PRIMARY KEY (id),
    CONSTRAINT tos_acceptances_user_id_fkey
        FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE
);

CREATE UNIQUE INDEX idx_tos_acceptances_user_version ON public.tos_acceptances (user_id, tos_version);
CREATE INDEX idx_tos_acceptances_user_id             ON public.tos_acceptances (user_id);
CREATE INDEX idx_tos_acceptances_version             ON public.tos_acceptances (tos_version);

COMMENT ON TABLE  public.tos_acceptances                    IS 'Tracks user acceptance of Terms of Service versions';
COMMENT ON COLUMN public.tos_acceptances.tos_version        IS 'Version identifier (e.g., "2026-02")';
COMMENT ON COLUMN public.tos_acceptances.accepted_ip        IS 'IP address at time of acceptance (for audit trail)';
COMMENT ON COLUMN public.tos_acceptances.accepted_user_agent IS 'User agent string at time of acceptance (for audit trail)';
