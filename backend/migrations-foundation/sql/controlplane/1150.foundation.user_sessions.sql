-- @migration-class: expand

-- App-side device/session metadata for the "Active Sessions" UI.
--
-- The platform uses a BFF (backend-for-frontend) OIDC model, so Keycloak's admin
-- session list only ever shows the backend client ("orkyo-backend") and the
-- backend/proxy container IP — never the real browser device or client IP. We
-- therefore capture the real device/IP at BFF login and join it back to the live
-- Keycloak session list by the Keycloak `sid` (= KeycloakSession.Id) when listing
-- sessions. Purely additive; the sessions endpoint falls back to Keycloak values
-- for any session without a row here.
--
-- GDPR: ip_address + user_agent are personal data. Rows cascade-delete with the
-- user and are pruned when the Keycloak session ends (revoke / logout-all / no
-- longer live), so retention is bounded by the session lifetime.

CREATE TABLE public.user_sessions (
    id                  uuid                     DEFAULT gen_random_uuid() NOT NULL,
    user_id             uuid                     NOT NULL,
    keycloak_session_id character varying(255)   NOT NULL,
    ip_address          character varying(45),
    user_agent          text,
    browser             character varying(60),
    operating_system    character varying(60),
    device_type         character varying(20),
    created_at          timestamp with time zone DEFAULT now() NOT NULL,
    last_seen_at        timestamp with time zone DEFAULT now() NOT NULL,

    CONSTRAINT user_sessions_pkey PRIMARY KEY (id),
    CONSTRAINT user_sessions_keycloak_session_id_key UNIQUE (keycloak_session_id),
    CONSTRAINT user_sessions_user_id_fkey
        FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE
);

CREATE INDEX idx_user_sessions_user_id ON public.user_sessions (user_id);

COMMENT ON TABLE  public.user_sessions                     IS 'Per-device session metadata captured at BFF login, joined to live Keycloak sessions by sid';
COMMENT ON COLUMN public.user_sessions.keycloak_session_id IS 'Keycloak sid claim (= admin session id); join key + upsert key';
COMMENT ON COLUMN public.user_sessions.ip_address          IS 'Real client IP at login (personal data; GDPR cascade-delete + prune)';
COMMENT ON COLUMN public.user_sessions.user_agent          IS 'Raw User-Agent at login (personal data; GDPR cascade-delete + prune)';
COMMENT ON COLUMN public.user_sessions.device_type         IS 'desktop | mobile | tablet | unknown (parsed from user_agent)';
