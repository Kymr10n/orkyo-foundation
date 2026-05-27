-- @migration-class: expand
-- Control-plane tables for per-tenant Superset reporting provisioning state.
--
-- tenant_reporting_state — one row per tenant, tracks whether Superset has been
--   provisioned with a datasource for that tenant's reporting schema.
--
-- tenant_report_bindings — maps Orkyo report keys (e.g. 'space-utilization')
--   to Superset dashboard UUIDs for each tenant. Populated by the provisioner.

CREATE TABLE public.tenant_reporting_state (
    tenant_id              uuid        NOT NULL,
    datasource_uuid        uuid,
    last_provisioned_at    timestamptz,
    status                 text        NOT NULL DEFAULT 'unprovisioned',
    last_error             text,
    -- Increment when the reader role password is rotated so provisioner knows
    -- which Superset datasource credentials are stale.
    credentials_version    int         NOT NULL DEFAULT 1,

    CONSTRAINT tenant_reporting_state_pkey PRIMARY KEY (tenant_id),
    CONSTRAINT tenant_reporting_state_tenant_fkey
        FOREIGN KEY (tenant_id) REFERENCES public.tenants(id) ON DELETE CASCADE,
    CONSTRAINT tenant_reporting_state_status_check
        CHECK (status IN ('unprovisioned', 'provisioning', 'provisioned', 'failed'))
);

CREATE TABLE public.tenant_report_bindings (
    tenant_id       uuid        NOT NULL,
    report_key      text        NOT NULL,
    dashboard_uuid  uuid        NOT NULL,

    CONSTRAINT tenant_report_bindings_pkey
        PRIMARY KEY (tenant_id, report_key),
    CONSTRAINT tenant_report_bindings_tenant_fkey
        FOREIGN KEY (tenant_id) REFERENCES public.tenants(id) ON DELETE CASCADE
);

CREATE INDEX idx_tenant_report_bindings_tenant ON public.tenant_report_bindings (tenant_id);
