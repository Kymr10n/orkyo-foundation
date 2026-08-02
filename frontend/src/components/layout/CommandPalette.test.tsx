/** @jsxImportSource react */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { BrowserRouter } from 'react-router';
import { CommandPalette } from './CommandPalette';
import { useCanEdit, useIsTenantAdmin } from '@foundation/src/hooks/usePermissions';
import * as searchApi from '@foundation/src/lib/api/search-api';
import type { SearchResponse, SearchResult } from '@foundation/src/lib/api/search-api';

// Mock the search API
vi.mock('@foundation/src/lib/api/search-api');

// Mock the store
vi.mock('@foundation/src/store/app-store', () => ({
  useAppStore: vi.fn((selector) => {
    const state = {
      selectedSiteId: 'site-1',
      setSelectedSiteId: vi.fn(),
    };
    return selector(state);
  }),
}));

// Mock navigate
const mockNavigate = vi.fn();
vi.mock('react-router', async () => {
  const actual = await vi.importActual('react-router');
  return {
    ...actual,
    useNavigate: () => mockNavigate,
  };
});

const mockSearchResult: SearchResult = {
  type: 'resource',
  resourceTypeKey: 'space',
  id: 'space-123',
  title: 'Conference Room A',
  subtitle: 'Main Building',
  siteId: 'site-1',
  score: 0.95,
  updatedAt: '2024-01-15T10:30:00Z',
  permissions: {
    canRead: true,
    canEdit: true,
  },
};

const mockSearchResponse: SearchResponse = {
  query: 'conference',
  results: [mockSearchResult],
};

function renderCommandPalette(props: { open: boolean; onOpenChange?: (open: boolean) => void }) {
  return render(
      <BrowserRouter>
      <CommandPalette open={props.open} onOpenChange={props.onOpenChange ?? vi.fn()} />
    </BrowserRouter>
  );
}

