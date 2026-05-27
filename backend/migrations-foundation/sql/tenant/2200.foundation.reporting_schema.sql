-- @migration-class: expand
-- Reporting schema for Superset embedded BI (MVP Phase 1).
--
-- Creates a dedicated 'reporting' schema and a per-tenant reader role.
-- Postgres roles are cluster-wide, so the role is named after the database
-- to stay unique across tenants sharing the same Postgres instance.
--
-- LOGIN + PASSWORD are managed by TenantReportingProvisioner at provisioning
-- time; this migration only creates the schema and role skeleton.

CREATE SCHEMA IF NOT EXISTS reporting;

-- Deny all public access to the reporting schema; access is granted only to
-- the rpt_reader role and to the DB owner (who runs migrations + provisioning).
REVOKE ALL ON SCHEMA reporting FROM PUBLIC;

DO $$
DECLARE
    _role text := current_database() || '_rpt_reader';
BEGIN
    -- Idempotent: guard prevents duplicate-role errors if migration is replayed.
    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = _role) THEN
        EXECUTE format(
            'CREATE ROLE %I '
            'NOSUPERUSER NOCREATEDB NOCREATEROLE NOINHERIT NOREPLICATION NOBYPASSRLS '
            'CONNECTION LIMIT 5',
            _role
        );
    END IF;

    -- CONNECT is required for the role to open a session against this DB at all.
    EXECUTE format('GRANT CONNECT ON DATABASE %I TO %I', current_database(), _role);

    -- USAGE lets the role resolve object names inside the schema.
    EXECUTE format('GRANT USAGE ON SCHEMA reporting TO %I', _role);
END
$$;
