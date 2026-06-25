import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Routes, Route, Navigate } from 'react-router-dom';
import { InsightsPage } from './InsightsPage';

vi.mock('@foundation/src/store/app-store', () => ({
  useAppStore: (selector: (s: { selectedSiteId: string | null }) => unknown) => selector({ selectedSiteId: null }),
}));

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/insights" element={<InsightsPage />}>
          <Route index element={<Navigate to="overview" replace />} />
          <Route path="overview" element={<div>OVERVIEW CONTENT</div>} />
          <Route path="utilization" element={<div>UTIL CONTENT</div>} />
          <Route path="conflicts" element={<div>CONFLICTS CONTENT</div>} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

describe('InsightsPage', () => {
  it('renders the three tab triggers', () => {
    renderAt('/insights/overview');
    expect(screen.getByRole('tab', { name: 'Overview' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Utilization' })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: 'Conflicts' })).toBeInTheDocument();
  });

  it('defaults to the Overview tab', () => {
    renderAt('/insights');
    expect(screen.getByText('OVERVIEW CONTENT')).toBeInTheDocument();
  });

  it('navigates to a tab when its trigger is clicked', async () => {
    const user = userEvent.setup();
    renderAt('/insights/overview');
    await user.click(screen.getByRole('tab', { name: 'Conflicts' }));
    expect(await screen.findByText('CONFLICTS CONTENT')).toBeInTheDocument();
  });
});
