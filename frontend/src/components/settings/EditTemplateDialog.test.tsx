import { render, screen, act } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { EditTemplateDialog } from './EditTemplateDialog';
import type { Template } from '@foundation/src/types/templates';
import * as criteriaApi from '@foundation/src/lib/api/criteria-api';

vi.mock('@foundation/src/lib/api/criteria-api');
vi.mock('@foundation/src/lib/api/template-api');

const mockTemplate: Template = {
  id: 'template-1',
  name: 'Test Template',
  description: 'Test Description',
  entityType: 'request',
  durationValue: 1,
  durationUnit: 'hours',
};

describe('EditTemplateDialog', () => {
  let queryClient: QueryClient;

  beforeEach(() => {
    queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
        mutations: { retry: false },
      },
    });
    vi.clearAllMocks();
    vi.mocked(criteriaApi.getCriteria).mockResolvedValue([]);
  });

  it('renders without crashing when closed', () => {
    const { container } = render(
      <QueryClientProvider client={queryClient}>
        <EditTemplateDialog
          open={false}
          onOpenChange={vi.fn()}
          template={mockTemplate}
          onSuccess={vi.fn()}
        />
      </QueryClientProvider>
    );
    expect(container).toBeTruthy();
  });

  it('renders dialog content when open', async () => {
    render(
      <QueryClientProvider client={queryClient}>
        <EditTemplateDialog
          open={true}
          onOpenChange={vi.fn()}
          template={mockTemplate}
          onSuccess={vi.fn()}
        />
      </QueryClientProvider>
    );
    
    expect(screen.getByText('Edit Request Template')).toBeInTheDocument();
    expect(screen.getByDisplayValue('Test Template')).toBeInTheDocument();
    expect(screen.getByDisplayValue('Test Description')).toBeInTheDocument();
    await act(async () => {});
  });
});