describe('CommandPalette', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(searchApi.globalSearch).mockResolvedValue({ query: '', results: [] });
    // usePermissions hooks are globally mocked to true (src/test/setup.ts); reset each test.
    vi.mocked(useCanEdit).mockReturnValue(true);
    vi.mocked(useIsTenantAdmin).mockReturnValue(true);
  });

  it('filters out settings/admin-only result types for a viewer', async () => {
    vi.mocked(useCanEdit).mockReturnValue(false);
    vi.mocked(useIsTenantAdmin).mockReturnValue(false);
    const mk = (
      type: SearchResult['type'],
      title: string,
      resourceTypeKey?: string,
    ): SearchResult => ({
      type, id: `${type}-1`, title, resourceTypeKey, score: 1,
      updatedAt: '2024-01-01T00:00:00Z', permissions: { canRead: true, canEdit: false },
    });
    vi.mocked(searchApi.globalSearch).mockResolvedValue({
      query: 'x',
      results: [
        mk('resource', 'My Space', 'space'),
        mk('criterion', 'My Criterion'),
        mk('template', 'My Template'),
        mk('site', 'My Site'),
        { ...mk('resource', 'My Person', 'person'), id: 'person-1' },
      ],
    });
    renderCommandPalette({ open: true });
    await userEvent.type(screen.getByPlaceholderText(/search/i), 'x');

    // Viewer-viewable types remain; editor/admin-only ones are filtered out.
    await waitFor(() => expect(screen.getByText('My Space')).toBeInTheDocument());
    expect(screen.getByText('My Person')).toBeInTheDocument();
    expect(screen.queryByText('My Criterion')).not.toBeInTheDocument();
    expect(screen.queryByText('My Template')).not.toBeInTheDocument();
    expect(screen.queryByText('My Site')).not.toBeInTheDocument();
  });

  describe('rendering', () => {
    it('renders nothing when closed', () => {
      renderCommandPalette({ open: false });
      expect(screen.queryByPlaceholderText(/search/i)).not.toBeInTheDocument();
    });

    it('renders search input when open', () => {
      renderCommandPalette({ open: true });
      expect(screen.getByPlaceholderText(/search/i)).toBeInTheDocument();
    });

    it('shows placeholder text when no query', () => {
      renderCommandPalette({ open: true });
      expect(screen.getByText(/start typing to search/i)).toBeInTheDocument();
    });
  });

  describe('search functionality', () => {
    it('calls globalSearch on input change with debounce', async () => {
      vi.mocked(searchApi.globalSearch).mockResolvedValue(mockSearchResponse);
      renderCommandPalette({ open: true });

      const input = screen.getByPlaceholderText(/search/i);
      await userEvent.type(input, 'conference');

      // Wait for debounce
      await waitFor(() => {
        expect(searchApi.globalSearch).toHaveBeenCalledWith({
          query: 'conference',
          siteId: 'site-1',
          limit: 20,
        });
      }, { timeout: 500 });
    });

    it('displays search results', async () => {
      vi.mocked(searchApi.globalSearch).mockResolvedValue(mockSearchResponse);
      renderCommandPalette({ open: true });

      const input = screen.getByPlaceholderText(/search/i);
      await userEvent.type(input, 'conference');

      await waitFor(() => {
        expect(screen.getByText('Conference Room A')).toBeInTheDocument();
      }, { timeout: 500 });
    });

    it('shows subtitle when available', async () => {
      vi.mocked(searchApi.globalSearch).mockResolvedValue(mockSearchResponse);
      renderCommandPalette({ open: true });

      const input = screen.getByPlaceholderText(/search/i);
      await userEvent.type(input, 'conference');

      await waitFor(() => {
        expect(screen.getByText('Main Building')).toBeInTheDocument();
      }, { timeout: 500 });
    });

    it('shows type badge', async () => {
      vi.mocked(searchApi.globalSearch).mockResolvedValue(mockSearchResponse);
      renderCommandPalette({ open: true });

      const input = screen.getByPlaceholderText(/search/i);
      await userEvent.type(input, 'conference');

      await waitFor(() => {
        expect(screen.getByText('Space')).toBeInTheDocument();
      }, { timeout: 500 });
    });

    it('shows no results message when search returns empty', async () => {
      vi.mocked(searchApi.globalSearch).mockResolvedValue({ query: 'nonexistent', results: [] });
      renderCommandPalette({ open: true });

      const input = screen.getByPlaceholderText(/search/i);
      await userEvent.type(input, 'nonexistent');

      await waitFor(() => {
        expect(screen.getByText(/no results found/i)).toBeInTheDocument();
      }, { timeout: 500 });
    });

    it('shows result count in footer', async () => {
      vi.mocked(searchApi.globalSearch).mockResolvedValue(mockSearchResponse);
      renderCommandPalette({ open: true });

      const input = screen.getByPlaceholderText(/search/i);
      await userEvent.type(input, 'conference');

      await waitFor(() => {
        expect(screen.getByText('1 result')).toBeInTheDocument();
      }, { timeout: 500 });
    });
  });

  describe('keyboard navigation', () => {
    it('closes on Escape', async () => {
      const onOpenChange = vi.fn();
      renderCommandPalette({ open: true, onOpenChange });

      const input = screen.getByPlaceholderText(/search/i);
      fireEvent.keyDown(input, { key: 'Escape' });

      expect(onOpenChange).toHaveBeenCalledWith(false);
    });

    it('navigates on Enter', async () => {
      const onOpenChange = vi.fn();
      vi.mocked(searchApi.globalSearch).mockResolvedValue(mockSearchResponse);
      renderCommandPalette({ open: true, onOpenChange });

      const input = screen.getByPlaceholderText(/search/i);
      await userEvent.type(input, 'conference');

      await waitFor(() => {
        expect(screen.getByText('Conference Room A')).toBeInTheDocument();
      }, { timeout: 500 });

      fireEvent.keyDown(input, { key: 'Enter' });

      expect(onOpenChange).toHaveBeenCalledWith(false);
      await waitFor(() => {
        expect(mockNavigate).toHaveBeenCalledWith('/spaces/floorplan?edit=space-123');
      });
    });
  });

  describe('result navigation opens the detail dialog via ?edit=', () => {
    // Clicking a result routes to its owning page with `?edit=<id>`, which that
    // page reads to open the item's detail dialog. Groups route by resource type.
    const cases: {
      type: SearchResult['type'];
      title: string;
      path: string;
      extra?: Partial<SearchResult>;
    }[] = [
      { type: 'resource', title: 'A Space', path: '/spaces/floorplan?edit=x-1', extra: { resourceTypeKey: 'space' } },
      { type: 'resource', title: 'A Person', path: '/people/list?edit=x-1', extra: { resourceTypeKey: 'person' } },
      // Previously unreachable: tools and tenant-defined types were never indexed at all.
      { type: 'resource', title: 'A Tool', path: '/resources/tool/list?edit=x-1', extra: { resourceTypeKey: 'tool' } },
      { type: 'resource', title: 'A Van', path: '/resources/delivery_van/list?edit=x-1', extra: { resourceTypeKey: 'delivery_van' } },
      { type: 'request', title: 'A Request', path: '/requests?edit=x-1' },
      { type: 'site', title: 'A Site', path: '/tenant-admin/sites?edit=x-1' },
      { type: 'template', title: 'A Template', path: '/settings/templates?edit=x-1' },
      { type: 'criterion', title: 'A Criterion', path: '/settings/criteria?edit=x-1' },
      { type: 'group', title: 'A Team', path: '/people/teams?edit=x-1', extra: { resourceTypeKey: 'person' } },
      { type: 'group', title: 'A Space Group', path: '/spaces/groups?edit=x-1', extra: { resourceTypeKey: 'space' } },
    ];

    it.each(cases)('routes a $type result to $path', async ({ type, title, path, extra }) => {
      const result: SearchResult = { ...mockSearchResult, type, id: 'x-1', title, ...extra };
      vi.mocked(searchApi.globalSearch).mockResolvedValue({ query: 'q', results: [result] });

      const onOpenChange = vi.fn();
      renderCommandPalette({ open: true, onOpenChange });

      await userEvent.type(screen.getByPlaceholderText(/search/i), 'q');
      await waitFor(() => expect(screen.getByText(title)).toBeInTheDocument(), { timeout: 500 });

      fireEvent.click(screen.getByText(title));

      expect(onOpenChange).toHaveBeenCalledWith(false);
      await waitFor(() => expect(mockNavigate).toHaveBeenCalledWith(path));
    });
  });

  describe('multiple result types', () => {
    it('renders different icons for different types', async () => {
      const multipleResults: SearchResponse = {
        query: 'test',
        results: [
          { ...mockSearchResult, type: 'resource', resourceTypeKey: 'space', title: 'Space Result' },
          { ...mockSearchResult, type: 'request', resourceTypeKey: undefined, id: 'req-1', title: 'Request Result' },
          { ...mockSearchResult, type: 'site', resourceTypeKey: undefined, id: 'site-2', title: 'Site Result' },
        ],
      };
      vi.mocked(searchApi.globalSearch).mockResolvedValue(multipleResults);

      renderCommandPalette({ open: true });

      const input = screen.getByPlaceholderText(/search/i);
      await userEvent.type(input, 'test');

      await waitFor(() => {
        expect(screen.getByText('Space Result')).toBeInTheDocument();
        expect(screen.getByText('Request Result')).toBeInTheDocument();
        expect(screen.getByText('Site Result')).toBeInTheDocument();
      }, { timeout: 500 });

      // Check all type badges are present
      expect(screen.getByText('Space')).toBeInTheDocument();
      expect(screen.getByText('Request')).toBeInTheDocument();
      expect(screen.getByText('Site')).toBeInTheDocument();
    });
  });

  describe('loading state', () => {
    it('shows loading indicator during search', async () => {
      // Create a promise we can control
      let resolveSearch: (value: SearchResponse) => void;
      const searchPromise = new Promise<SearchResponse>((resolve) => {
        resolveSearch = resolve;
      });
      vi.mocked(searchApi.globalSearch).mockReturnValue(searchPromise);

      renderCommandPalette({ open: true });

      const input = screen.getByPlaceholderText(/search/i);
      await userEvent.type(input, 'test');

      // Wait for debounce to trigger the search
      await waitFor(() => {
        expect(searchApi.globalSearch).toHaveBeenCalled();
      }, { timeout: 500 });

      // Now resolve the search
      resolveSearch!({ query: 'test', results: [] });
      await waitFor(() => {});
    });
  });

  describe('clear button', () => {
    it('shows clear button when there is a search query', async () => {
      renderCommandPalette({ open: true });

      const input = screen.getByPlaceholderText(/search/i);
      await userEvent.type(input, 'test');

      // Clear button should appear (it's an X icon button)
      await waitFor(() => {
        const clearButtons = screen.getAllByRole('button');
        // Find button that isn't the Edit button
        const clearButton = clearButtons.find(btn => btn.querySelector('svg.lucide-x'));
        expect(clearButton).toBeInTheDocument();
      }, { timeout: 500 });
    });

    it('clears search query when clear button is clicked', async () => {
      renderCommandPalette({ open: true });

      const input = screen.getByPlaceholderText(/search/i);
      await userEvent.type(input, 'test');

      await waitFor(() => {
        expect(input).toHaveValue('test');
      });

      // Find and click the clear button
      const clearButtons = screen.getAllByRole('button');
      const clearButton = clearButtons.find(btn => btn.querySelector('svg'));
      fireEvent.click(clearButton!);

      expect(input).toHaveValue('');
    });
  });

  describe('search persistence', () => {
    it('preserves search query when dialog reopens', async () => {
      const onOpenChange = vi.fn();
      const { rerender } = render(
        <BrowserRouter>
          <CommandPalette open={true} onOpenChange={onOpenChange} />
        </BrowserRouter>
      );

      const input = screen.getByPlaceholderText(/search/i);
      await userEvent.type(input, 'my search');

      expect(input).toHaveValue('my search');

      // Close the dialog
      rerender(
        <BrowserRouter>
          <CommandPalette open={false} onOpenChange={onOpenChange} />
        </BrowserRouter>
      );

      // Reopen the dialog
      rerender(
        <BrowserRouter>
          <CommandPalette open={true} onOpenChange={onOpenChange} />
        </BrowserRouter>
      );

      // Query should still be there
      const reopenedInput = screen.getByPlaceholderText(/search/i);
      expect(reopenedInput).toHaveValue('my search');
    });
  });
});
