import { Popover, PopoverContent, PopoverTrigger } from "@foundation/src/components/ui/popover";
import { ConflictItem, type ConflictWithRequest } from "@foundation/src/components/insights/ConflictItem";
import type { Request } from "@foundation/src/types/requests";

/**
 * What a red bar on the schedule grid means, shown where the user clicked it.
 *
 * The grid used to say "this is in conflict" through colour and a native `title`
 * carrying only a count — it raised the question and would not answer it. The cards
 * are the same ones the Conflicts tab renders, so a conflict reads identically in
 * both places.
 *
 * Anchored to a zero-size span at the pointer, the same way the grid's right-click
 * menu anchors. Keyboard users reach the same detail through the request editor's
 * conflict banner, because a keypress carries no anchor point.
 */
export function ConflictDetailsPopover({
  request,
  conflicts,
  position,
  onOpenRequest,
  onClose,
  peerRequestFor,
}: {
  request: Request;
  conflicts: ConflictWithRequest[];
  position: { x: number; y: number };
  onOpenRequest: (request: Request) => void;
  onClose: () => void;
  peerRequestFor?: (conflict: ConflictWithRequest) => Request | undefined;
}) {
  return (
    <Popover open onOpenChange={(open) => !open && onClose()}>
      <PopoverTrigger asChild>
        <span
          aria-hidden
          style={{ position: "fixed", left: position.x, top: position.y, width: 0, height: 0 }}
        />
      </PopoverTrigger>
      <PopoverContent
        align="start"
        className="w-[26rem] max-w-[90vw] p-0"
        aria-label={`Conflicts on ${request.name}`}
      >
        <div className="max-h-[60vh] space-y-2 overflow-auto p-2">
          {conflicts.map((conflict) => (
            <ConflictItem
              key={conflict.id}
              item={conflict}
              onOpen={(target) => {
                onClose();
                onOpenRequest(target);
              }}
              peerRequest={peerRequestFor?.(conflict)}
            />
          ))}
        </div>
      </PopoverContent>
    </Popover>
  );
}
