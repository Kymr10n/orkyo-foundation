import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Routes, Route, Navigate, useLocation } from 'react-router-dom';
import { PeoplePage } from './PeoplePage';

// Tab-content children are mocked — this test only covers the routing shell.
function Stub({ id }: { id: string }) {
  return <div data-testid={id} />;
}
function LocationProbe() {
  const loc = useLocation();
  return <div data-testid="path">{loc.pathname}</div>;
}

function renderAt(initialPath: string) {
  return render(
    <MemoryRouter initialEntries={[initialPath]}>
      <Routes>
        <Route path="/people" element={<PeoplePage />}>
          <Route index element={<Navigate to="list" replace />} />
          <Route path="list" element={<Stub id="list" />} />
          <Route path="groups" element={<Stub id="groups" />} />
          <Route path="absences" element={<Stub id="absences" />} />
          <Route path="departments" element={<Stub id="departments" />} />
          <Route path="job-titles" element={<Stub id="job-titles" />} />
          <Route path="skills" element={<Stub id="skills" />} />
        </Route>
        <Route path="*" element={<LocationProbe />} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('PeoplePage', () => {
  beforeEach(() => vi.clearAllMocks());

  it('renders page title and tab triggers', () => {
    renderAt('/people/list');
    expect(screen.getByText('People', { selector: 'h1' })).toBeInTheDocument();
    for (const label of ['People', 'Groups', 'Absences', 'Job Titles', 'Departments', 'Skills']) {
      expect(screen.getByRole('tab', { name: label })).toBeInTheDocument();
    }
  });

  it.each([
    ['/people/list', 'list'],
    ['/people/groups', 'groups'],
    ['/people/absences', 'absences'],
    ['/people/job-titles', 'job-titles'],
    ['/people/departments', 'departments'],
    ['/people/skills', 'skills'],
  ])('deep-links %s renders the right child', (path, id) => {
    renderAt(path);
    expect(screen.getByTestId(id)).toBeInTheDocument();
  });

  it('clicking a tab navigates to the sub-route', async () => {
    renderAt('/people/list');
    await userEvent.click(screen.getByRole('tab', { name: 'Departments' }));
    expect(screen.getByTestId('departments')).toBeInTheDocument();
  });

  it.each([
    ['people', '/people/list'],
    ['jobTitles', '/people/job-titles'],
    ['departments', '/people/departments'],
    ['groups', '/people/groups'],
  ])('legacy ?tab=%s redirects to %s', (legacy, target) => {
    renderAt(`/people?tab=${legacy}`);
    // After redirect, the new path renders the matching child.
    const id = target.split('/').pop()!;
    expect(screen.getByTestId(id)).toBeInTheDocument();
  });
});
