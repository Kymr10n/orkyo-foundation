import { useEffect, useState } from "react";
import {
  canCreateTenant,
  getStarterTemplates,
  getTenantMemberships,
  type TenantMembership,
} from "@foundation/src/lib/api/tenant-account-api";
import type { StarterTemplate } from "@foundation/src/components/onboarding/StarterTemplatePicker";
import { logger } from "@foundation/src/lib/core/logger";

/** What the onboarding wizard needs before it can offer anything. */
export interface OnboardingData {
  /** Null until the check answers. */
  canCreate: boolean | null;
  /**
   * Why creation is refused, when the backend cares to say. Invitation-only access needs to point
   * somewhere; the generic "contact an administrator" is the fallback for a refusal with no stated
   * reason (e.g. already owning a workspace).
   */
  cannotCreateReason: string | null;
  /** True until the create check settles. The template and membership reads never gate the page. */
  loading: boolean;
  templates: StarterTemplate[];
  templatesError: boolean;
  /** The person's own workspaces that are scheduled for deletion, and can still be restored. */
  deletingTenants: TenantMembership[];
}

/**
 * The three reads the onboarding page opens with, loaded once on mount.
 *
 * Deliberately not react-query: this surface renders above the app shell, outside the
 * QueryClientProvider, and each answer is a one-shot fact about this session. Manual load by
 * design on this operator surface — see docs/dialog-feedback.md.
 */
export function useOnboardingData(): OnboardingData {
  const [canCreate, setCanCreate] = useState<boolean | null>(null);
  const [cannotCreateReason, setCannotCreateReason] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [templates, setTemplates] = useState<StarterTemplate[]>([]);
  const [templatesError, setTemplatesError] = useState(false);
  const [deletingTenants, setDeletingTenants] = useState<TenantMembership[]>([]);

  useEffect(() => {
    const checkCanCreate = async () => {
      try {
        const data = await canCreateTenant();
        setCanCreate(data.canCreate);
        setCannotCreateReason(data.reason ?? null);
      } catch (err) {
        logger.error("Failed to check can create:", err);
        // 401 is handled globally by the API error handler (redirects to apex login)
        setCanCreate(false);
      } finally {
        setLoading(false);
      }
    };

    const loadTemplates = async () => {
      try {
        const data = await getStarterTemplates();
        setTemplates(data);
      } catch (err) {
        logger.error("Failed to load starter templates:", err);
        setTemplatesError(true);
      }
    };

    const loadDeletingTenants = async () => {
      try {
        const memberships = await getTenantMemberships();
        setDeletingTenants(memberships.filter((m) => m.tenantStatus === "deleting" && m.isOwner));
      } catch {
        // Non-critical — just don't show the section
      }
    };

    checkCanCreate();
    loadTemplates();
    loadDeletingTenants();
  }, []);

  return { canCreate, cannotCreateReason, loading, templates, templatesError, deletingTenants };
}
