/* eslint-disable @typescript-eslint/no-explicit-any */
import { describe, it, expect, vi, beforeEach } from 'vitest';
import {
  exportResources,
  importResources,
  type ResourceExportRow,
  exportRequests,
  importRequests,
  exportConflicts,
  exportCriteria,
  importCriteria,
  exportSites,
  importSites,
  exportTemplates,
  importTemplates,
  exportUsers,
  importUsers,
  exportUtilization,
} from './export-handlers';
import { getResources } from '@foundation/src/lib/api/resources-api';
import { exportGanttChartToPDF } from './gantt-pdf-export';
import type { Request, Conflict } from '@foundation/src/types/requests';
import type { Criterion } from '@foundation/src/types/criterion';
import type { Site } from '@foundation/src/types/site';
import { spaceAssignment } from '@foundation/src/test-utils/request-fixtures';

// Placement is resolved against the placeable type set now, not the literal 'space' key.
const PLACEABLE_KEYS: ReadonlySet<string> = new Set(['space']);

// Mock downloadFile
let mockDownloadFile = vi.fn();

// Mock the import-export module
vi.mock('./import-export', async () => {
  const actual = await vi.importActual('./import-export');
  return {
    ...actual,
    downloadFile: (...args: any[]) => mockDownloadFile(...args),
  };
});

// Mock gantt-pdf-export
vi.mock('./gantt-pdf-export', () => ({
  exportGanttChartToPDF: vi.fn(),
}));

// Mock the resources API — exportUtilization fetches row labels from it.
vi.mock('@foundation/src/lib/api/resources-api', () => ({
  getResources: vi.fn(),
}));

// Helper to create a mock File with text() method
function createMockFile(content: string, filename: string, type: string): File {
  const file = new File([content], filename, { type });
  // Add text() method for Node.js environment
  (file as any).text = () => Promise.resolve(content);
  return file;
}

