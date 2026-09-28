import { useMutation } from "@tanstack/react-query";
import { transferTenantOwnership, updateTenant } from "@foundation/src/lib/api/tenant-management-api";
import { exportTenantData } from "@foundation/src/lib/api/export-api";
import { downloadFile } from "@foundation/src/lib/utils/import-export";
import { formatDateForInput } from "@foundation/src/lib/utils";

// The organization settings card's writes. No `meta`: the card reports success and failure in
// its own alerts, and the membership it reads comes from the auth context, not a query.

export const useRenameTenant = () =>
  useMutation({
    mutationFn: async ({ tenantId, displayName }: { tenantId: string; displayName: string }) => {
      await updateTenant(tenantId, { displayName });
    },
  });

export const useTransferTenantOwnership = () =>
  useMutation({
    mutationFn: async ({ tenantId, newOwnerId }: { tenantId: string; newOwnerId: string }) => {
      await transferTenantOwnership(tenantId, newOwnerId);
    },
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
  });
