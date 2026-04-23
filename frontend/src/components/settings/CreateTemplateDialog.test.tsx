import { render, screen, act } from '@testing-library/react';
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { CreateTemplateDialog } from './CreateTemplateDialog';
import * as criteriaApi from '@foundation/src/lib/api/criteria-api';

vi.mock('@foundation/src/lib/api/criteria-api');
vi.mock('@foundation/src/lib/api/template-api');

describe('CreateTemplateDialog', () => {
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
        <CreateTemplateDialog
          open={false}
          onOpenChange={vi.fn()}
          onSuccess={vi.fn()}
        />
      </QueryClientProvider>
    );
    expect(container).toBeTruthy();
  });

  it('renders dialog content when open', async () => {
    render(
      <QueryClientProvider client={queryClient}>
        <CreateTemplateDialog
          open={true}
          onOpenChange={vi.fn()}
          onSuccess={vi.fn()}
        />
      </QueryClientProvider>
    );
    
    expect(screen.getByText('Create Request Template')).toBeInTheDocument();
    expect(screen.getByLabelText(/name/i)).toBeInTheDocument();
    await act(async () => {});
  });
});
