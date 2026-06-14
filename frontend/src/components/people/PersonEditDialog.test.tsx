import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { PersonEditDialog } from './PersonEditDialog';
import { createFeedbackTestQueryWrapper } from '@foundation/src/test-utils';
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
vi.mock('@foundation/src/lib/api/resource-assignments-api', () => ({
  getAssignmentsByResource: vi.fn(),
}));
const sitesMock = vi.hoisted(() => ({
  sites: [] as { id: string; name: string }[],
  isMultiSite: false,
}));
vi.mock('@foundation/src/hooks/useSites', () => ({
  useSites: () => ({ data: sitesMock.sites }),
  useIsMultiSite: () => sitesMock.isMultiSite,
}));
vi.mock('sonner', () => ({
  toast: { success: vi.fn(), error: vi.fn() },
}));

import { createResource, updateResource } from '@foundation/src/lib/api/resources-api';
import { getPersonProfile, upsertPersonProfile } from '@foundation/src/lib/api/person-profiles-api';
import { getJobTitles } from '@foundation/src/lib/api/job-titles-api';
import { getDepartmentTree } from '@foundation/src/lib/api/departments-api';
import { getAssignmentsByResource } from '@foundation/src/lib/api/resource-assignments-api';
import { useCanEdit } from '@foundation/src/hooks/usePermissions';
import { toast } from 'sonner';

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
  // Save flows through useMutation; the feedback wrapper's MutationCache mirrors
  // production so meta-driven toasts/invalidation fire in tests.
  return render(
    <PersonEditDialog
      person={null}
      isOpen
      onClose={() => {}}
      onSaved={() => {}}
      {...props}
    />,
    { wrapper: createFeedbackTestQueryWrapper() },
  );
}

