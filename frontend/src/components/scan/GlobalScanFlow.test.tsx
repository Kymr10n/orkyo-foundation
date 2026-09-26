import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { useCanEdit } from '@foundation/src/hooks/usePermissions';
import { useUiActionsStore } from '@foundation/src/store/ui-actions-store';
import { createTestQueryClient } from '@foundation/src/test-utils';
import { machineResourceType } from '@foundation/src/test-utils/resource-fixtures';
import { GlobalScanFlow } from './GlobalScanFlow';

const api = vi.hoisted(() => ({ lookupScanCode: vi.fn(), linkResourceScanCode: vi.fn() }));
vi.mock('@foundation/src/lib/api/resource-scan-codes-api', () => api);

const resourcesApi = vi.hoisted(() => ({ getResources: vi.fn() }));
vi.mock('@foundation/src/lib/api/resources-api', () => resourcesApi);

const typesApi = vi.hoisted(() => ({ getResourceTypes: vi.fn() }));
vi.mock('@foundation/src/lib/api/resource-types-api', () => typesApi);

const toast = vi.hoisted(() => Object.assign(vi.fn(), { success: vi.fn(), error: vi.fn(), info: vi.fn() }));
vi.mock('sonner', () => ({ toast }));

// Sites decide whether a resource label carries its site name. One site by default.
const sitesState = vi.hoisted(() => ({ sites: [{ id: 'site-1', name: 'North' }] }));
vi.mock('@foundation/src/hooks/useSites', () => ({
  useSites: () => ({ data: sitesState.sites }),
  useIsMultiSite: () => sitesState.sites.length > 1,
}));

vi.mock('@foundation/src/components/scan/QrScannerDialog', () => ({
  QrScannerDialog: ({ open, onScan }: { open: boolean; onScan: (code: string) => void }) =>
    open ? <button onClick={() => onScan('STICKER-9')}>Simulate scan</button> : null,
}));

/** Owns the scanner's open state, as AppLayout does. */
function Harness() {
  const [open, setOpen] = useState(true);
  return (
    <>
      <GlobalScanFlow open={open} onOpenChange={setOpen} />
      <span data-testid="scanner-open">{String(open)}</span>
    </>
  );
}

function renderFlow() {
  const { wrapper: Wrapper } = createTestQueryClient({ feedback: true });
  return render(
    <Wrapper>
      <Harness />
    </Wrapper>,
  );
}

const resource = (id: string, name: string, resourceTypeKey: string, homeSiteId?: string) => ({
  id,
  name,
  resourceTypeKey,
  homeSiteId,
});

const toolResourceType = { ...machineResourceType, id: 'rt-tool', key: 'tool', displayName: 'Tool', scanCodesEnabled: true };

/** The two pickers of the link dialog, told apart by their labels. */
const typeFilter = () => screen.getByRole('combobox', { name: 'Resource type' });
const resourcePicker = () => screen.getByRole('combobox', { name: 'Resource' });
const optionLabels = async () => {
  const listbox = await screen.findByRole('listbox');
  return within(listbox)
    .getAllByRole('option')
    .map((o) => o.textContent?.trim());
};

/** Renders the flow and scans STICKER-9, which the lookup answers as given. */
async function scanWith(lookupResult: unknown) {
  const user = userEvent.setup();
  api.lookupScanCode.mockResolvedValue(lookupResult);
  renderFlow();
  await user.click(screen.getByRole('button', { name: 'Simulate scan' }));
  return user;
}

/** What the last plain toast offered as its action. */
const toastAction = () => (toast.mock.lastCall![1] as { action: { label: string; onClick: () => void } }).action;

