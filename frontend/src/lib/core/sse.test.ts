import { describe, expect, it } from 'vitest';
import { takeSseEvents } from './sse';

describe('takeSseEvents', () => {
  it('returns the complete events and nothing to carry over', () => {
    expect(takeSseEvents('event: progress\ndata: {"step":"erase"}\n\nevent: result\ndata: {"rows":1}\n\n')).toEqual({
      events: [
        { name: 'progress', data: '{"step":"erase"}' },
        { name: 'result', data: '{"rows":1}' },
      ],
      rest: '',
    });
  });

  it('keeps an event that is not complete yet for the next chunk', () => {
    const first = takeSseEvents('event: progress\ndata: {"step":"lo');
    expect(first).toEqual({ events: [], rest: 'event: progress\ndata: {"step":"lo' });

    expect(takeSseEvents(`${first.rest}ad","percent":40}\n\n`).events).toEqual([
      { name: 'progress', data: '{"step":"load","percent":40}' },
    ]);
  });

  it('drops keep-alive comments and frames without a name or data', () => {
    expect(takeSseEvents(': keep-alive\n\nevent: lonely\n\nevent: done\ndata: {}\n\n').events).toEqual([
      { name: 'done', data: '{}' },
    ]);
  });
});
