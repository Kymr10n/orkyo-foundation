import type { Request, Conflict } from "@/types/requests";
import type { SpaceCapability } from "@/lib/api/space-capability-api";

/**
 * Validates if a space's capabilities meet a request's requirements.
 * Returns an array of conflicts if validation fails.
 *
 * Pure function — no side-effects, no API calls.
 */
export function validateSpaceRequirements(
  request: Request,
  spaceCapabilities: SpaceCapability[]
): Conflict[] {
  const conflicts: Conflict[] = [];
  
  if (!request.requirements || request.requirements.length === 0) {
    return conflicts;
  }

  const capabilityMap = new Map<string, SpaceCapability>();
  spaceCapabilities.forEach(cap => {
    capabilityMap.set(cap.criterionId, cap);
  });

  for (const requirement of request.requirements) {
    const capability = capabilityMap.get(requirement.criterionId);
    
    if (!capability) {
      conflicts.push({
        id: `${request.id}-${requirement.criterionId}-missing`,
        kind: "connector_mismatch",
        severity: "error",
        message: `Space is missing required capability: ${requirement.criterion?.name || 'Unknown'}`,
      });
      continue;
    }

    const dataType = capability.criterion.dataType;
    const reqValue = requirement.value;
    const capValue = capability.value;

    const normType = dataType.charAt(0).toUpperCase() + dataType.slice(1).toLowerCase();

    switch (normType) {
      case "Number":
        if (typeof capValue === "number" && typeof reqValue === "number") {
          if (capValue < reqValue) {
            conflicts.push({
              id: `${request.id}-${requirement.criterionId}-insufficient`,
              kind: "load_exceeded",
              severity: "error",
              message: `${capability.criterion.name}: Space has ${capValue}${capability.criterion.unit ? ` ${capability.criterion.unit}` : ''}, but requires ${reqValue}${capability.criterion.unit ? ` ${capability.criterion.unit}` : ''}`,
            });
          }
        }
        break;

      case "Boolean":
        if (reqValue === true && capValue !== true) {
          conflicts.push({
            id: `${request.id}-${requirement.criterionId}-boolean`,
            kind: "connector_mismatch",
            severity: "error",
            message: `${capability.criterion.name}: Required but not available in space`,
          });
        }
        break;

      case "String":
        if (reqValue !== capValue) {
          conflicts.push({
            id: `${request.id}-${requirement.criterionId}-string`,
            kind: "connector_mismatch",
            severity: "warning",
            message: `${capability.criterion.name}: Space has "${capValue}", but requires "${reqValue}"`,
          });
        }
        break;

      case "Enum":
        if (reqValue !== capValue) {
          conflicts.push({
            id: `${request.id}-${requirement.criterionId}-enum`,
            kind: "size_mismatch",
            severity: "error",
            message: `${capability.criterion.name}: Space has "${capValue}", but requires "${reqValue}"`,
          });
        }
        break;

      default:
        if (reqValue !== capValue) {
          conflicts.push({
            id: `${request.id}-${requirement.criterionId}-unknown`,
            kind: "connector_mismatch",
            severity: "warning",
            message: `${capability.criterion.name}: May not meet requirements`,
          });
        }
    }
  }

  return conflicts;
}
