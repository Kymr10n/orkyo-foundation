/*
 * Harness-only override for @foundation/src/hooks/usePermissions.
 *
 * The real hook derives edit rights from AuthContext (which runs the auth state
 * machine and bootstraps a backend session). This harness has no backend, so we
 * pin the permission gate to "can edit" — exactly what the vitest setup does
 * (src/test/setup.ts) — and re-export everything else from the real module.
 *
 * Wired via a vite alias in vite.config.ts. The relative import below bypasses
 * that alias and hits the real file, so there is no resolution loop.
 */
export { TENANT_ROLE } from "../../src/hooks/usePermissions";

export const useCanEdit = (): boolean => true;
export const useIsTenantAdmin = (): boolean => true;
