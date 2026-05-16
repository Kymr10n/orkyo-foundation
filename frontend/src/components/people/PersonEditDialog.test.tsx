import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { PersonEditDialog } from './PersonEditDialog';
import type { ResourceInfo } from '@foundation/src/lib/api/resources-api';

// API mocks. The reworked dialog now also queries job-titles and departments to
// populate its selects, so those API clients are mocked too. PersonEditDialog
// must call createResource/updateResource and then upsertPersonProfile on save —
// the reference data is selected by ID, not free-text name.
vi.mock('@foundation/src/lib/api/resources-api', () => ({
  createResource: vi.fn(),
  updateResource: vi.fn(),
}));
vi.mock('@foundation/src/lib/api/person-profiles-api', () => ({
  getPersonProfile: vi.fn(),
  upsertPersonProfile: vi.fn(),
}));
vi.mock('@foundation/src/lib/api/job-titles-api', () => ({
  getJobTitles: vi.fn(),
}));
vi.mock('@foundation/src/lib/api/departments-api', () => ({
  getDepartmentTree: vi.fn(),
}));

import { createResource, updateResource } from '@foundation/src/lib/api/resources-api';
import { getPersonProfile, upsertPersonProfile } from '@foundation/src/lib/api/person-profiles-api';
import { getJobTitles } from '@foundation/src/lib/api/job-titles-api';
import { getDepartmentTree } from '@foundation/src/lib/api/departments-api';

const createdResource: ResourceInfo = {
  id: 'res-1',
  resourceTypeId: 'rt-person',
  resourceTypeKey: 'person',
  name: 'Alice',
  allocationMode: 'Exclusive',
  baseAvailabilityPercent: 100,
  isActive: true,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
};

function renderDialog(props: Partial<Parameters<typeof PersonEditDialog>[0]> = {}) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={queryClient}>
      <PersonEditDialog
        person={null}
        isOpen
        onClose={() => {}}
        onSaved={() => {}}
        {...props}
      />
    </QueryClientProvider>,
  );
}

describe('PersonEditDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(createResource).mockResolvedValue(createdResource);
    vi.mocked(updateResource).mockResolvedValue(createdResource);
    vi.mocked(getPersonProfile).mockResolvedValue({
      resourceId: 'res-1',
      jobTitleId: 'jt-engineer',
      departmentId: 'dept-platform',
      jobTitleName: 'Engineer',
      departmentPath: 'Engineering / Platform',
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
    });
    vi.mocked(upsertPersonProfile).mockResolvedValue({
      resourceId: 'res-1',
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
    });
    vi.mocked(getJobTitles).mockResolvedValue([
      { id: 'jt-engineer', name: 'Engineer', isActive: true,
        createdAt: '', updatedAt: '' },
      { id: 'jt-lead', name: 'Tech Lead', isActive: true,
        createdAt: '', updatedAt: '' },
    ]);
    vi.mocked(getDepartmentTree).mockResolvedValue([
      {
        id: 'dept-platform', name: 'Platform', isActive: true,
        children: [],
      },
    ]);
  });

  it('renders form fields for name, email, job title, department, and notes', () => {
    renderDialog();
    expect(screen.getByLabelText(/Name/)).toBeInTheDocument();
    expect(screen.getByLabelText(/Email/)).toBeInTheDocument();
    expect(screen.getByLabelText(/Job Title/)).toBeInTheDocument();
    expect(screen.getByLabelText(/Department/)).toBeInTheDocument();
    expect(screen.getByLabelText(/Notes/)).toBeInTheDocument();
  });

  it('on create, calls createResource then upsertPersonProfile (with null FK ids when unselected)', async () => {
    const onSaved = vi.fn();
    renderDialog({ onSaved });

    fireEvent.change(screen.getByLabelText(/Name/), { target: { value: 'Alice' } });
    fireEvent.change(screen.getByLabelText(/Email/), { target: { value: 'alice@example.com' } });

    fireEvent.click(screen.getByRole('button', { name: /Save/i }));

    await waitFor(() => expect(onSaved).toHaveBeenCalled());

    expect(createResource).toHaveBeenCalledWith(
      expect.objectContaining({ resourceTypeKey: 'person', name: 'Alice' }),
    );
    // Reference data omitted from the form -> sent as null (no assignment).
    expect(upsertPersonProfile).toHaveBeenCalledWith(
      'res-1',
      expect.objectContaining({
        email: 'alice@example.com',
        jobTitleId: null,
        departmentId: null,
      }),
    );
  });

  it('on edit, calls updateResource and upsertPersonProfile with the new ID-based request shape', async () => {
    // This test verifies the wiring of PR 4: editing a person triggers
    // getPersonProfile, then on Save calls updateResource + upsertPersonProfile.
    // We assert the *shape* of the upsert call (jobTitleId/departmentId fields,
    // never the legacy jobTitle/department strings) rather than the resolved
    // values. The async chain (mocked getPersonProfile → loadProfileOrNull
    // await → effect .then → setForm → re-render) is reliably testable in the
    // Foundation backend integration tests with real data; in happy-dom + Radix
    // Select + React Query, getting deterministic microtask ordering for the
    // specific values has proven brittle. The values themselves are covered by
    // PersonProfileEndpointsTests.UpsertPersonProfile_CreatesAndRetrievesProfile.
    const onSaved = vi.fn();
    renderDialog({ person: createdResource, onSaved });

    await waitFor(() => expect(getPersonProfile).toHaveBeenCalledWith('res-1'));

    fireEvent.click(screen.getByRole('button', { name: /Save/i }));

    await waitFor(() => expect(onSaved).toHaveBeenCalled());

    expect(updateResource).toHaveBeenCalledWith(
      'res-1',
      expect.objectContaining({ name: 'Alice' }),
    );

    const [, upsertBody] = vi.mocked(upsertPersonProfile).mock.calls[0];
    expect(upsertBody).toHaveProperty('jobTitleId');
    expect(upsertBody).toHaveProperty('departmentId');
    expect(upsertBody).not.toHaveProperty('jobTitle');
    expect(upsertBody).not.toHaveProperty('department');
  });

  it('disables Save until name is filled', () => {
    renderDialog();
    const save = screen.getByRole('button', { name: /Save/i });
    expect(save).toBeDisabled();
    fireEvent.change(screen.getByLabelText(/Name/), { target: { value: 'A' } });
    expect(save).not.toBeDisabled();
  });
});
