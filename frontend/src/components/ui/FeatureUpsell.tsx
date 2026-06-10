import type { ReactNode } from "react";
import { Link } from "react-router-dom";
import { Lock } from "lucide-react";
import { Button } from "@foundation/src/components/ui/button";

interface FeatureUpsellProps {
  /** Feature name, e.g. "Reporting API". */
  title: string;
  /** One-line explanation of the feature and which plans include it. */
  description: string;
  /** When set, renders a CTA linking here (e.g. the plans page). Omit to hide the CTA. */
  upgradeHref?: string;
  /** CTA label; defaults to "View plans". */
  upgradeLabel?: string;
  /** Optional teaser content shown between the description and the CTA. */
  children?: ReactNode;
}

/**
 * Locked-feature upsell shown in place of a tier-gated feature. Keeps the user on the
 * feature surface (no silent redirect) and explains the value plus the upgrade path.
 *
 * Presentation only — the actual access decision is enforced server-side (see the
 * IFeatureGate checks on the gated endpoints). This must never become the enforcement point.
 */
export function FeatureUpsell({
  title,
  description,
  upgradeHref,
  upgradeLabel = "View plans",
  children,
}: FeatureUpsellProps) {
  return (
    <div className="rounded-lg border bg-card p-8 text-center">
      <div className="mx-auto flex h-12 w-12 items-center justify-center rounded-full bg-muted">
        <Lock className="h-6 w-6 text-muted-foreground" />
      </div>
      <h2 className="mt-4 text-lg font-semibold">{title}</h2>
      <p className="mx-auto mt-1 max-w-md text-sm text-muted-foreground">{description}</p>
      {children && <div className="mx-auto mt-6 max-w-md text-left">{children}</div>}
      {upgradeHref && (
        <Button asChild className="mt-6">
          <Link to={upgradeHref}>{upgradeLabel}</Link>
        </Button>
      )}
    </div>
  );
}