describe('GlobalScanFlow', () => {
  beforeEach(() => {
    useUiActionsStore.setState({ statusResourceId: null });
    typesApi.getResourceTypes.mockResolvedValue([
      machineResourceType,
      { ...machineResourceType, id: 'rt-person', key: 'person', displayName: 'Person', scanCodesEnabled: false },
    ]);
    resourcesApi.getResources.mockResolvedValue({
      items: [resource('r-drill', 'Drill', machineResourceType.key), resource('r-ana', 'Ana', 'person')],
    });
    api.linkResourceScanCode.mockResolvedValue({ id: 'c1', resourceId: 'r-drill', code: 'STICKER-9', createdAt: '' });
  });

  afterEach(() => {
    vi.clearAllMocks();
    vi.mocked(useCanEdit).mockReturnValue(true);
    sitesState.sites = [{ id: 'site-1', name: 'North' }];
  });

  it('hides the type filter when only one type has QR codes turned on', async () => {
    await scanWith({ status: 'unknown' });
    await screen.findByText('Link QR code');

    expect(screen.queryByRole('combobox', { name: 'Resource type' })).not.toBeInTheDocument();
    expect(resourcePicker()).toBeInTheDocument();
  });

  it('narrows the list by resource type, and drops a selection the filter hides', async () => {
    typesApi.getResourceTypes.mockResolvedValue([machineResourceType, toolResourceType]);
    resourcesApi.getResources.mockResolvedValue({
      items: [resource('r-drill', 'Drill', 'machine'), resource('r-saw', 'Saw', 'tool')],
    });
    await scanWith({ status: 'unknown' });
    await screen.findByText('Link QR code');

    // Unfiltered: every scannable resource, in one list.
    fireEvent.click(resourcePicker());
    await waitFor(async () => expect(await optionLabels()).toEqual(['Drill (Machine)', 'Saw (Tool)']));
    fireEvent.click(screen.getByRole('option', { name: 'Saw (Tool)' }));
    expect(screen.getByRole('button', { name: 'Link' })).toBeEnabled();

    // A filter that hides the chosen resource also unchooses it: submit would link it otherwise.
    fireEvent.click(typeFilter());
    fireEvent.click(screen.getByRole('option', { name: 'Machine' }));
    expect(screen.getByRole('button', { name: 'Link' })).toBeDisabled();

    fireEvent.click(resourcePicker());
    expect(await optionLabels()).toEqual(['Drill (Machine)']);
  });

  it('says which type came up empty', async () => {
    typesApi.getResourceTypes.mockResolvedValue([machineResourceType, toolResourceType]);
    resourcesApi.getResources.mockResolvedValue({ items: [resource('r-drill', 'Drill', 'machine')] });
    await scanWith({ status: 'unknown' });
    await screen.findByText('Link QR code');

    fireEvent.click(typeFilter());
    fireEvent.click(screen.getByRole('option', { name: 'Tool' }));
    fireEvent.click(resourcePicker());

    expect(await screen.findByText('No Tool resource found.')).toBeInTheDocument();
  });

  it('names the site of each resource on a multi-site tenant, so same-named ones read apart', async () => {
    sitesState.sites = [
      { id: 'site-1', name: 'North' },
      { id: 'site-2', name: 'South' },
    ];
    resourcesApi.getResources.mockResolvedValue({
      items: [resource('r-n', 'Break Room', 'machine', 'site-1'), resource('r-s', 'Break Room', 'machine', 'site-2')],
    });
    await scanWith({ status: 'unknown' });
    await screen.findByText('Link QR code');

    fireEvent.click(resourcePicker());
    await waitFor(async () =>
      expect(await optionLabels()).toEqual(['Break Room (Machine) · North', 'Break Room (Machine) · South']),
    );

    // The site is part of the label, so the search box finds it.
    fireEvent.change(screen.getByPlaceholderText('Search resources…'), { target: { value: 'south' } });
    expect(await optionLabels()).toEqual(['Break Room (Machine) · South']);
  });

  it('opens the status sheet of a linked resource', async () => {
    await scanWith({
      status: 'linked',
      resource: { id: 'r-drill', name: 'Drill', resourceTypeKey: 'machine', isActive: true },
    });

    await waitFor(() => expect(useUiActionsStore.getState().statusResourceId).toBe('r-drill'));
    expect(screen.getByTestId('scanner-open')).toHaveTextContent('false');
  });

  it('lets an Editor link an unknown code to a resource whose type has QR codes on', async () => {
    const user = await scanWith({ status: 'unknown' });
    expect(await screen.findByText('Link QR code')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Link' })).toBeDisabled();

    fireEvent.click(screen.getByRole('combobox'));
    const listbox = await screen.findByRole('listbox');
    await waitFor(() => expect(within(listbox).getAllByRole('option')).toHaveLength(1));
    fireEvent.click(within(listbox).getByText(`Drill (${machineResourceType.displayName})`));
    await user.click(screen.getByRole('button', { name: 'Link' }));

    await waitFor(() => expect(api.linkResourceScanCode).toHaveBeenCalledWith('r-drill', 'STICKER-9', false));
    await waitFor(() => expect(useUiActionsStore.getState().statusResourceId).toBe('r-drill'));
  });

  it('shows a failed link inside the dialog', async () => {
    api.linkResourceScanCode.mockRejectedValue(new Error("This code is already linked to 'Mill'."));
    const user = await scanWith({ status: 'unknown' });
    fireEvent.click(await screen.findByRole('combobox'));
    fireEvent.click(await within(await screen.findByRole('listbox')).findByText(/^Drill/));
    await user.click(screen.getByRole('button', { name: 'Link' }));

    expect(await screen.findByText("This code is already linked to 'Mill'.")).toBeInTheDocument();
    expect(useUiActionsStore.getState().statusResourceId).toBeNull();
  });

  it('tells a Viewer that the code is not linked, and offers another scan', async () => {
    vi.mocked(useCanEdit).mockReturnValue(false);
    await scanWith({ status: 'unknown' });

    await waitFor(() => expect(toast).toHaveBeenCalledWith('This QR code is not linked to a resource.', expect.anything()));
    expect(screen.getByTestId('scanner-open')).toHaveTextContent('false');
    expect(resourcesApi.getResources).not.toHaveBeenCalled();

    act(() => toastAction().onClick());
    expect(toastAction().label).toBe('Scan again');
    expect(screen.getByTestId('scanner-open')).toHaveTextContent('true');
  });

  it('does not name a resource whose type has scanning off', async () => {
    await scanWith({ status: 'type_disabled' });

    await waitFor(() => expect(toast).toHaveBeenCalledWith('Scanning is off for this resource type.', expect.anything()));
    expect(screen.queryByText('Link QR code')).not.toBeInTheDocument();
  });

  it('reports a lookup that fails', async () => {
    const user = userEvent.setup();
    api.lookupScanCode.mockRejectedValue(new Error('offline'));
    renderFlow();

    await user.click(screen.getByRole('button', { name: 'Simulate scan' }));

    await waitFor(() =>
      expect(toast.error).toHaveBeenCalledWith('The scanned code could not be checked. Try again.', expect.anything()),
    );
  });
});