describe('Export Handlers', () => {
  beforeEach(() => {
    mockDownloadFile = vi.fn();
  });

  describe('placement columns on the generic transfer', () => {
    // Placement used to have its own spaces-only exporter. It rides the generic one now, so
    // these pin what that exporter must keep doing — including the round-trip, which the old
    // spaces path never had (its JSON export was a no-op).
    const placeable: ResourceExportRow = {
      id: '1',
      resourceTypeId: 'type-space',
      resourceTypeKey: 'space',
      name: 'Room A',
      allocationMode: 'Exclusive',
      baseAvailabilityPercent: 100,
      isActive: true,
      homeSiteId: 'site1',
      crossSiteAllowed: false,
      code: 'RM-A',
      isPhysical: true,
      capacity: 4,
      geometry: {
        type: 'rectangle',
        coordinates: [{ x: 10, y: 20 }, { x: 30, y: 40 }],
      },
      createdAt: '2024-01-01T00:00:00Z',
      updatedAt: '2024-01-01T00:00:00Z',
    };

    it('writes a shape as two legible columns, not a blob', async () => {
      await exportResources([placeable], 'csv', 'space');

      const [content] = mockDownloadFile.mock.calls[0];
      expect(content).toContain('geometry_type');
      expect(content).toContain('rectangle');
      expect(content).toContain('coordinates');
      // The failure this guards: an object dropped into a CSV cell verbatim.
      expect(content).not.toContain('[object Object]');
    });

    it('leaves placement columns out for a resource that has none', async () => {
      const person: ResourceExportRow = {
        ...placeable,
        resourceTypeKey: 'person',
        isPhysical: false,
        geometry: undefined,
        code: null,
      };
      await exportResources([person], 'csv', 'person');

      const [content] = mockDownloadFile.mock.calls[0];
      expect(content).not.toContain('geometry_type');
      expect(content).not.toContain('coordinates');
    });

    it('round-trips a drawn shape through export and import', async () => {
      await exportResources([placeable], 'csv', 'space');
      const [content] = mockDownloadFile.mock.calls[0];
      const file = createMockFile(content as string, 'stations.csv', 'text/csv');

      const rows = await importResources(file, 'csv', 'space');

      expect(rows).toHaveLength(1);
      expect(rows[0].request).toMatchObject({
        resourceTypeKey: 'space',
        name: 'Room A',
        code: 'RM-A',
        isPhysical: true,
        capacity: 4,
        geometry: { type: 'rectangle', coordinates: [{ x: 10, y: 20 }, { x: 30, y: 40 }] },
      });
    });

    it('drops a shape it cannot parse rather than guessing at one', async () => {
      // A resource placed at coordinates nobody drew is worse than one the server rejects by
      // name for being physical with no geometry.
      const csv = 'name,is_physical,geometry_type,coordinates\nBroken,true,rectangle,"not json"';
      const file = createMockFile(csv, 'stations.csv', 'text/csv');

      const rows = await importResources(file, 'csv', 'space');

      expect(rows[0].request.geometry).toBeUndefined();
      expect(rows[0].request.isPhysical).toBe(true);
    });
  });

  describe('exportRequests', () => {
    const mockRequests: Request[] = [
      {
        id: '1',
        name: 'Request 1',
        assignments: [spaceAssignment('space1')],
        minimalDurationValue: 60,
        minimalDurationUnit: 'minutes',
        status: 'new',
        schedulingSettingsApply: true,
        planningMode: "leaf",
        sortOrder: 0,
        createdAt: '2026-01-01T00:00:00Z',
        updatedAt: '2026-01-01T00:00:00Z',
      },
      {
        id: '2',
        name: 'Request 2',
        assignments: [spaceAssignment('space2')],
        minimalDurationValue: 120,
        minimalDurationUnit: 'minutes',
        status: 'done',
        schedulingSettingsApply: true,
        planningMode: "leaf",
        sortOrder: 0,
        createdAt: '2026-02-01T00:00:00Z',
        updatedAt: '2026-02-01T00:00:00Z',
      },
    ];

    it('should export requests as CSV', async () => {
      await exportRequests(mockRequests, 'csv', PLACEABLE_KEYS);

      expect(mockDownloadFile).toHaveBeenCalledTimes(1);
      const [content, filename] = mockDownloadFile.mock.calls[0];
      expect(content).toContain('new');
      expect(content).toContain('done');
      expect(filename).toMatch(/requests-.*\.csv$/);
    });

    it('should export requests as JSON', async () => {
      // JSON export not implemented for requests
      await exportRequests(mockRequests, 'json', PLACEABLE_KEYS);

      expect(mockDownloadFile).toHaveBeenCalledTimes(0);
    });
  });

  describe('importRequests', () => {
    it('should import requests from CSV as a camelCase create payload', async () => {
      const csvContent = 'id,name,resource_id,start_ts,end_ts,status\n1,Request 1,resource1,2026-01-01,2026-01-31,in_progress';
      const file = createMockFile(csvContent, 'requests.csv', 'text/csv');

      const result = await importRequests(file, 'csv');

      expect(result).toHaveLength(1);
      expect(result[0]).toMatchObject({
        name: 'Request 1',
        status: 'in_progress',
        startTs: '2026-01-01',
        endTs: '2026-01-31',
        resourceIds: ['resource1'],
      });
    });

    it('drops a status that is not a real RequestStatus', async () => {
      // The importer used to cast whatever the column held straight to
      // RequestStatus; "pending" is not one, and the backend silently ignored it.
      const csvContent = 'name,status\nRequest 1,pending';
      const file = createMockFile(csvContent, 'requests.csv', 'text/csv');

      const result = await importRequests(file, 'csv');

      expect(result[0].status).toBeUndefined();
    });

    it('should import requests from JSON', async () => {
      // JSON import not implemented - returns empty array
      const jsonContent = JSON.stringify({
        data: [
          { id: '1', title: 'Request 1', name: 'Request 1', resourceId: 'space1', startDate: '2026-01-01', endDate: '2026-01-31' },
        ]
      });
      const file = createMockFile(jsonContent, 'requests.json', 'application/json');

      const result = await importRequests(file, 'json');

      expect(result).toEqual([]);
    });
  });

  describe('exportConflicts', () => {
    const mockConflicts: Conflict[] = [
      {
        id: '1',
        kind: 'overlap',
        severity: 'error',
        message: 'Conflict detected',
      },
    ];

    it('should export conflicts as CSV', async () => {
      await exportConflicts(mockConflicts, 'csv');

      expect(mockDownloadFile).toHaveBeenCalledTimes(1);
      const [content, filename] = mockDownloadFile.mock.calls[0];
      expect(content).toContain('overlap');
      expect(content).toContain('Conflict detected');
      expect(filename).toMatch(/conflicts-.*\.csv$/);
    });
  });

  describe('exportCriteria', () => {
    const mockCriteria: Criterion[] = [
      {
        id: '1',
        name: 'Duration',
        dataType: 'Number',
        description: 'Request duration',
        resourceTypeKeys: ['space'],
      createdAt: '2024-01-01T00:00:00Z',
        updatedAt: '2024-01-01T00:00:00Z',
      },
    ];

    it('should export criteria as CSV', async () => {
      await exportCriteria(mockCriteria, 'csv');

      expect(mockDownloadFile).toHaveBeenCalledTimes(1);
      const [content, filename] = mockDownloadFile.mock.calls[0];
      expect(content).toContain('Duration');
      expect(filename).toMatch(/criteria-.*\.csv$/);
    });

    it('should export criteria as JSON', async () => {
      await exportCriteria(mockCriteria, 'json');

      expect(mockDownloadFile).toHaveBeenCalledTimes(1);
      const [content, filename] = mockDownloadFile.mock.calls[0];
      const parsed = JSON.parse(content);
      expect(parsed.context).toBe('criteria');
      expect(parsed.data[0].name).toBe('Duration');
      expect(filename).toMatch(/criteria-.*\.json$/);
    });
  });

  describe('importCriteria', () => {
    it('should import criteria from CSV', async () => {
      const csvContent = 'id,name,type,description\n1,Duration,range,Request duration';
      const file = createMockFile(csvContent, 'criteria.csv', 'text/csv');

      const result = await importCriteria(file, 'csv');

      expect(result).toHaveLength(1);
      expect(result[0]).toMatchObject({
        name: 'Duration',
        description: 'Request duration',
      });
    });

    it('should import criteria from JSON', async () => {
      const jsonContent = JSON.stringify({
        data: [
          { id: '1', name: 'Duration', type: 'range', description: 'Request duration' },
        ]
      });
      const file = createMockFile(jsonContent, 'criteria.json', 'application/json');

      const result = await importCriteria(file, 'json');

      expect(result).toHaveLength(1);
      expect(result[0].name).toBe('Duration');
    });
  });

  describe('exportSites', () => {
    const mockSites: Site[] = [
      {
        id: '1',
        code: 'MAIN',
        name: 'Main Campus',
        address: '123 Main St',
        createdAt: '2024-01-01T00:00:00Z',
        updatedAt: '2024-01-01T00:00:00Z',
      },
    ];

    it('should export sites as CSV', async () => {
      await exportSites(mockSites, 'csv');

      expect(mockDownloadFile).toHaveBeenCalledTimes(1);
      const [content, filename] = mockDownloadFile.mock.calls[0];
      expect(content).toContain('Main Campus');
      expect(filename).toMatch(/sites-.*\.csv$/);
    });

    it('should export sites as JSON', async () => {
      await exportSites(mockSites, 'json');

      expect(mockDownloadFile).toHaveBeenCalledTimes(1);
      const [content, filename] = mockDownloadFile.mock.calls[0];
      const parsed = JSON.parse(content);
      expect(parsed.context).toBe('sites');
      expect(parsed.data[0].name).toBe('Main Campus');
      expect(filename).toMatch(/sites-.*\.json$/);
    });
  });

  describe('importSites', () => {
    it('should import sites from CSV', async () => {
      const csvContent = 'id,name,location\n1,Main Campus,123 Main St';
      const file = createMockFile(csvContent, 'sites.csv', 'text/csv');

      const result = await importSites(file, 'csv');

      expect(result).toHaveLength(1);
      expect(result[0]).toMatchObject({
        name: 'Main Campus',
        location: '123 Main St',
      });
    });

    it('should import sites from JSON', async () => {
      const jsonContent = JSON.stringify({
        data: [
          { id: '1', name: 'Main Campus', location: '123 Main St' },
        ]
      });
      const file = createMockFile(jsonContent, 'sites.json', 'application/json');

      const result = await importSites(file, 'json');

      expect(result).toHaveLength(1);
      expect(result[0].name).toBe('Main Campus');
    });
  });

  describe('exportTemplates', () => {
    const mockTemplates = [
      {
        id: '1',
        name: 'Conference Room Booking',
        description: 'Template for conference room bookings',
        entityType: 'request' as const,
        requirements: [],
      },
    ];

    it('should export templates as CSV', async () => {
      await exportTemplates(mockTemplates, 'csv');

      expect(mockDownloadFile).toHaveBeenCalledTimes(1);
      const [content, filename] = mockDownloadFile.mock.calls[0];
      expect(content).toContain('Conference Room Booking');
      expect(filename).toMatch(/templates-.*\.csv$/);
    });

    it('should export templates as JSON', async () => {
      await exportTemplates(mockTemplates, 'json');

      expect(mockDownloadFile).toHaveBeenCalledTimes(1);
      const [content, filename] = mockDownloadFile.mock.calls[0];
      const parsed = JSON.parse(content);
      expect(parsed.context).toBe('templates');
      expect(parsed.data[0].name).toBe('Conference Room Booking');
      expect(filename).toMatch(/templates-.*\.json$/);
    });
  });

  describe('importTemplates', () => {
    it('should import templates from CSV', async () => {
      const csvContent = 'id,name,description\n1,Conference Room Booking,Template for conference room bookings';
      const file = createMockFile(csvContent, 'templates.csv', 'text/csv');

      const result = await importTemplates(file, 'csv');

      expect(result).toHaveLength(1);
      expect(result[0]).toMatchObject({
        name: 'Conference Room Booking',
        description: 'Template for conference room bookings',
      });
    });

    it('should import templates from JSON', async () => {
      const jsonContent = JSON.stringify({
        data: [
          { id: '1', name: 'Conference Room Booking', description: 'Template for conference room bookings' },
        ]
      });
      const file = createMockFile(jsonContent, 'templates.json', 'application/json');

      const result = await importTemplates(file, 'json');

      expect(result).toHaveLength(1);
      expect(result[0].name).toBe('Conference Room Booking');
    });
  });

  describe('exportUsers', () => {
    const mockUsers = [
      {
        id: '1',
        email: 'user@example.com',
        role: 'admin' as const,
        displayName: 'John Doe',
        status: 'active' as const,
      },
    ];

    it('should export users as CSV', async () => {
      await exportUsers(mockUsers, 'csv');

      expect(mockDownloadFile).toHaveBeenCalledTimes(1);
      const [content, filename] = mockDownloadFile.mock.calls[0];
      expect(content).toContain('user@example.com');
      expect(filename).toMatch(/users-.*\.csv$/);
    });

    it('should export users as JSON', async () => {
      await exportUsers(mockUsers, 'json');

      expect(mockDownloadFile).toHaveBeenCalledTimes(1);
      const [content, filename] = mockDownloadFile.mock.calls[0];
      const parsed = JSON.parse(content);
      expect(parsed.context).toBe('users');
      expect(parsed.data[0].email).toBe('user@example.com');
      expect(filename).toMatch(/users-.*\.json$/);
    });
  });

  describe('importUsers', () => {
    it('should import users from CSV', async () => {
      const csvContent = 'id,email,role,displayName\n1,user@example.com,admin,John Doe';
      const file = createMockFile(csvContent, 'users.csv', 'text/csv');

      const result = await importUsers(file, 'csv');

      expect(result).toHaveLength(1);
      expect(result[0]).toMatchObject({
        email: 'user@example.com',
        role: 'admin',
      });
    });

    it('should import users from JSON', async () => {
      const jsonContent = JSON.stringify({
        data: [
          { id: '1', email: 'user@example.com', role: 'admin', displayName: 'John Doe' },
        ]
      });
      const file = createMockFile(jsonContent, 'users.json', 'application/json');

      const result = await importUsers(file, 'json');

      expect(result).toHaveLength(1);
      expect(result[0].email).toBe('user@example.com');
    });
  });

  describe('Error Handling', () => {
    it('should handle empty files gracefully', async () => {
      const file = createMockFile('', 'empty.csv', 'text/csv');

      const result = await importResources(file, 'csv', 'space');

      expect(result).toEqual([]);
    });

    it('reads an unparseable boolean as not-physical rather than failing the row', async () => {
      // The row still names a resource, so it is offered to the server; only the flag is lost.
      const csvContent = 'name,is_physical\nRoom A,invalid';
      const file = createMockFile(csvContent, 'stations.csv', 'text/csv');

      const result = await importResources(file, 'csv', 'space');

      expect(result).toHaveLength(1);
      expect(result[0].request.name).toBe('Room A');
      expect(result[0].request.isPhysical).toBe(false);
    });

    it('should handle malformed JSON gracefully', async () => {
      const file = createMockFile('{ invalid json }', 'stations.json', 'application/json');

      await expect(importResources(file, 'json', 'space')).rejects.toThrow();
    });
  });

  describe('exportUtilization', () => {
    beforeEach(() => {
      vi.mocked(getResources).mockReset();
      vi.mocked(exportGanttChartToPDF).mockClear();
    });

    const page = (ids: number[], total: number) => ({
      data: ids.map((i) => ({ id: `res-${i}`, name: `Resource ${i}`, resourceTypeKey: 'space' })),
      total,
      page: 1,
      pageSize: 100,
    });

    const spaceType = { key: 'space', displayNamePlural: 'Spaces' } as any;

    it('pages through every resource so no row label is missing', async () => {
      // The server caps pageSize at 100; a single default call used to leave
      // everything past the first page labelled "Unknown resource".
      vi.mocked(getResources)
        .mockResolvedValueOnce(page(Array.from({ length: 100 }, (_, i) => i), 150) as any)
        .mockResolvedValueOnce(page(Array.from({ length: 50 }, (_, i) => i + 100), 150) as any);

      await exportUtilization([], new Date('2024-03-01'), new Date('2024-03-31'), [spaceType]);

      expect(getResources).toHaveBeenCalledTimes(2);
      expect(getResources).toHaveBeenNthCalledWith(1, { isActive: true, page: 1, pageSize: 100 });
      expect(getResources).toHaveBeenNthCalledWith(2, { isActive: true, page: 2, pageSize: 100 });

      const { resources } = vi.mocked(exportGanttChartToPDF).mock.calls[0][0];
      expect(resources.size).toBe(150);
      expect(resources.get('res-0')).toEqual({ name: 'Resource 0', typeKey: 'space' });
      expect(resources.get('res-149')).toEqual({ name: 'Resource 149', typeKey: 'space' });
    });

    it('forwards the requested types so the chart can section by them', async () => {
      const personType = { key: 'person', displayNamePlural: 'People' } as any;
      vi.mocked(getResources).mockResolvedValueOnce(page([0], 1) as any);

      await exportUtilization([], new Date('2024-03-01'), new Date('2024-03-31'), [
        spaceType,
        personType,
      ]);

      const { resourceTypes } = vi.mocked(exportGanttChartToPDF).mock.calls[0][0];
      expect(resourceTypes).toEqual([spaceType, personType]);
    });

    it('stops after one call when everything fits on a page', async () => {
      vi.mocked(getResources).mockResolvedValueOnce(page([0, 1], 2) as any);

      await exportUtilization([], new Date('2024-03-01'), new Date('2024-03-31'), [spaceType]);

      expect(getResources).toHaveBeenCalledTimes(1);
    });
  });
});
