/**
 * Resource allocation modes, as stored in `resources.allocation_mode`.
 * Mirrors the backend `AllocationModes` constants. No pre-existing union type
 * existed (the field was typed as `string`), so the type is derived here.
 */
export const ALLOCATION_MODE = {
  EXCLUSIVE: "Exclusive",
  FRACTIONAL: "Fractional",
  CONCURRENT_CAPACITY: "ConcurrentCapacity",
} as const;

export type AllocationMode = typeof ALLOCATION_MODE[keyof typeof ALLOCATION_MODE];
