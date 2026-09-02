import type { PredecessorLogic } from "@foundation/src/types/requests";

/**
 * The join-condition vocabulary, in one place.
 *
 * Two surfaces show it — the dependency list's control and the plan editor's node badge and
 * popover — and they must agree, or the same rule reads as two different rules depending on
 * where the user is standing.
 */
export const PREDECESSOR_LOGIC_OPTIONS: { value: PredecessorLogic; label: string; hint: string }[] = [
  { value: "all", label: "All predecessors", hint: "Starts once every predecessor is done." },
  { value: "any", label: "Any predecessor", hint: "Starts as soon as one predecessor is done." },
  { value: "k_of_n", label: "At least…", hint: "Starts once the chosen number of predecessors are done." },
];

/**
 * The short form for a node badge. `total` is the number of predecessors the request actually
 * has, so a k larger than that reads as "all" — which is exactly how the server clamps it.
 */
export function predecessorLogicBadge(
  logic: PredecessorLogic | undefined,
  k: number | null | undefined,
  total: number,
): string {
  if (total === 0) return "";
  switch (logic) {
    case "any":
      return "ANY";
    case "k_of_n": {
      const required = Math.min(Math.max(k ?? total, 1), total);
      return required >= total ? "ALL" : `${required} OF ${total}`;
    }
    default:
      return "ALL";
  }
}
