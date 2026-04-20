import { render, screen, waitFor, fireEvent } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { PresetSettings } from './PresetSettings';
import * as presetApi from '@/lib/api/preset-api';

vi.mock('@/lib/api/preset-api');

describe('PresetSettings', () => {
  let queryClient: QueryClient;
  const user = userEvent.setup();

  const mockApplications: presetApi.PresetApplication[] = [
    {
      id: '1',
      presetId: 'manufacturing-ch-v1',
      presetVersion: '1.0.0',
      appliedAt: '2024-01-15T10:00:00Z',
      appliedByUserId: 'user-1',
    },
    {
      id: '2',
      presetId: 'test-preset-v2',
      presetVersion: '2.0.0',
      appliedAt: '2024-01-20T14:30:00Z',
      updatedAt: '2024-01-21T09:00:00Z',
      appliedByUserId: 'user-2',
    },
  ];

  const mockPreset: presetApi.Preset = {
    presetId: 'test-preset-v1',
    name: 'Test Preset',
    description: 'A test preset for testing',
    vendor: 'internal',
    industry: 'manufacturing',
    version: '1.0.0',
    createdAt: '2024-01-01T00:00:00Z',
    contents: {
      criteria: [
        { key: 'test-boolean', name: 'Test Boolean', dataType: 'Boolean' },
        { key: 'test-number', name: 'Test Number', dataType: 'Number', unit: 'kg' },
      ],
      spaceGroups: [
        { key: 'group-1', name: 'Group One', description: 'First group', color: '#FF5733', displayOrder: 1 },
      ],
      templates: {
        space: [{ key: 'space-tpl', name: 'Space Template', items: [] }],
        group: [{ key: 'group-tpl', name: 'Group Template', items: [] }],
        request: [{ key: 'request-tpl', name: 'Request Template', durationValue: 8, durationUnit: 'hours', items: [] }],
      },
    },
  };

  const mockValidationSuccess: presetApi.PresetValidationResult = {
    isValid: true,
    errors: [],
  };

  const mockValidationFailure: presetApi.PresetValidationResult = {
    isValid: false,
    errors: ['Invalid preset ID format', 'Missing required field: name'],
  };

  const mockApplicationSuccess: presetApi.PresetApplicationResult = {
    success: true,
    stats: {
      criteriaCreated: 5,
      criteriaUpdated: 2,
      spaceGroupsCreated: 3,
      spaceGroupsUpdated: 1,
      templatesCreated: 4,
      templatesUpdated: 0,
    },
  };

  const mockApplicationFailure: presetApi.PresetApplicationResult = {
    success: false,
    error: 'Database constraint violation',
    stats: {
      criteriaCreated: 0,
      criteriaUpdated: 0,
      spaceGroupsCreated: 0,
      spaceGroupsUpdated: 0,
      templatesCreated: 0,
      templatesUpdated: 0,
    },
  };

  // Helper to simulate file selection via the change event
  const simulateFileUpload = (content: string, filename = 'test.preset.json') => {
    const input = document.querySelector('input[type="file"]')!;
    const file = new File([content], filename, { type: 'application/json' });
    
    // Mock file.text() since jsdom doesn't support it
    Object.defineProperty(file, 'text', {
      value: () => Promise.resolve(content),
    });
    
    Object.defineProperty(input, 'files', {
      value: [file],
      configurable: true,
    });
    
    fireEvent.change(input);
  };

  beforeEach(() => {
    queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
        mutations: { retry: false },
      },
    });
    vi.clearAllMocks();
    
    vi.mocked(presetApi.getPresetApplications).mockResolvedValue(mockApplications);
    vi.mocked(presetApi.parsePresetFile).mockReturnValue(mockPreset);
    vi.mocked(presetApi.validatePreset).mockResolvedValue(mockValidationSuccess);
    vi.mocked(presetApi.applyPreset).mockResolvedValue(mockApplicationSuccess);
    vi.mocked(presetApi.downloadPreset).mockImplementation(() => {});
    
    global.alert = vi.fn();
  });

  const renderComponent = () => {
    return render(
      <QueryClientProvider client={queryClient}>
        <PresetSettings />
      </QueryClientProvider>
    );
  };

  describe('Initial Rendering', () => {
    it('renders preset section with title and description', async () => {
      renderComponent();

      expect(screen.getAllByRole('heading', { name: 'Presets' })[0]).toBeInTheDocument();
      expect(screen.getByText(/Import or export tenant configuration presets/)).toBeInTheDocument();
    });

    it('renders import and export buttons', async () => {
      renderComponent();

      expect(screen.getByRole('button', { name: /Import Preset/i })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: /Export Current Configuration/i })).toBeInTheDocument();
    });

    it('renders application history section', async () => {
      renderComponent();

      expect(screen.getByText('Application History')).toBeInTheDocument();
      expect(screen.getByText(/Presets that have been applied to this tenant/)).toBeInTheDocument();
    });
  });

  describe('Application History', () => {
    it('displays loading state while fetching history', () => {
      vi.mocked(presetApi.getPresetApplications).mockImplementation(
        () => new Promise(() => {}) // Never resolves
      );

      renderComponent();

      // Should show loading spinner
      expect(document.querySelector('.animate-spin')).toBeInTheDocument();
    });

    it('displays empty state when no presets applied', async () => {
      vi.mocked(presetApi.getPresetApplications).mockResolvedValue([]);

      renderComponent();

      await waitFor(() => {
        expect(screen.getByText(/No presets have been applied to this tenant yet/)).toBeInTheDocument();
      });
    });

    it('displays preset application history', async () => {
      renderComponent();

      await waitFor(() => {
        expect(screen.getByText('manufacturing-ch-v1')).toBeInTheDocument();
        expect(screen.getByText('test-preset-v2')).toBeInTheDocument();
        expect(screen.getByText('v1.0.0')).toBeInTheDocument();
        expect(screen.getByText('v2.0.0')).toBeInTheDocument();
      });
    });

    it('shows updated date when preset was re-applied', async () => {
      renderComponent();

      await waitFor(() => {
        expect(screen.getByText(/Updated:/)).toBeInTheDocument();
      });
    });
  });

  describe('Import Flow', () => {
    it('opens preview dialog when valid file is selected', async () => {
      renderComponent();
      simulateFileUpload(JSON.stringify(mockPreset));

      await waitFor(() => {
        expect(screen.getByText('Preview Preset')).toBeInTheDocument();
        expect(screen.getByText('Test Preset')).toBeInTheDocument();
      });
    });

    it('shows preset metadata in preview dialog', async () => {
      renderComponent();
      simulateFileUpload(JSON.stringify(mockPreset));

      await waitFor(() => {
        expect(screen.getByText(/test-preset-v1/)).toBeInTheDocument();
        expect(screen.getByText(/Vendor: internal/)).toBeInTheDocument();
        expect(screen.getByText(/Industry: manufacturing/)).toBeInTheDocument();
      });
    });

    it('shows content counts in preview dialog', async () => {
      renderComponent();
      simulateFileUpload(JSON.stringify(mockPreset));

      await waitFor(() => {
        expect(screen.getByText('Criteria')).toBeInTheDocument();
        expect(screen.getByText('Space Groups')).toBeInTheDocument();
        expect(screen.getByText('Templates')).toBeInTheDocument();
        // Should show counts
        expect(screen.getByText('2')).toBeInTheDocument(); // 2 criteria
        expect(screen.getByText('1')).toBeInTheDocument(); // 1 space group
        expect(screen.getByText('3')).toBeInTheDocument(); // 3 templates
      });
    });

    it('auto-validates preset on file selection', async () => {
      renderComponent();
      simulateFileUpload(JSON.stringify(mockPreset));

      await waitFor(() => {
        expect(presetApi.validatePreset).toHaveBeenCalled();
        // Check the first argument of the first call
        const firstCallArgs = vi.mocked(presetApi.validatePreset).mock.calls[0];
        expect(firstCallArgs[0]).toMatchObject({ presetId: 'test-preset-v1' });
      });
    });

    it('shows validation success message', async () => {
      renderComponent();
      simulateFileUpload(JSON.stringify(mockPreset));

      await waitFor(() => {
        expect(screen.getByText('Validation Passed')).toBeInTheDocument();
        expect(screen.getByText(/The preset is valid and ready to be applied/)).toBeInTheDocument();
      });
    });

    it('shows validation errors when preset is invalid', async () => {
      vi.mocked(presetApi.validatePreset).mockResolvedValue(mockValidationFailure);

      renderComponent();
      simulateFileUpload(JSON.stringify(mockPreset));

      await waitFor(() => {
        expect(screen.getByText('Validation Failed')).toBeInTheDocument();
        expect(screen.getByText('Invalid preset ID format')).toBeInTheDocument();
        expect(screen.getByText('Missing required field: name')).toBeInTheDocument();
      });
    });

    it('disables apply button when validation fails', async () => {
      vi.mocked(presetApi.validatePreset).mockResolvedValue(mockValidationFailure);

      renderComponent();
      simulateFileUpload(JSON.stringify(mockPreset));

      await waitFor(() => {
        const applyButton = screen.getByRole('button', { name: /Apply Preset/i });
        expect(applyButton).toBeDisabled();
      });
    });

    it('shows error alert for invalid JSON file', async () => {
      vi.mocked(presetApi.parsePresetFile).mockImplementation(() => {
        throw new Error('Invalid JSON format');
      });

      renderComponent();
      simulateFileUpload('not valid json', 'invalid.json');

      await waitFor(() => {
        expect(global.alert).toHaveBeenCalledWith('Invalid JSON format');
      });
    });
  });

  describe('Apply Preset', () => {
    it('applies preset when apply button is clicked', async () => {
      renderComponent();
      simulateFileUpload(JSON.stringify(mockPreset));

      await waitFor(() => {
        expect(screen.getByText('Validation Passed')).toBeInTheDocument();
      });

      const applyButton = screen.getByRole('button', { name: /Apply Preset/i });
      await user.click(applyButton);

      await waitFor(() => {
        expect(presetApi.applyPreset).toHaveBeenCalled();
        // Check the first argument of the first call
        const firstCallArgs = vi.mocked(presetApi.applyPreset).mock.calls[0];
        expect(firstCallArgs[0]).toMatchObject({ presetId: 'test-preset-v1' });
      });
    });

    it('shows success message with stats after successful apply', async () => {
      renderComponent();
      simulateFileUpload(JSON.stringify(mockPreset));

      await waitFor(() => {
        expect(screen.getByText('Validation Passed')).toBeInTheDocument();
      });

      const applyButton = screen.getByRole('button', { name: /Apply Preset/i });
      await user.click(applyButton);

      await waitFor(() => {
        expect(screen.getByText('Successfully Applied')).toBeInTheDocument();
        expect(screen.getByText(/Criteria created: 5/)).toBeInTheDocument();
        expect(screen.getByText(/Criteria updated: 2/)).toBeInTheDocument();
        expect(screen.getByText(/Groups created: 3/)).toBeInTheDocument();
        expect(screen.getByText(/Templates created: 4/)).toBeInTheDocument();
      });
    });

    it('shows Done button after successful apply', async () => {
      renderComponent();
      simulateFileUpload(JSON.stringify(mockPreset));

      await waitFor(() => {
        expect(screen.getByText('Validation Passed')).toBeInTheDocument();
      });

      await user.click(screen.getByRole('button', { name: /Apply Preset/i }));

      await waitFor(() => {
        expect(screen.getByRole('button', { name: 'Done' })).toBeInTheDocument();
      });
    });

    it('shows error message when apply fails', async () => {
      vi.mocked(presetApi.applyPreset).mockResolvedValue(mockApplicationFailure);

      renderComponent();
      simulateFileUpload(JSON.stringify(mockPreset));

      await waitFor(() => {
        expect(screen.getByText('Validation Passed')).toBeInTheDocument();
      });

      await user.click(screen.getByRole('button', { name: /Apply Preset/i }));

      await waitFor(() => {
        expect(screen.getByText('Application Failed')).toBeInTheDocument();
        expect(screen.getByText('Database constraint violation')).toBeInTheDocument();
      });
    });

    it('closes dialog and resets state when Done is clicked', async () => {
      renderComponent();
      simulateFileUpload(JSON.stringify(mockPreset));

      await waitFor(() => {
        expect(screen.getByText('Validation Passed')).toBeInTheDocument();
      });

      await user.click(screen.getByRole('button', { name: /Apply Preset/i }));

      await waitFor(() => {
        expect(screen.getByText('Successfully Applied')).toBeInTheDocument();
      });

      await user.click(screen.getByRole('button', { name: 'Done' }));

      await waitFor(() => {
        expect(screen.queryByText('Preview Preset')).not.toBeInTheDocument();
        expect(screen.queryByText('Successfully Applied')).not.toBeInTheDocument();
      });
    });
  });

  describe('Export Flow', () => {
    it('opens export dialog when export button is clicked', async () => {
      renderComponent();

      const exportButton = screen.getByRole('button', { name: /Export Current Configuration/i });
      await user.click(exportButton);

      await waitFor(() => {
        expect(screen.getByText('Export Configuration as Preset')).toBeInTheDocument();
      });
    });

    it('shows export form fields', async () => {
      renderComponent();

      await user.click(screen.getByRole('button', { name: /Export Current Configuration/i }));

      await waitFor(() => {
        expect(screen.getByLabelText('Preset ID')).toBeInTheDocument();
        expect(screen.getByLabelText('Name')).toBeInTheDocument();
        expect(screen.getByLabelText('Description (optional)')).toBeInTheDocument();
      });
    });

    it('disables export button when required fields are empty', async () => {
      renderComponent();

      await user.click(screen.getByRole('button', { name: /Export Current Configuration/i }));

      await waitFor(() => {
        const exportDialogButton = screen.getAllByRole('button', { name: /Export/i }).find(
          btn => btn.closest('[role="dialog"]')
        );
        expect(exportDialogButton).toBeDisabled();
      });
    });

    it('enables export button when required fields are filled', async () => {
      renderComponent();

      await user.click(screen.getByRole('button', { name: /Export Current Configuration/i }));

      const presetIdInput = screen.getByLabelText('Preset ID');
      const nameInput = screen.getByLabelText('Name');

      await user.type(presetIdInput, 'my-export-v1');
      await user.type(nameInput, 'My Export');

      await waitFor(() => {
        const exportDialogButton = screen.getAllByRole('button', { name: /Export/i }).find(
          btn => btn.closest('[role="dialog"]')
        );
        expect(exportDialogButton).toBeEnabled();
      });
    });

    it('calls export API and downloads file on success', async () => {
      const mockExportedPreset: presetApi.Preset = {
        ...mockPreset,
        presetId: 'exported-preset-v1',
        name: 'Exported Preset',
      };
      vi.mocked(presetApi.exportPreset).mockResolvedValue(mockExportedPreset);

      renderComponent();

      await user.click(screen.getByRole('button', { name: /Export Current Configuration/i }));

      const presetIdInput = screen.getByLabelText('Preset ID');
      const nameInput = screen.getByLabelText('Name');
      const descInput = screen.getByLabelText('Description (optional)');

      await user.type(presetIdInput, 'exported-preset-v1');
      await user.type(nameInput, 'Exported Preset');
      await user.type(descInput, 'My export description');

      const exportDialogButton = screen.getAllByRole('button', { name: /Export/i }).find(
        btn => btn.closest('[role="dialog"]')
      );
      await user.click(exportDialogButton!);

      await waitFor(() => {
        expect(presetApi.exportPreset).toHaveBeenCalledWith(
          'exported-preset-v1',
          'Exported Preset',
          'My export description'
        );
        expect(presetApi.downloadPreset).toHaveBeenCalledWith(mockExportedPreset);
      });
    });

    it('closes export dialog after successful export', async () => {
      vi.mocked(presetApi.exportPreset).mockResolvedValue(mockPreset);

      renderComponent();

      await user.click(screen.getByRole('button', { name: /Export Current Configuration/i }));

      await user.type(screen.getByLabelText('Preset ID'), 'test-v1');
      await user.type(screen.getByLabelText('Name'), 'Test');

      const exportDialogButton = screen.getAllByRole('button', { name: /Export/i }).find(
        btn => btn.closest('[role="dialog"]')
      );
      await user.click(exportDialogButton!);

      await waitFor(() => {
        expect(screen.queryByText('Export Configuration as Preset')).not.toBeInTheDocument();
      });
    });

    it('closes export dialog when cancel is clicked', async () => {
      renderComponent();

      await user.click(screen.getByRole('button', { name: /Export Current Configuration/i }));

      await waitFor(() => {
        expect(screen.getByText('Export Configuration as Preset')).toBeInTheDocument();
      });

      await user.click(screen.getByRole('button', { name: 'Cancel' }));

      await waitFor(() => {
        expect(screen.queryByText('Export Configuration as Preset')).not.toBeInTheDocument();
      });
    });
  });
});
