import type { ResourceTypeKey } from "@foundation/src/types/criterion";

/**
 * Resource type keys. Mirrors the backend `ResourceTypeKeys` constants and the
 * `ResourceTypeKey` union in types/criterion.ts (which remains the type source).
 */
export const RESOURCE_TYPE_KEY = {
  SPACE: "space",
  PERSON: "person",
  TOOL: "tool",
} as const satisfies Record<string, ResourceTypeKey>;
