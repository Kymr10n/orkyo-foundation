/** @jsxImportSource react */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { globalSearch, type SearchResponse, type SearchResult } from './search-api';
import * as apiClient from '../core/api-client';
import { API_PATHS } from '../core/api-paths';

vi.mock('../core/api-client');

const mockSearchResult: SearchResult = {
  type: 'space',
  id: 'space-123',
  title: 'Conference Room A',
  subtitle: 'Main Building - Floor 2',
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

describe('search-api', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe('globalSearch', () => {
    it('calls apiGet with correct endpoint and query param', async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue(mockSearchResponse);

      const result = await globalSearch({ query: 'conference' });

      expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.SEARCH, {
        params: { q: 'conference' },
      });
      expect(result).toEqual(mockSearchResponse);
    });

    it('includes siteId when provided', async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue(mockSearchResponse);

      await globalSearch({ query: 'test', siteId: 'site-123' });

      expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.SEARCH, {
        params: { q: 'test', siteId: 'site-123' },
      });
    });

    it('includes types filter when provided', async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue(mockSearchResponse);

      await globalSearch({ query: 'test', types: ['space', 'request'] });

      expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.SEARCH, {
        params: { q: 'test', types: 'space,request' },
      });
    });

    it('includes limit when provided', async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue(mockSearchResponse);

      await globalSearch({ query: 'test', limit: 10 });

      expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.SEARCH, {
        params: { q: 'test', limit: '10' },
      });
    });

    it('includes all params when all provided', async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue(mockSearchResponse);

      await globalSearch({
        query: 'conference',
        siteId: 'site-1',
        types: ['space', 'group'],
        limit: 5,
      });

      expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.SEARCH, {
        params: {
          q: 'conference',
          siteId: 'site-1',
          types: 'space,group',
          limit: '5',
        },
      });
    });

    it('handles empty results', async () => {
      const emptyResponse: SearchResponse = { query: 'nonexistent', results: [] };
      vi.mocked(apiClient.apiGet).mockResolvedValue(emptyResponse);

      const result = await globalSearch({ query: 'nonexistent' });

      expect(result.results).toHaveLength(0);
    });

    it('propagates API errors', async () => {
      const error = new Error('Network error');
      vi.mocked(apiClient.apiGet).mockRejectedValue(error);

      await expect(globalSearch({ query: 'test' })).rejects.toThrow('Network error');
    });
  });

  describe('SearchResult types', () => {
    it('correctly types all entity types', () => {
      const types: SearchResult['type'][] = [
        'space',
        'request',
        'group',
        'site',
        'template',
        'criterion',
      ];
      
      types.forEach((type) => {
        const result: SearchResult = { ...mockSearchResult, type };
        expect(result.type).toBe(type);
      });
    });
  });
});
