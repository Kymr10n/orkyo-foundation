import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { PersonEditDialog } from './PersonEditDialog';
import type { ResourceInfo } from '@foundation/src/lib/api/resources-api';

// API mocks. PersonEditDialog must call BOTH the resource API and the person-profile
// API on save (the whole point of phase 6 dialog completion).
vi.mock('@foundation/src/lib/api/resources-api', () => ({
  createResource: vi.fn(),
  updateResource: vi.fn(),
}));
vi.mock('@foundation/src/lib/api/person-profiles-api', () => ({
  getPersonProfile: vi.fn(),
  upsertPersonProfile: vi.fn(),
}));

import { createResource, updateResource } from '@foundation/src/lib/api/resources-api';
import { getPersonProfile, upsertPersonProfile } from '@foundation/src/lib/api/person-profiles-api';

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
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
    });
    vi.mocked(upsertPersonProfile).mockResolvedValue({
      resourceId: 'res-1',
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
    });
  });

  it('renders form fields for name, email, job title, department, and notes', () => {
    renderDialog();
    expect(screen.getByLabelText(/Name/)).toBeInTheDocument();
    expect(screen.getByLabelText(/Email/)).toBeInTheDocument();
    expect(screen.getByLabelText(/Job Title/)).toBeInTheDocument();
    expect(screen.getByLabelText(/Department/)).toBeInTheDocument();
    expect(screen.getByLabelText(/Notes/)).toBeInTheDocument();
  });

  it('on create, calls BOTH createResource AND upsertPersonProfile with the entered fields', async () => {
    const onSaved = vi.fn();
    renderDialog({ onSaved });

    fireEvent.change(screen.getByLabelText(/Name/), { target: { value: 'Alice' } });
    fireEvent.change(screen.getByLabelText(/Email/), { target: { value: 'alice@example.com' } });
    fireEvent.change(screen.getByLabelText(/Job Title/), { target: { value: 'Engineer' } });
    fireEvent.change(screen.getByLabelText(/Department/), { target: { value: 'Platform' } });

    fireEvent.click(screen.getByRole('button', { name: /Save/i }));

    await waitFor(() => expect(onSaved).toHaveBeenCalled());

    expect(createResource).toHaveBeenCalledWith(
      expect.objectContaining({ resourceTypeKey: 'person', name: 'Alice' }),
    );
    expect(upsertPersonProfile).toHaveBeenCalledWith(
      'res-1',
      expect.objectContaining({
        email: 'alice@example.com',
        jobTitle: 'Engineer',
        department: 'Platform',
      }),
    );
  });

  it('on edit, calls updateResource then upsertPersonProfile', async () => {
    const onSaved = vi.fn();
    renderDialog({ person: createdResource, onSaved });

    // Wait for profile load to populate the form.
    await waitFor(() => expect(getPersonProfile).toHaveBeenCalledWith('res-1'));

    fireEvent.change(screen.getByLabelText(/Job Title/), { target: { value: 'Tech Lead' } });
    fireEvent.click(screen.getByRole('button', { name: /Save/i }));

    await waitFor(() => expect(onSaved).toHaveBeenCalled());

    expect(updateResource).toHaveBeenCalledWith(
      'res-1',
      expect.objectContaining({ name: 'Alice' }),
    );
    expect(upsertPersonProfile).toHaveBeenCalledWith(
      'res-1',
      expect.objectContaining({ jobTitle: 'Tech Lead' }),
    );
  });

  it('disables Save until name is filled', () => {
    renderDialog();
    const save = screen.getByRole('button', { name: /Save/i });
    expect(save).toBeDisabled();
    fireEvent.change(screen.getByLabelText(/Name/), { target: { value: 'A' } });
    expect(save).not.toBeDisabled();
  });
});
