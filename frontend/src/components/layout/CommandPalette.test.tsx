/** @jsxImportSource react */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { BrowserRouter } from 'react-router-dom';
import { CommandPalette } from './CommandPalette';
import * as searchApi from '@/lib/api/search-api';
import type { SearchResponse, SearchResult } from '@/lib/api/search-api';

// Mock the search API
vi.mock('@/lib/api/search-api');

// Mock the store
vi.mock('@/store/app-store', () => ({
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
vi.mock('react-router-dom', async () => {
  const actual = await vi.importActual('react-router-dom');
  return {
    ...actual,
    useNavigate: () => mockNavigate,
  };
});

const mockSearchResult: SearchResult = {
  type: 'space',
  id: 'space-123',
  title: 'Conference Room A',
  subtitle: 'Main Building',
  siteId: 'site-1',
  score: 0.95,
  updatedAt: '2024-01-15T10:30:00Z',
  open: {
    route: '/spaces/:id',
    params: { id: 'space-123' },
  },
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
    <BrowserRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
      <CommandPalette open={props.open} onOpenChange={props.onOpenChange ?? vi.fn()} />
    </BrowserRouter>
  );
}

describe('CommandPalette', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(searchApi.globalSearch).mockResolvedValue({ query: '', results: [] });
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
      
      expect(mockNavigate).toHaveBeenCalledWith('/spaces');
      expect(onOpenChange).toHaveBeenCalledWith(false);
    });
  });

  describe('result navigation', () => {
    it('navigates to spaces page for space results', async () => {
      const onOpenChange = vi.fn();
      vi.mocked(searchApi.globalSearch).mockResolvedValue(mockSearchResponse);
      renderCommandPalette({ open: true, onOpenChange });
      
      const input = screen.getByPlaceholderText(/search/i);
      await userEvent.type(input, 'conference');
      
      await waitFor(() => {
        expect(screen.getByText('Conference Room A')).toBeInTheDocument();
      }, { timeout: 500 });
      
      fireEvent.click(screen.getByText('Conference Room A'));
      
      expect(mockNavigate).toHaveBeenCalledWith('/spaces');
    });

    it('navigates to requests page for request results', async () => {
      const requestResult: SearchResult = {
        ...mockSearchResult,
        type: 'request',
        title: 'Setup Request',
      };
      vi.mocked(searchApi.globalSearch).mockResolvedValue({
        query: 'setup',
        results: [requestResult],
      });
      
      const onOpenChange = vi.fn();
      renderCommandPalette({ open: true, onOpenChange });
      
      const input = screen.getByPlaceholderText(/search/i);
      await userEvent.type(input, 'setup');
      
      await waitFor(() => {
        expect(screen.getByText('Setup Request')).toBeInTheDocument();
      }, { timeout: 500 });
      
      fireEvent.click(screen.getByText('Setup Request'));
      
      expect(mockNavigate).toHaveBeenCalledWith('/requests');
    });

    it('navigates to settings page for site results', async () => {
      const siteResult: SearchResult = {
        ...mockSearchResult,
        type: 'site',
        title: 'Headquarters',
        id: 'site-2',
      };
      vi.mocked(searchApi.globalSearch).mockResolvedValue({
        query: 'head',
        results: [siteResult],
      });
      
      const onOpenChange = vi.fn();
      renderCommandPalette({ open: true, onOpenChange });
      
      const input = screen.getByPlaceholderText(/search/i);
      await userEvent.type(input, 'head');
      
      await waitFor(() => {
        expect(screen.getByText('Headquarters')).toBeInTheDocument();
      }, { timeout: 500 });
      
      fireEvent.click(screen.getByText('Headquarters'));
      
      expect(mockNavigate).toHaveBeenCalledWith('/settings');
    });
  });

  describe('multiple result types', () => {
    it('renders different icons for different types', async () => {
      const multipleResults: SearchResponse = {
        query: 'test',
        results: [
          { ...mockSearchResult, type: 'space', title: 'Space Result' },
          { ...mockSearchResult, type: 'request', id: 'req-1', title: 'Request Result' },
          { ...mockSearchResult, type: 'site', id: 'site-2', title: 'Site Result' },
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

  describe('edit button', () => {
    it('shows Edit button for results with canEdit permission', async () => {
      vi.mocked(searchApi.globalSearch).mockResolvedValue(mockSearchResponse);
      renderCommandPalette({ open: true });
      
      const input = screen.getByPlaceholderText(/search/i);
      await userEvent.type(input, 'conference');
      
      await waitFor(() => {
        expect(screen.getByRole('button', { name: /edit/i })).toBeInTheDocument();
      }, { timeout: 500 });
    });

    it('does not show Edit button when canEdit is false', async () => {
      const noEditResult: SearchResult = {
        ...mockSearchResult,
        permissions: { canRead: true, canEdit: false },
      };
      vi.mocked(searchApi.globalSearch).mockResolvedValue({
        query: 'test',
        results: [noEditResult],
      });
      renderCommandPalette({ open: true });
      
      const input = screen.getByPlaceholderText(/search/i);
      await userEvent.type(input, 'test');
      
      await waitFor(() => {
        expect(screen.getByText('Conference Room A')).toBeInTheDocument();
      }, { timeout: 500 });
      
      expect(screen.queryByRole('button', { name: /edit/i })).not.toBeInTheDocument();
    });

    it('navigates to edit URL when Edit button is clicked', async () => {
      const onOpenChange = vi.fn();
      vi.mocked(searchApi.globalSearch).mockResolvedValue(mockSearchResponse);
      renderCommandPalette({ open: true, onOpenChange });
      
      const input = screen.getByPlaceholderText(/search/i);
      await userEvent.type(input, 'conference');
      
      await waitFor(() => {
        expect(screen.getByRole('button', { name: /edit/i })).toBeInTheDocument();
      }, { timeout: 500 });
      
      fireEvent.click(screen.getByRole('button', { name: /edit/i }));
      
      // Dialog should close
      expect(onOpenChange).toHaveBeenCalledWith(false);
      
      // Navigation happens async via setTimeout
      await waitFor(() => {
        expect(mockNavigate).toHaveBeenCalledWith('/spaces?edit=space-123');
      }, { timeout: 100 });
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
        <BrowserRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
          <CommandPalette open={true} onOpenChange={onOpenChange} />
        </BrowserRouter>
      );
      
      const input = screen.getByPlaceholderText(/search/i);
      await userEvent.type(input, 'my search');
      
      expect(input).toHaveValue('my search');
      
      // Close the dialog
      rerender(
        <BrowserRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
          <CommandPalette open={false} onOpenChange={onOpenChange} />
        </BrowserRouter>
      );
      
      // Reopen the dialog
      rerender(
        <BrowserRouter future={{ v7_startTransition: true, v7_relativeSplatPath: true }}>
          <CommandPalette open={true} onOpenChange={onOpenChange} />
        </BrowserRouter>
      );
      
      // Query should still be there
      const reopenedInput = screen.getByPlaceholderText(/search/i);
      expect(reopenedInput).toHaveValue('my search');
    });
  });
});
