/** One Server-Sent Event: its name and its `data`, still text. */
export interface SseEvent {
  name: string;
  data: string;
}

/**
 * Takes the complete events off the front of `buffer` and returns what is left, for the
 * caller to prepend to the next chunk. Events end with a blank line. The parser is
 * deliberately minimal: our servers only ever emit `event:`/`data:` pairs, plus comment
 * lines as the keep-alive, which are dropped here.
 */
export function takeSseEvents(buffer: string): { events: SseEvent[]; rest: string } {
  const events: SseEvent[] = [];
  let rest = buffer;

  let separator = rest.indexOf('\n\n');
  while (separator !== -1) {
    const event = parseFrame(rest.slice(0, separator));
    if (event) events.push(event);
    rest = rest.slice(separator + 2);
    separator = rest.indexOf('\n\n');
  }
  return { events, rest };
}

function parseFrame(frame: string): SseEvent | null {
  let name = '';
  let data = '';

  for (const line of frame.split('\n')) {
    if (line.startsWith(':')) continue; // keep-alive
    if (line.startsWith('event:')) name = line.slice(6).trim();
    else if (line.startsWith('data:')) data += line.slice(5).trim();
  }
  return name && data ? { name, data } : null;
}