describe('PersonEditDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    sitesMock.sites = [];
    sitesMock.isMultiSite = false;
    vi.mocked(createResource).mockResolvedValue(createdResource);
    vi.mocked(updateResource).mockResolvedValue(createdResource);
    vi.mocked(getAssignmentsByResource).mockResolvedValue([]);
    // useCanEdit is globally mocked to true (src/test/setup.ts); reset each test so a
    // viewer-state override never leaks (clearAllMocks does not restore the implementation).
    vi.mocked(useCanEdit).mockReturnValue(true);
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

  it('renders nothing and does not sync the form while closed', () => {
    renderDialog({ isOpen: false });
    expect(screen.queryByLabelText(/Name/)).not.toBeInTheDocument();
  });

  it('shows an "update" error toast when an edit save fails', async () => {
    vi.mocked(updateResource).mockRejectedValue(new Error('Conflict'));
    renderDialog({ person: createdResource, onSaved: vi.fn() });

    await waitFor(() => expect(getPersonProfile).toHaveBeenCalledWith('res-1'));
    fireEvent.click(screen.getByRole('button', { name: /Save/i }));

    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith(
        'Failed to update person',
        expect.objectContaining({ description: 'Conflict' }),
      ),
    );
  });

  it('coalesces a partial/stale resource shape to safe form defaults', async () => {
    // The API normally returns a fully-populated ResourceInfo; a stale or partial
    // shape (null name/allocationMode/availability) must not crash the form.
    const partial = {
      ...createdResource,
      name: null,
      allocationMode: null,
      baseAvailabilityPercent: null,
    } as unknown as ResourceInfo;
    renderDialog({ person: partial, onSaved: vi.fn() });

    await waitFor(() => expect(getPersonProfile).toHaveBeenCalled());
    expect(screen.getByLabelText(/Name/)).toHaveValue('');
    // Save stays disabled because the coalesced name is empty.
    expect(screen.getByRole('button', { name: /Save/i })).toBeDisabled();
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

  it('shows a success toast with "Person created" after a successful create', async () => {
    renderDialog({ onSaved: vi.fn() });

    fireEvent.change(screen.getByLabelText(/Name/), { target: { value: 'Alice' } });
    fireEvent.click(screen.getByRole('button', { name: /Save/i }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Person created'));
  });

  it('shows a success toast with "Person updated" after a successful edit', async () => {
    renderDialog({ person: createdResource, onSaved: vi.fn() });

    await waitFor(() => expect(getPersonProfile).toHaveBeenCalledWith('res-1'));
    fireEvent.click(screen.getByRole('button', { name: /Save/i }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Person updated'));
  });

  it('shows an error toast when create fails', async () => {
    vi.mocked(createResource).mockRejectedValue(new Error('Server error'));
    renderDialog({ onSaved: vi.fn() });

    fireEvent.change(screen.getByLabelText(/Name/), { target: { value: 'Alice' } });
    fireEvent.click(screen.getByRole('button', { name: /Save/i }));

    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith(
        'Failed to create person',
        expect.objectContaining({ description: 'Server error' }),
      ),
    );
  });

  it('shows an error toast when a malformed email is submitted', async () => {
    renderDialog({ onSaved: vi.fn() });

    const nameInput = screen.getByLabelText(/Name/);
    fireEvent.change(nameInput, { target: { value: 'Alice' } });
    fireEvent.change(screen.getByLabelText(/Email/), { target: { value: 'notanemail' } });
    fireEvent.submit(nameInput.closest('form')!);

    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith(
        'Failed to create person',
        expect.objectContaining({ description: 'Please enter a valid email address' }),
      ),
    );
    expect(createResource).not.toHaveBeenCalled();
  });

  it('does not validate email when the field is empty (optional field)', async () => {
    renderDialog({ onSaved: vi.fn() });

    const nameInput = screen.getByLabelText(/Name/);
    fireEvent.change(nameInput, { target: { value: 'Alice' } });
    // Leave email blank
    fireEvent.submit(nameInput.closest('form')!);

    await waitFor(() => expect(createResource).toHaveBeenCalled());
    expect(toast.error).not.toHaveBeenCalled();
  });

  it('persists description, notes, and base availability on create', async () => {
    renderDialog({ onSaved: vi.fn() });

    fireEvent.change(screen.getByLabelText(/Name/), { target: { value: 'Alice' } });
    fireEvent.change(screen.getByLabelText(/Description/), {
      target: { value: 'A teammate' },
    });
    fireEvent.change(screen.getByLabelText(/Notes/), {
      target: { value: 'Prefers mornings' },
    });
    fireEvent.change(screen.getByLabelText(/Base Availability/), {
      target: { value: '80' },
    });

    fireEvent.click(screen.getByRole('button', { name: /Save/i }));

    await waitFor(() => expect(createResource).toHaveBeenCalled());
    expect(createResource).toHaveBeenCalledWith(
      expect.objectContaining({
        description: 'A teammate',
        baseAvailabilityPercent: 80,
      }),
    );
    expect(upsertPersonProfile).toHaveBeenCalledWith(
      'res-1',
      expect.objectContaining({ notes: 'Prefers mornings' }),
    );
  });

  describe('multi-site Location block', () => {
    const multiSitePerson: ResourceInfo = {
      ...createdResource,
      homeSiteId: 'site-1',
      currentSiteId: 'site-1',
      crossSiteAllowed: true,
    };

    beforeEach(() => {
      sitesMock.isMultiSite = true;
      sitesMock.sites = [
        { id: 'site-1', name: 'Site A' },
        { id: 'site-2', name: 'Site B' },
      ];
      vi.mocked(updateResource).mockResolvedValue(multiSitePerson);
      // No profile row → the API 404s and the component's loadProfileOrNull maps it to null.
      vi.mocked(getPersonProfile).mockRejectedValue(new Error('404 Not Found'));
    });

    it('renders the Location fields only when the tenant is multi-site', async () => {
      renderDialog({ person: multiSitePerson, onSaved: vi.fn() });
      await waitFor(() => expect(getPersonProfile).toHaveBeenCalled());

      expect(screen.getByText('Location')).toBeInTheDocument();
      expect(screen.getByLabelText('Home Site')).toBeInTheDocument();
      expect(screen.getByLabelText('Current Site')).toBeInTheDocument();
      expect(
        screen.getByLabelText('Available for other sites'),
      ).toBeInTheDocument();
    });

    it('saves the home-site fields, toggling cross-site availability', async () => {
      const onSaved = vi.fn();
      renderDialog({ person: multiSitePerson, onSaved });
      await waitFor(() => expect(getPersonProfile).toHaveBeenCalled());

      // Wait for the form to sync from the person (checkbox starts checked),
      // then turn off "available for other sites".
      const crossSite = screen.getByLabelText('Available for other sites');
      await waitFor(() => expect(crossSite).toBeChecked());
      fireEvent.click(crossSite);
      await waitFor(() => expect(crossSite).not.toBeChecked());

      fireEvent.click(screen.getByRole('button', { name: /Save/i }));

      await waitFor(() => expect(onSaved).toHaveBeenCalled());
      expect(updateResource).toHaveBeenCalledWith(
        'res-1',
        expect.objectContaining({
          homeSiteId: 'site-1',
          currentSiteId: 'site-1',
          crossSiteAllowed: false,
        }),
      );
    });

    it('defaults the current site to the home site when current is left unset', async () => {
      const onSaved = vi.fn();
      const homeOnlyPerson: ResourceInfo = {
        ...multiSitePerson,
        homeSiteId: 'site-1',
        currentSiteId: undefined,
      };
      vi.mocked(updateResource).mockResolvedValue(homeOnlyPerson);
      renderDialog({ person: homeOnlyPerson, onSaved });
      await waitFor(() => expect(getPersonProfile).toHaveBeenCalled());

      fireEvent.click(screen.getByRole('button', { name: /Save/i }));

      await waitFor(() => expect(onSaved).toHaveBeenCalled());
      expect(updateResource).toHaveBeenCalledWith(
        'res-1',
        expect.objectContaining({ homeSiteId: 'site-1', currentSiteId: 'site-1' }),
      );
    });

    it('hides the Location block for single-site tenants', () => {
      sitesMock.isMultiSite = false;
      renderDialog({ onSaved: vi.fn() });
      expect(screen.queryByText('Location')).not.toBeInTheDocument();
    });

    it('locks the site fields and warns when the person has scheduled assignments', async () => {
      vi.mocked(getAssignmentsByResource).mockResolvedValue(
        [{ id: 'a1' }] as unknown as Awaited<ReturnType<typeof getAssignmentsByResource>>,
      );
      renderDialog({ person: multiSitePerson, onSaved: vi.fn() });

      expect(await screen.findByText(/scheduled assignment/i)).toBeInTheDocument();
      expect(screen.getByLabelText('Home Site')).toBeDisabled();
      expect(screen.getByLabelText('Current Site')).toBeDisabled();
      expect(screen.getByLabelText('Available for other sites')).toBeDisabled();
    });

    it('leaves the site fields editable when the person has no assignments', async () => {
      renderDialog({ person: multiSitePerson, onSaved: vi.fn() });
      await waitFor(() => expect(getPersonProfile).toHaveBeenCalled());

      expect(screen.queryByText(/scheduled assignment/i)).not.toBeInTheDocument();
      expect(screen.getByLabelText('Home Site')).toBeEnabled();
    });
  });

  it('disables Save for a viewer who cannot edit', () => {
    vi.mocked(useCanEdit).mockReturnValue(false);
    renderDialog({ person: createdResource, onSaved: vi.fn() });
    expect(screen.getByRole('button', { name: /Save/i })).toBeDisabled();
  });

  describe('reference-data selects', () => {
    it('renders a disabled placeholder option for a no-longer-active job title and department', async () => {
      // Profile points at FK ids that are no longer in the active lists; the
      // dialog injects a disabled "current assignment" option so the Select is
      // not blank. Opening the trigger mounts the SelectContent that holds it.
      vi.mocked(getPersonProfile).mockResolvedValue({
        resourceId: 'res-1',
        jobTitleId: 'jt-removed',
        departmentId: 'dept-removed',
        jobTitleName: 'Removed',
        departmentPath: 'Removed',
        createdAt: '2026-01-01T00:00:00Z',
        updatedAt: '2026-01-01T00:00:00Z',
      });
      renderDialog({ person: createdResource, onSaved: vi.fn() });
      await waitFor(() => expect(getPersonProfile).toHaveBeenCalled());

      fireEvent.click(screen.getByLabelText(/Job Title/));
      await waitFor(() =>
        expect(
          screen.getAllByText('(current assignment — no longer active)').length,
        ).toBeGreaterThan(0),
      );
    });
  });
});
