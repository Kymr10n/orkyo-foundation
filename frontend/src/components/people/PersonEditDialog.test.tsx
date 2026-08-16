import { describe, it, expect, vi, beforeEach } from 'vitest';
import type * as CustomFieldsApi from '@foundation/src/lib/api/resource-custom-fields-api';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
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
// People are resources, so a tenant can put custom fields on them; the dialog resolves the
// person type's id to ask for its definitions.
vi.mock('@foundation/src/hooks/useResourceTypes', () => ({
  useResourceTypes: () => ({ data: [{ id: 'rt-person', key: 'person' }] }),
}));
vi.mock('@foundation/src/lib/api/resource-custom-fields-api', async (importOriginal) => ({
  ...(await importOriginal<typeof CustomFieldsApi>()),
  getResourceCustomFields: vi.fn(),
}));

import { createResource, updateResource } from '@foundation/src/lib/api/resources-api';
import { getPersonProfile, upsertPersonProfile } from '@foundation/src/lib/api/person-profiles-api';
import { getJobTitles } from '@foundation/src/lib/api/job-titles-api';
import { getDepartmentTree } from '@foundation/src/lib/api/departments-api';
import { useCanEdit } from '@foundation/src/hooks/usePermissions';
import { getResourceCustomFields } from '@foundation/src/lib/api/resource-custom-fields-api';
import { toast } from 'sonner';

