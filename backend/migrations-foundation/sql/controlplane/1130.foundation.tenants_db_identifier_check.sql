-- @migration-class: expand

-- Defense-in-depth for tenant isolation: tenants.db_identifier is used to build
-- the per-tenant database connection (via NpgsqlConnectionStringBuilder), so it
-- must never contain connection-string / SQL metacharacters. Application code in
-- TenantProvisioningService already validates the stricter ^[a-z0-9_]+$ on the way
-- in; this constraint is the database-level backstop in case a row is ever written
-- through another path. It is deliberately a touch looser than the app contract
-- (it also permits hyphens) so it blocks every injection vector without rejecting
-- any value the system legitimately produces. Additive and rollback-safe.

ALTER TABLE public.tenants
    ADD CONSTRAINT tenants_db_identifier_safe_check
    CHECK (db_identifier ~ '^[a-z0-9_-]+$');
