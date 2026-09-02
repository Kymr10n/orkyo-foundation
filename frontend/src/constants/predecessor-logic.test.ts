import { describe, it, expect } from 'vitest';
import { predecessorLogicBadge, PREDECESSOR_LOGIC_OPTIONS } from './predecessor-logic';

describe('predecessorLogicBadge', () => {
  it('says nothing when a request has no predecessors', () => {
    // A condition over an empty set is not a fact worth putting on a node.
    expect(predecessorLogicBadge('any', null, 0)).toBe('');
  });

  it('labels the default and the disjunction', () => {
    expect(predecessorLogicBadge('all', null, 3)).toBe('ALL');
    expect(predecessorLogicBadge('any', null, 3)).toBe('ANY');
  });

  it('treats an absent logic as all, matching the column default', () => {
    expect(predecessorLogicBadge(undefined, null, 2)).toBe('ALL');
  });

  it('names the count for a partial requirement', () => {
    expect(predecessorLogicBadge('k_of_n', 2, 3)).toBe('2 OF 3');
  });

  it('reads a k at or above the count as all, exactly as the server clamps it', () => {
    // Edges come and go independently of the stored k, so k can outlive the set it described.
    expect(predecessorLogicBadge('k_of_n', 3, 3)).toBe('ALL');
    expect(predecessorLogicBadge('k_of_n', 9, 3)).toBe('ALL');
  });

  it('floors a nonsensical k at one rather than showing "0 OF 3"', () => {
    expect(predecessorLogicBadge('k_of_n', 0, 3)).toBe('1 OF 3');
  });

  it('offers exactly the three logics the server accepts', () => {
    expect(PREDECESSOR_LOGIC_OPTIONS.map((o) => o.value)).toEqual(['all', 'any', 'k_of_n']);
  });
});