const createdResource: ResourceInfo = {
  id: 'res-1',
  resourceTypeId: 'rt-person',
  resourceTypeKey: 'person',
  name: 'Alice',
  allocationMode: 'Exclusive',
  baseAvailabilityPercent: 100,
  isPhysical: false,
  capacity: 1,
  isActive: true,
  // Directory fields ride on the resource — the dialog no longer fetches a profile.
  email: 'alice@example.com',
  jobTitleId: 'jt-engineer',
  departmentId: 'dept-platform',
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
    vi.mocked(getResourceCustomFields).mockResolvedValue([]);
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

    await waitFor(() => expect(screen.getByRole('button', { name: /Save/i })).toBeEnabled());
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

  it('renders Details and Allocation tabs, hiding Location for single-site tenants', () => {
    renderDialog();
    expect(screen.getByRole('tab', { name: /Details/ })).toBeInTheDocument();
    expect(screen.getByRole('tab', { name: /Allocation/ })).toBeInTheDocument();
    expect(screen.queryByRole('tab', { name: 'Location' })).not.toBeInTheDocument();
  });

  it('on create, sends the directory fields in the same request as the resource', async () => {
    const onSaved = vi.fn();
    renderDialog({ onSaved });

    fireEvent.change(screen.getByLabelText(/Name/), { target: { value: 'Alice' } });
    fireEvent.change(screen.getByLabelText(/Email/), { target: { value: 'alice@example.com' } });

    await waitFor(() => expect(screen.getByRole('button', { name: /Save/i })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: /Save/i }));

    await waitFor(() => expect(onSaved).toHaveBeenCalled());

    // One write, not two: reference data left unselected is sent as null, which the generic
    // update now reads as "no assignment" rather than "not editing".
    expect(createResource).toHaveBeenCalledWith(
      expect.objectContaining({
        resourceTypeKey: 'person',
        name: 'Alice',
        email: 'alice@example.com',
        jobTitleId: null,
        departmentId: null,
      }),
    );
  });

  it('on edit, sends the directory fields as ids in the one resource request', async () => {
    // The shape is what matters here: id-based fields, never the legacy jobTitle/department
    // strings. Resolved values are covered backend-side; happy-dom plus Radix Select has proven
    // brittle for pinning the specific values through the select's microtask ordering.
    const onSaved = vi.fn();
    renderDialog({ person: createdResource, onSaved });


    await waitFor(() => expect(screen.getByRole('button', { name: /Save/i })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: /Save/i }));

    await waitFor(() => expect(onSaved).toHaveBeenCalled());

    expect(updateResource).toHaveBeenCalledWith(
      'res-1',
      expect.objectContaining({ name: 'Alice' }),
    );

    const [, body] = vi.mocked(updateResource).mock.calls[0];
    expect(body).toHaveProperty('jobTitleId');
    expect(body).toHaveProperty('departmentId');
    expect(body).not.toHaveProperty('jobTitle');
    expect(body).not.toHaveProperty('department');
    // The profile upsert is gone — one round trip, so a save cannot half-apply.
    expect(upsertPersonProfile).not.toHaveBeenCalled();
  });

  it('disables Save until name is filled', async () => {
    renderDialog();
    const save = screen.getByRole('button', { name: /Save/i });
    expect(save).toBeDisabled();
    fireEvent.change(screen.getByLabelText(/Name/), { target: { value: 'A' } });
    // Also waits on the type's custom-field definitions: until they land nothing knows
    // whether one of them is required.
    await waitFor(() => expect(save).not.toBeDisabled());
  });

  it('shows a success toast with "Person created" after a successful create', async () => {
    renderDialog({ onSaved: vi.fn() });

    fireEvent.change(screen.getByLabelText(/Name/), { target: { value: 'Alice' } });
    await waitFor(() => expect(screen.getByRole('button', { name: /Save/i })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: /Save/i }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Person created'));
  });

  it('shows a success toast with "Person updated" after a successful edit', async () => {
    renderDialog({ person: createdResource, onSaved: vi.fn() });

    await waitFor(() => expect(screen.getByRole('button', { name: /Save/i })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: /Save/i }));

    await waitFor(() => expect(toast.success).toHaveBeenCalledWith('Person updated'));
  });

  it('shows an error toast when create fails', async () => {
    vi.mocked(createResource).mockRejectedValue(new Error('Server error'));
    renderDialog({ onSaved: vi.fn() });

    fireEvent.change(screen.getByLabelText(/Name/), { target: { value: 'Alice' } });
    await waitFor(() => expect(screen.getByRole('button', { name: /Save/i })).toBeEnabled());
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
    // Leave email blank. Wait for the custom-field definitions first: a submit before they
    // land is refused on purpose, since nothing knows yet whether one of them is required.
    await waitFor(() => expect(screen.getByRole('button', { name: /Save/i })).toBeEnabled());
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

    await waitFor(() => expect(screen.getByRole('button', { name: /Save/i })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: /Save/i }));

    await waitFor(() => expect(createResource).toHaveBeenCalled());
    expect(createResource).toHaveBeenCalledWith(
      expect.objectContaining({
        description: 'A teammate',
        baseAvailabilityPercent: 80,
        notes: 'Prefers mornings',
      }),
    );
  });

  describe('multi-site Location block', () => {
    const multiSitePerson: ResourceInfo = {
      ...createdResource,
      homeSiteId: 'site-1',
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

      expect(screen.getByRole('tab', { name: 'Location' })).toBeInTheDocument();
      expect(screen.getByLabelText('Home Site')).toBeInTheDocument();
      expect(screen.queryByLabelText('Current Site')).not.toBeInTheDocument();
      expect(
        screen.getByLabelText('Available for other sites'),
      ).toBeInTheDocument();
    });

    it('saves the home-site fields, toggling cross-site availability', async () => {
      const onSaved = vi.fn();
      renderDialog({ person: multiSitePerson, onSaved });

      // Wait for the form to sync from the person (checkbox starts checked),
      // then turn off "available for other sites".
      const crossSite = screen.getByLabelText('Available for other sites');
      await waitFor(() => expect(crossSite).toBeChecked());
      fireEvent.click(crossSite);
      await waitFor(() => expect(crossSite).not.toBeChecked());

      await waitFor(() => expect(screen.getByRole('button', { name: /Save/i })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: /Save/i }));

      await waitFor(() => expect(onSaved).toHaveBeenCalled());
      expect(updateResource).toHaveBeenCalledWith(
        'res-1',
        expect.objectContaining({
          homeSiteId: 'site-1',
          crossSiteAllowed: false,
        }),
      );
    });

    it('hides the Location block for single-site tenants', () => {
      sitesMock.isMultiSite = false;
      renderDialog({ onSaved: vi.fn() });
      expect(screen.queryByText('Location')).not.toBeInTheDocument();
    });

    it('keeps the home site editable regardless of the person\'s schedule', async () => {
      // The home site is only the administrative anchor / idle-time location now;
      // in-window location is derived from assignments, so there is nothing to lock.
      renderDialog({ person: multiSitePerson, onSaved: vi.fn() });

      expect(screen.getByLabelText('Home Site')).toBeEnabled();
      expect(screen.getByLabelText('Available for other sites')).toBeEnabled();
    });
  });

  describe('validation status badges', () => {
    it('shows no banner or dot for a clean new form', () => {
      renderDialog();
      expect(screen.queryByTestId('status-banner')).not.toBeInTheDocument();
      expect(screen.queryByLabelText('details warning')).not.toBeInTheDocument();
    });

    it('flags an invalid email with a red Details dot and a banner', async () => {
      renderDialog({ onSaved: vi.fn() });
      fireEvent.change(screen.getByLabelText(/Email/), { target: { value: 'notanemail' } });

      const dot = await screen.findByLabelText('details warning');
      expect(dot.className).toContain('bg-destructive');
      expect(screen.getByTestId('status-banner')).toHaveTextContent(/email address is not valid/i);
    });

    it('flags a deactivated job title/department assignment with an amber warning', async () => {
      renderDialog({
        // The deactivated assignment now arrives on the resource itself.
        person: { ...createdResource, jobTitleId: 'jt-removed', departmentId: 'dept-removed' }, onSaved: vi.fn() });

      const banner = await screen.findByTestId('status-banner');
      expect(banner).toHaveTextContent(/job title is no longer active/i);
      expect(banner).toHaveTextContent(/department is no longer active/i);
      expect(screen.getByLabelText('details warning').className).toContain('bg-amber-500');
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
      renderDialog({
        // The deactivated assignment now arrives on the resource itself.
        person: { ...createdResource, jobTitleId: 'jt-removed', departmentId: 'dept-removed' }, onSaved: vi.fn() });

      fireEvent.click(screen.getByLabelText(/Job Title/));
      await waitFor(() =>
        expect(
          screen.getAllByText('(current assignment — no longer active)').length,
        ).toBeGreaterThan(0),
      );
    });

    it('preserves deactivated FK ids on save when radix-select fires spurious onValueChange("")', async () => {
      // Regression for radix-select 2.3+: mounting SelectContent with a controlled
      // value that doesn't match any item fires onValueChange('') — our guard must
      // drop that call so the form value is NOT cleared before save.
      const onSaved = vi.fn();
      // The deactivated assignment arrives on the resource itself now.
      renderDialog({
        person: { ...createdResource, jobTitleId: 'jt-removed', departmentId: 'dept-removed' },
        onSaved,
      });

      // Open job-title select — mounts SelectContent and triggers radix's spurious
      // onValueChange('') for the unmatched controlled value.
      fireEvent.click(screen.getByLabelText(/Job Title/));
      await waitFor(() =>
        expect(screen.getAllByText('(current assignment — no longer active)').length).toBeGreaterThan(0),
      );
      // Close with Escape (radix handles it before the dialog does).
      fireEvent.keyDown(document.body, { key: 'Escape', code: 'Escape' });
      await waitFor(() => expect(screen.queryByRole('listbox')).toBeNull());

      // Same for Department.
      fireEvent.click(screen.getByLabelText(/Department/));
      await waitFor(() =>
        expect(screen.getAllByText('(current assignment — no longer active)').length).toBeGreaterThan(0),
      );
      fireEvent.keyDown(document.body, { key: 'Escape', code: 'Escape' });
      await waitFor(() => expect(screen.queryByRole('listbox')).toBeNull());

      await waitFor(() => expect(screen.getByRole('button', { name: /Save/i })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: /Save/i }));
      await waitFor(() => expect(onSaved).toHaveBeenCalled());

      // Both deactivated IDs must be preserved — not cleared to null. This matters more now
      // that null genuinely erases: a spurious onValueChange('') would wipe the assignment
      // rather than being harmlessly ignored.
      expect(updateResource).toHaveBeenCalledWith(
        'res-1',
        expect.objectContaining({
          jobTitleId: 'jt-removed',
          departmentId: 'dept-removed',
        }),
      );
    });
  });

  // ── custom fields ─────────────────────────────────────────────────────────

  function personField(overrides: Partial<CustomFieldsApi.ResourceCustomField> & { key: string }) {
    return {
      id: `field-${overrides.key}`,
      resourceTypeId: 'rt-person',
      label: overrides.key,
      dataType: 'text' as const,
      isRequired: false,
      sortOrder: 0,
      isActive: true,
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
      ...overrides,
    };
  }

  it('has no Custom Fields tab when the tenant defined none', async () => {
    renderDialog({ onSaved: vi.fn() });

    await screen.findByLabelText(/Name/);
    await waitFor(() => expect(getResourceCustomFields).toHaveBeenCalled());
    expect(screen.queryByRole('tab', { name: /Custom Fields/ })).not.toBeInTheDocument();
  });

  it('gives the tenant’s fields their own tab', async () => {
    vi.mocked(getResourceCustomFields).mockResolvedValue([
      personField({ key: 'badge_number', label: 'Badge number' }),
      personField({ key: 'retired', label: 'Retired field', isActive: false }),
    ]);

    renderDialog({ onSaved: vi.fn() });

    await userEvent.click(await screen.findByRole('tab', { name: /Custom Fields/ }));
    expect(screen.getByLabelText('Badge number')).toBeInTheDocument();
    // Retired fields keep their values but stop being asked for.
    expect(screen.queryByLabelText('Retired field')).not.toBeInTheDocument();
  });

  it('saves custom-field values with the person', async () => {
    vi.mocked(getResourceCustomFields).mockResolvedValue([
      personField({ key: 'badge_number', label: 'Badge number' }),
    ]);

    renderDialog({ onSaved: vi.fn() });

    fireEvent.change(await screen.findByLabelText(/Name/), { target: { value: 'Alice' } });
    await userEvent.click(await screen.findByRole('tab', { name: /Custom Fields/ }));
    fireEvent.change(screen.getByLabelText('Badge number'), { target: { value: 'B-7' } });
    await waitFor(() => expect(screen.getByRole('button', { name: /Save/i })).toBeEnabled());
    fireEvent.click(screen.getByRole('button', { name: /Save/i }));

    await waitFor(() =>
      expect(createResource).toHaveBeenCalledWith(
        expect.objectContaining({ customFields: { badge_number: 'B-7' } }),
      ),
    );
  });

  it('will not save a person missing a required custom field', async () => {
    vi.mocked(getResourceCustomFields).mockResolvedValue([
      personField({ key: 'badge_number', label: 'Badge number', isRequired: true }),
    ]);

    renderDialog({ onSaved: vi.fn() });

    fireEvent.change(await screen.findByLabelText(/Name/), { target: { value: 'Alice' } });
    await screen.findByRole('tab', { name: /Custom Fields/ });
    expect(screen.getByRole('button', { name: /Save/i })).toBeDisabled();

    await userEvent.click(screen.getByRole('tab', { name: /Custom Fields/ }));
    fireEvent.change(screen.getByLabelText(/Badge number/), { target: { value: 'B-7' } });
    await waitFor(() => expect(screen.getByRole('button', { name: /Save/i })).toBeEnabled());
  });

  it('says which field is missing while its tab is out of sight', async () => {
    // The panel is hidden behind another tab, so a disabled Save has to be explained here.
    vi.mocked(getResourceCustomFields).mockResolvedValue([
      personField({ key: 'badge_number', label: 'Badge number', isRequired: true }),
    ]);

    renderDialog({ onSaved: vi.fn() });

    // Still on Details, where the field is not visible.
    expect(await screen.findByText('Badge number is required.')).toBeInTheDocument();
    expect(screen.getByLabelText('custom fields warning')).toBeInTheDocument();
  });

  it('sends a submit with a missing required field to the tab that owns it', async () => {
    vi.mocked(getResourceCustomFields).mockResolvedValue([
      personField({ key: 'badge_number', label: 'Badge number', isRequired: true }),
    ]);

    renderDialog({ onSaved: vi.fn() });

    fireEvent.change(await screen.findByLabelText(/Name/), { target: { value: 'Alice' } });
    await screen.findByRole('tab', { name: /Custom Fields/ });
    // Enter submits the form even while the Save button is disabled.
    fireEvent.submit(screen.getByLabelText(/Name/).closest('form')!);

    await waitFor(() =>
      expect(screen.getByRole('tab', { name: /Custom Fields/ })).toHaveAttribute(
        'aria-selected',
        'true',
      ),
    );
    expect(toast.error).toHaveBeenCalledWith(
      'Failed to create person',
      expect.objectContaining({ description: 'Fill in the required custom fields' }),
    );
    expect(createResource).not.toHaveBeenCalled();
  });

  it('will not submit before the field definitions have arrived', async () => {
    // Enter submits even while Save is disabled, and until the definitions land nothing knows
    // whether one of them is required — so the gate has to be on the submit, not the button.
    let resolve!: (fields: CustomFieldsApi.ResourceCustomField[]) => void;
    vi.mocked(getResourceCustomFields).mockReturnValue(
      new Promise<CustomFieldsApi.ResourceCustomField[]>((r) => { resolve = r; }),
    );

    renderDialog({ onSaved: vi.fn() });

    fireEvent.change(await screen.findByLabelText(/Name/), { target: { value: 'Alice' } });
    fireEvent.submit(screen.getByLabelText(/Name/).closest('form')!);

    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith(
        'Failed to create person',
        expect.objectContaining({ description: expect.stringContaining('Still loading') }),
      ),
    );
    expect(createResource).not.toHaveBeenCalled();

    resolve([]);
    await waitFor(() => expect(screen.getByRole('button', { name: /Save/i })).toBeEnabled());
  });
});
