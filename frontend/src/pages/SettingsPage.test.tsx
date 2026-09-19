import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Routes, Route, Navigate } from 'react-router';
import { SettingsPage } from './SettingsPage';

function Stub({ id }: { id: string }) {
  return <div data-testid={id} />;
}

function renderAt(initialPath: string) {
  return render(
    <MemoryRouter initialEntries={[initialPath]}>
      <Routes>
        <Route path="/settings" element={<SettingsPage />}>
          <Route index element={<Navigate to="criteria" replace />} />
          <Route path="criteria" element={<Stub id="criteria-settings" />} />
          <Route path="templates" element={<Stub id="template-settings" />} />
          <Route path="routings" element={<Stub id="routing-settings" />} />
          <Route path="presets" element={<Stub id="preset-settings" />} />
          <Route path="scheduling" element={<Stub id="scheduling-settings" />} />
        </Route>
      </Routes>
    </MemoryRouter>,
  );
}

describe('SettingsPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders page title', () => {
    renderAt('/settings/criteria');
    expect(screen.getByRole('heading', { level: 1, name: 'Settings' })).toBeInTheDocument();
  });

  it('renders the five editor-open tabs (no role branching)', () => {
    renderAt('/settings/criteria');
    for (const label of ['Criteria', 'Templates', 'Routings', 'Presets', 'Scheduling']) {
      expect(screen.getByRole('tab', { name: label })).toBeInTheDocument();
    }
  });

  it('does not expose governance tabs (moved to /tenant-admin Administration)', () => {
    renderAt('/settings/criteria');
    for (const label of ['Sites', 'Users', 'Organization', 'Configuration', 'Integrations', 'Usage & Limits']) {
      expect(screen.queryByRole('tab', { name: label })).not.toBeInTheDocument();
    }
  });

  it('index route redirects to /settings/criteria', () => {
    renderAt('/settings');
    expect(screen.getByTestId('criteria-settings')).toBeInTheDocument();
  });

  it.each([
    ['/settings/criteria', 'criteria-settings'],
    ['/settings/templates', 'template-settings'],
    ['/settings/routings', 'routing-settings'],
    ['/settings/presets', 'preset-settings'],
    ['/settings/scheduling', 'scheduling-settings'],
  ])('deep-links %s renders the right child', (path, id) => {
    renderAt(path);
    expect(screen.getByTestId(id)).toBeInTheDocument();
  });

  it('clicking a tab navigates to its sub-route', async () => {
    renderAt('/settings/criteria');
    await userEvent.click(screen.getByRole('tab', { name: 'Scheduling' }));
    expect(screen.getByTestId('scheduling-settings')).toBeInTheDocument();
  });
});
