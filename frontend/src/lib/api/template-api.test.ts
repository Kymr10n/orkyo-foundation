import { describe, it, expect, vi, beforeEach } from 'vitest';
import {
  getTemplates,
  createTemplate,
  updateTemplate,
  deleteTemplate,
} from './template-api';
import * as apiClient from '../core/api-client';
import { API_PATHS } from '../core/api-paths';

vi.mock('../core/api-client');

const mockTemplate = {
  id: 'tmpl-123',
  name: 'Standard Request',
  entityType: 'request' as const,
  fields: [
    { key: 'title', label: 'Title', type: 'text', required: true },
    { key: 'description', label: 'Description', type: 'textarea', required: false },
  ],
  createdAt: '2024-01-01T00:00:00Z',
  updatedAt: '2024-01-01T00:00:00Z',
};

describe('template-api', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe('getTemplates', () => {
    it('calls apiGet with correct endpoint for request templates', async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue([mockTemplate]);

      const result = await getTemplates('request');

      expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.templatesWithType('request'));
      expect(result).toEqual([mockTemplate]);
    });

    it('calls apiGet with correct endpoint for space templates', async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue([]);

      await getTemplates('space');

      expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.templatesWithType('space'));
    });

    it('calls apiGet with correct endpoint for group templates', async () => {
      vi.mocked(apiClient.apiGet).mockResolvedValue([]);

      await getTemplates('group');

      expect(apiClient.apiGet).toHaveBeenCalledWith(API_PATHS.templatesWithType('group'));
    });
  });

  describe('createTemplate', () => {
    it('calls apiPost with correct endpoint and data', async () => {
      const createRequest = {
        name: 'Standard Request',
        entityType: 'request' as const,
        fields: [{ key: 'title', label: 'Title', type: 'text', required: true }],
      };
      vi.mocked(apiClient.apiPost).mockResolvedValue(mockTemplate);

      const result = await createTemplate(createRequest);

      expect(apiClient.apiPost).toHaveBeenCalledWith(API_PATHS.TEMPLATES, createRequest);
      expect(result).toEqual(mockTemplate);
    });
  });

  describe('updateTemplate', () => {
    it('calls apiPut with correct endpoint and data', async () => {
      const updateRequest = { name: 'Updated Template Name' };
      const updatedTemplate = { ...mockTemplate, name: 'Updated Template Name' };
      vi.mocked(apiClient.apiPut).mockResolvedValue(updatedTemplate);

      const result = await updateTemplate('tmpl-123', updateRequest);

      expect(apiClient.apiPut).toHaveBeenCalledWith(
        API_PATHS.template('tmpl-123'),
        updateRequest
      );
      expect(result).toEqual(updatedTemplate);
    });

    it('handles partial updates', async () => {
      const partialUpdate = { name: 'New Name' };
      vi.mocked(apiClient.apiPut).mockResolvedValue({ ...mockTemplate, name: 'New Name' });

      await updateTemplate('tmpl-123', partialUpdate);

      expect(apiClient.apiPut).toHaveBeenCalledWith(
        API_PATHS.template('tmpl-123'),
        partialUpdate
      );
    });
  });

  describe('deleteTemplate', () => {
    it('calls apiDelete with correct endpoint', async () => {
      vi.mocked(apiClient.apiDelete).mockResolvedValue(undefined);

      await deleteTemplate('tmpl-123');

      expect(apiClient.apiDelete).toHaveBeenCalledWith(API_PATHS.template('tmpl-123'));
    });
  });
});
