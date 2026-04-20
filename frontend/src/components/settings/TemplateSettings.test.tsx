import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { TemplateSettings } from './TemplateSettings';
import { getTemplates, deleteTemplate } from '@/lib/api/template-api';

const mockGetTemplates = vi.mocked(getTemplates);
const mockDeleteTemplate = vi.mocked(deleteTemplate);

vi.mock('@/lib/api/template-api', () => ({
  getTemplates: vi.fn(() => Promise.resolve([])),
  createTemplate: vi.fn(),
  deleteTemplate: vi.fn(() => Promise.resolve()),
}));

vi.mock('@/hooks/useImportExport', () => ({
  useExportHandler: vi.fn(),
  useImportHandler: vi.fn(),
}));

vi.mock('@/lib/utils/export-handlers', () => ({
  exportTemplates: vi.fn(),
  importTemplates: vi.fn(),
}));

vi.mock('./CreateTemplateDialog', () => ({
  CreateTemplateDialog: () => null,
}));

vi.mock('./EditTemplateDialog', () => ({
  EditTemplateDialog: () => null,
}));

const mockTemplates = [
  { id: 't1', name: 'Weekly Meeting', description: 'A weekly recurring meeting', entityType: 'request' as const, durationValue: 60, durationUnit: 'minutes', createdAt: '2024-01-15T00:00:00Z', items: [] },
  { id: 't2', name: 'Quick Standup', description: '', entityType: 'request' as const, durationValue: 15, durationUnit: 'minutes', createdAt: '2024-02-01T00:00:00Z', items: [] },
];

function renderTemplateSettings() {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return render(
    <QueryClientProvider client={qc}>
      <TemplateSettings />
    </QueryClientProvider>,
  );
}

describe('TemplateSettings', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockGetTemplates.mockResolvedValue([]);
    global.confirm = vi.fn(() => true);
    global.alert = vi.fn();
  });

  it('renders header and create button', async () => {
    renderTemplateSettings();
    await waitFor(() => {
      expect(screen.getByText('Templates')).toBeInTheDocument();
    });
    expect(screen.getByText('Add Template')).toBeInTheDocument();
  });

  it('shows loading state', () => {
    renderTemplateSettings();
    expect(screen.getByText('Loading request templates...')).toBeInTheDocument();
  });

  it('shows empty state when no templates', async () => {
    renderTemplateSettings();
    await waitFor(() => {
      expect(screen.getByText('No request templates defined yet')).toBeInTheDocument();
    });
    expect(screen.getByText('Create your first template')).toBeInTheDocument();
  });

  it('renders template cards when templates exist', async () => {
    mockGetTemplates.mockResolvedValue(mockTemplates);
    renderTemplateSettings();
    await waitFor(() => {
      expect(screen.getByText('Weekly Meeting')).toBeInTheDocument();
    });
    expect(screen.getByText('Quick Standup')).toBeInTheDocument();
    expect(screen.getByText('A weekly recurring meeting')).toBeInTheDocument();
    expect(screen.getAllByText('60 minutes').length).toBeGreaterThanOrEqual(1);
  });

  it('shows error state when loading fails', async () => {
    mockGetTemplates.mockRejectedValue(new Error('Network error'));
    renderTemplateSettings();
    await waitFor(() => {
      expect(screen.getByText('Network error')).toBeInTheDocument();
    });
    expect(screen.getByText('Retry')).toBeInTheDocument();
  });

  it('retries loading on Retry click', async () => {
    const user = userEvent.setup();
    mockGetTemplates.mockRejectedValueOnce(new Error('Network error'));
    renderTemplateSettings();
    await waitFor(() => {
      expect(screen.getByText('Retry')).toBeInTheDocument();
    });
    mockGetTemplates.mockResolvedValueOnce(mockTemplates);
    await user.click(screen.getByText('Retry'));
    await waitFor(() => {
      expect(screen.getByText('Weekly Meeting')).toBeInTheDocument();
    });
  });

  it('deletes a template with confirmation', async () => {
    const user = userEvent.setup();
    mockGetTemplates.mockResolvedValue(mockTemplates);
    renderTemplateSettings();
    await waitFor(() => {
      expect(screen.getByText('Weekly Meeting')).toBeInTheDocument();
    });
    // Click first delete button (Trash2 icon buttons)
    const deleteButtons = screen.getAllByRole('button').filter(b => b.querySelector('.text-destructive'));
    await user.click(deleteButtons[0]);
    await waitFor(() => {
      expect(global.confirm).toHaveBeenCalledWith(expect.stringContaining('Weekly Meeting'));
      expect(mockDeleteTemplate).toHaveBeenCalledWith('t1', expect.any(Object));
    });
  });

  it('does not delete when confirmation is declined', async () => {
    global.confirm = vi.fn(() => false);
    const user = userEvent.setup();
    mockGetTemplates.mockResolvedValue(mockTemplates);
    renderTemplateSettings();
    await waitFor(() => {
      expect(screen.getByText('Weekly Meeting')).toBeInTheDocument();
    });
    const deleteButtons = screen.getAllByRole('button').filter(b => b.querySelector('.text-destructive'));
    await user.click(deleteButtons[0]);
    expect(mockDeleteTemplate).not.toHaveBeenCalled();
  });

  it('shows alert on delete error', async () => {
    mockDeleteTemplate.mockRejectedValueOnce(new Error('Delete failed'));
    const user = userEvent.setup();
    mockGetTemplates.mockResolvedValue(mockTemplates);
    renderTemplateSettings();
    await waitFor(() => {
      expect(screen.getByText('Weekly Meeting')).toBeInTheDocument();
    });
    const deleteButtons = screen.getAllByRole('button').filter(b => b.querySelector('.text-destructive'));
    await user.click(deleteButtons[0]);
    await waitFor(() => {
      expect(global.alert).toHaveBeenCalledWith('Delete failed');
    });
  });

  it('shows created date for templates', async () => {
    mockGetTemplates.mockResolvedValue(mockTemplates);
    renderTemplateSettings();
    await waitFor(() => {
      expect(screen.getByText('Weekly Meeting')).toBeInTheDocument();
    });
    expect(screen.getByText(/Created:.*1\/15\/2024/)).toBeInTheDocument();
  });
});
