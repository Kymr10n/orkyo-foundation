import { useMutation } from "@tanstack/react-query";
import { transferTenantOwnership, updateTenant } from "@foundation/src/lib/api/tenant-management-api";
import { deleteTenant } from "@foundation/src/lib/api/tenant-account-api";
import { exportTenantData } from "@foundation/src/lib/api/export-api";
import { downloadFile } from "@foundation/src/lib/utils/import-export";
import { formatDateForInput } from "@foundation/src/lib/utils";

// The organization settings card's writes. Each reports through its `meta` toast (none of
// them runs inside a dialog that stays open), and none invalidates: the membership the card
// reads comes from the auth context, not a query.

export const useRenameTenant = () =>
  useMutation({
    mutationFn: async ({ tenantId, displayName }: { tenantId: string; displayName: string }) => {
      await updateTenant(tenantId, { displayName });
    },
    meta: {
      successMessage: "Organization name updated successfully.",
      errorMessage: "Could not update the organization name",
    },
  });

/** No success toast: the page reloads at once to pick up the new membership. */
export const useTransferTenantOwnership = () =>
  useMutation({
    mutationFn: async ({ tenantId, newOwnerId }: { tenantId: string; newOwnerId: string }) => {
      await transferTenantOwnership(tenantId, newOwnerId);
    },
    meta: { errorMessage: "Could not transfer ownership" },
  });

/**
 * Start deleting the organization from its settings page. The confirm closes on failure, so
 * the failure is toasted; the account page's `useDeleteTenant` shows it inline instead.
 */
export const useDeleteOrganization = () =>
  useMutation({
    mutationFn: (tenantId: string) => deleteTenant(tenantId),
    meta: { errorMessage: "Could not delete the organization" },
  });

/** Export the tenant's data and hand the browser a `<slug>-export-<date>.json` file. */
export const useExportTenantData = (tenantSlug: string) =>
  useMutation({
    mutationFn: async (includePlanningData: boolean) => {
      const payload = await exportTenantData({ includeMasterData: true, includePlanningData });
      const json = JSON.stringify(payload, null, 2);
      const timestamp = formatDateForInput(new Date());
      downloadFile(json, `${tenantSlug}-export-${timestamp}.json`, "application/json");
    },
    // The button itself says "Downloaded", so only a failure is toasted.
    meta: { errorMessage: "Could not export the organization data" },
  });
