import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@foundation/src/components/ui/select";
import { TENANT_ROLE } from "@foundation/src/hooks/usePermissions";

/** Every tenant role a picker can offer — "none" means "no membership" and is never assignable. */
export type SelectableRole = Exclude<
  (typeof TENANT_ROLE)[keyof typeof TENANT_ROLE],
  typeof TENANT_ROLE.None
>;

/**
 * The catalog of role labels and their one-line meaning, so a rename flows to every picker
 * instead of living as copy-pasted literals at each call site. Both role pickers in foundation
 * read it; nothing outside this module does yet.
 */
export const ROLE_LABELS: Record<SelectableRole, string> = {
  [TENANT_ROLE.Admin]: "Admin",
  [TENANT_ROLE.Editor]: "Editor",
  [TENANT_ROLE.Viewer]: "Viewer",
  [TENANT_ROLE.Inactive]: "Inactive",
};

export const ROLE_DESCRIPTIONS: Record<SelectableRole, string> = {
  [TENANT_ROLE.Admin]: "Full access including settings and user management",
  [TENANT_ROLE.Editor]: "Can create and modify utilization and requests",
  [TENANT_ROLE.Viewer]: "Can view data but cannot make changes",
  [TENANT_ROLE.Inactive]: "No access - user account disabled",
};

/**
 * The sentence-long meaning shown in the panel beneath the picker, as distinct from the short
 * phrase `ROLE_DESCRIPTIONS` puts inside each dropdown option.
 *
 * Both are needed and they are deliberately different lengths. Keeping them here stops the two
 * user dialogs from carrying identical copies, which is what they did: the same three sentences,
 * duplicated, and a fourth for inactive in only one of them.
 */
export const ROLE_SUMMARIES: Record<SelectableRole, string> = {
  [TENANT_ROLE.Admin]:
    "Admins have full access to all features including user management and settings.",
  [TENANT_ROLE.Editor]:
    "Editors can create and modify utilization, requests, and spaces but cannot access settings.",
  [TENANT_ROLE.Viewer]: "Viewers have read-only access to view utilization and plans.",
  [TENANT_ROLE.Inactive]: "Inactive users cannot log in and have no access to the system.",
};

/** Admin surfaces assign the three active roles; "inactive" is opted into per call site. */
const DEFAULT_ROLES: readonly SelectableRole[] = [
  TENANT_ROLE.Admin,
  TENANT_ROLE.Editor,
  TENANT_ROLE.Viewer,
];

/**
 * Membership-role picker. Shared by the two settings dialogs that assign a role; the hosted
 * product's admin surfaces adopt it on the next package bump.
 * `roles` fixes both the offered set and its order; `withDescriptions` renders the
 * one-line meaning under each label, which the settings dialogs show and the tables do not.
 */
export function RoleSelect<T extends SelectableRole = SelectableRole>({
  value,
  onValueChange,
  roles,
  withDescriptions = false,
  disabled,
  id,
  className = "w-28",
}: {
  value: T;
  onValueChange: (role: T) => void;
  /** Roles to offer, in render order. Defaults to Admin/Editor/Viewer. */
  roles?: readonly T[];
  /** Render ROLE_DESCRIPTIONS under each label. */
  withDescriptions?: boolean;
  disabled?: boolean;
  id?: string;
  /** Trigger width; the dialogs use the full column, the tables a fixed 28. */
  className?: string;
}) {
  const offered: readonly SelectableRole[] = roles ?? DEFAULT_ROLES;
  return (
    <Select
      value={value}
      onValueChange={(v) => onValueChange(v as T)}
      disabled={disabled}
    >
      <SelectTrigger id={id} className={className}>
        <SelectValue />
      </SelectTrigger>
      <SelectContent>
        {offered.map((role) => (
          <SelectItem key={role} value={role}>
            {withDescriptions ? (
              <div>
                <div className="font-medium">{ROLE_LABELS[role]}</div>
                <div className="text-xs text-muted-foreground">
                  {ROLE_DESCRIPTIONS[role]}
                </div>
              </div>
            ) : (
              ROLE_LABELS[role]
            )}
          </SelectItem>
        ))}
      </SelectContent>
    </Select>
  );
}
