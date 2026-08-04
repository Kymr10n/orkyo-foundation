import { describe, expect, it, vi, beforeEach, type Mock } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router';
import { AuditLogTab } from './AuditLogTab';
import { getTenantAuditEvents } from '@foundation/src/lib/api/audit-api';

vi.mock('@foundation/src/lib/api/audit-api', () => ({
  getTenantAuditEvents: vi.fn(),
}));

vi.mock('@foundation/src/hooks/useAuditLogAvailable', () => ({
  useAuditLogAvailable: () => true,
}));

const event = {
  id: 'e1',
  createdAt: '2026-07-01T10:00:00Z',
  actorUserId: 'u1',
  actorEmail: 'alex@example.com',
  actorDisplayName: 'Alex',
  actorType: 'user',
  action: 'user.login',
  targetType: null,
  targetId: null,
  metadata: null,
};

const renderTab = () =>
  render(
    <MemoryRouter>
      <AuditLogTab />
    </MemoryRouter>,
  );

beforeEach(() => {
  vi.clearAllMocks();
  (getTenantAuditEvents as Mock).mockResolvedValue({
    events: [event],
    totalCount: 60,
    totalPages: 3,
  });
});

describe('AuditLogTab', () => {
  it('loads the first page unfiltered', async () => {
    renderTab();
    await screen.findByText('user.login');

    expect(getTenantAuditEvents).toHaveBeenCalledWith({
      page: 1,
      pageSize: 25,
      action: undefined,
      from: undefined,
      to: undefined,
    });
  });

  it('translates the date-range header filter into from/to API params and resets the page', async () => {
    renderTab();
    await screen.findByText('user.login');

    // Advance a page first, so the reset is observable.
    await userEvent.click(screen.getByRole('button', { name: 'Next page' }));
    await waitFor(() =>
      expect(getTenantAuditEvents).toHaveBeenLastCalledWith(expect.objectContaining({ page: 2 })),
    );

    // This table is server-paged: the header filter must query, not hide visible rows.
    await userEvent.click(screen.getByRole('button', { name: 'When — sort and filter' }));
    await userEvent.type(screen.getByLabelText('Filter When from'), '2026-06-01');

    await waitFor(() =>
      expect(getTenantAuditEvents).toHaveBeenLastCalledWith(
        expect.objectContaining({ from: '2026-06-01', page: 1 }),
      ),
    );
  });

  it('translates the action header filter into the action API param', async () => {
    renderTab();
    await screen.findByText('user.login');

    await userEvent.click(screen.getByRole('button', { name: 'Action — sort and filter' }));
    await userEvent.type(screen.getByLabelText('Filter Action'), 'login');

    // Text filters debounce their URL write (300ms) before the query keys change.
    await waitFor(
      () =>
        expect(getTenantAuditEvents).toHaveBeenLastCalledWith(
          expect.objectContaining({ action: 'login', page: 1 }),
        ),
      { timeout: 2000 },
    );
  });
});
