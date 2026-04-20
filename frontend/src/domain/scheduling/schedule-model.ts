/**
 * Core domain model for the scheduling validation architecture.
 *
 * Key invariants:
 * - All timestamps are epoch milliseconds (number)
 * - `end` is exclusive (half-open interval [start, end))
 * - Duration is always derived from start/end — never stored separately
 * - DraftInteraction describes a single in-flight user gesture
 */

import type { Conflict, DurationUnit, Request } from "@/types/requests";
import { DURATION_UNIT_MS, MS_PER_MINUTE } from "../constants";

// ---------------------------------------------------------------------------
// Committed entry — one scheduled request from the server / query cache
// ---------------------------------------------------------------------------

interface ScheduledEntry {
  requestId: string;
  spaceId: string;
  startMs: number; // epoch ms, inclusive
  endMs: number;   // epoch ms, exclusive
  name: string;
  minimalDurationMs: number;
}

// ---------------------------------------------------------------------------
// Draft interaction — the in-flight resize gesture (null when idle)
// ---------------------------------------------------------------------------

export type ResizeEdge = "left" | "right";

/**
 * Phase of a resize draft:
 *   - "active"     → user is dragging; bounds update on every pointer move
 *   - "committing" → pointer released; bounds frozen, waiting for mutation to settle
 */
export type ResizePhase = "active" | "committing";

export interface DraftResize {
  kind: "resize";
  phase: ResizePhase;
  requestId: string;
  spaceId: string;
  edge: ResizeEdge;
  /** Original committed bounds (kept for rollback / delta math) */
  committedStartMs: number;
  committedEndMs: number;
  /** Current preview bounds (mutated on every pointer move) */
  previewStartMs: number;
  previewEndMs: number;
}

export type DraftInteraction = DraftResize | null;

// ---------------------------------------------------------------------------
// Preview entry — what should be rendered for a single request
// ---------------------------------------------------------------------------

export interface PreviewEntry {
  requestId: string;
  spaceId: string;
  startMs: number;
  endMs: number;
  name: string;
  minimalDurationMs: number;
  /** True when this entry's bounds differ from committed state */
  isDraft: boolean;
}

// ---------------------------------------------------------------------------
// Preview schedule — the full set of entries used for rendering
// ---------------------------------------------------------------------------

export type PreviewSchedule = Map<string, PreviewEntry>;

// ---------------------------------------------------------------------------
// Validation
// ---------------------------------------------------------------------------

/** requestId → list of conflicts for that request in the current preview */
export type ValidationResult = Map<string, Conflict[]>;

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

export function durationToMs(value: number, unit: DurationUnit): number {
  return value * (DURATION_UNIT_MS[unit] ?? MS_PER_MINUTE);
}

/** Convert a domain Request into a ScheduledEntry, or null if not scheduled. */
export function toScheduledEntry(request: Request): ScheduledEntry | null {
  if (!request.spaceId || !request.startTs || !request.endTs) return null;
  const startMs = new Date(request.startTs).getTime();
  const endMs = new Date(request.endTs).getTime();
  if (isNaN(startMs) || isNaN(endMs) || startMs >= endMs) return null;
  return {
    requestId: request.id,
    spaceId: request.spaceId,
    startMs,
    endMs,
    name: request.name,
    minimalDurationMs: durationToMs(request.minimalDurationValue, request.minimalDurationUnit),
  };
}
