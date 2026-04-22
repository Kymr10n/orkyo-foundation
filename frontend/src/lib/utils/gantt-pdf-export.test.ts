/* eslint-disable @typescript-eslint/no-explicit-any */
/**
 * Tests for PDF Export functionality
 */

import { describe, it, expect, vi, beforeEach } from 'vitest';
import { exportGanttChartToPDF } from './gantt-pdf-export';
import type { Request } from '@/types/requests';
import type { Space } from '@/types/space';

// Create mock PDF instance
const mockPDFInstance = {
  internal: {
    pageSize: {
      getWidth: () => 297,
      getHeight: () => 210,
    },
  },
  setFontSize: vi.fn().mockReturnThis(),
  setFont: vi.fn().mockReturnThis(),
  text: vi.fn().mockReturnThis(),
  setDrawColor: vi.fn().mockReturnThis(),
  line: vi.fn().mockReturnThis(),
  setTextColor: vi.fn().mockReturnThis(),
  setFillColor: vi.fn().mockReturnThis(),
  roundedRect: vi.fn().mockReturnThis(),
  save: vi.fn().mockReturnThis(),
};

// Mock jsPDF constructor
vi.mock('jspdf', () => {
  const mockConstructor = vi.fn(function(this: any) {
    return mockPDFInstance;
  });
  return {
    default: mockConstructor,
  };
});

describe('gantt-pdf-export', () => {
  const mockSpaces: Space[] = [
    {
      id: 'space-1',
      name: 'Conference Room A',
      code: 'CR-A',
      siteId: 'site-1',
      isPhysical: true,
      capacity: 1,
      createdAt: '2024-01-01',
      updatedAt: '2024-01-01',
    },
    {
      id: 'space-2',
      name: 'Conference Room B',
      code: 'CR-B',
      siteId: 'site-1',
      isPhysical: true,
      capacity: 1,
      createdAt: '2024-01-01',
      updatedAt: '2024-01-01',
    },
  ];

  const mockRequests: Request[] = [
    {
      id: 'req-1',
      name: 'Meeting 1',
      description: 'Test meeting',
      spaceId: 'space-1',
      startTs: '2024-03-01T10:00:00Z',
      endTs: '2024-03-01T11:00:00Z',
      status: 'planned',
      minimalDurationValue: 1,
      minimalDurationUnit: 'hours',
      schedulingSettingsApply: true,
      planningMode: "leaf",
      sortOrder: 0,
      createdAt: '2024-01-01',
      updatedAt: '2024-01-01',
    },
    {
      id: 'req-2',
      name: 'Meeting 2',
      description: 'Another meeting',
      spaceId: 'space-2',
      startTs: '2024-03-02T14:00:00Z',
      endTs: '2024-03-02T15:00:00Z',
      status: 'in_progress',
      minimalDurationValue: 1,
      minimalDurationUnit: 'hours',
      schedulingSettingsApply: true,
      planningMode: "leaf",
      sortOrder: 0,
      createdAt: '2024-01-01',
      updatedAt: '2024-01-01',
    },
    {
      id: 'req-3',
      name: 'Unscheduled',
      description: 'Not scheduled yet',
      spaceId: null,
      startTs: null,
      endTs: null,
      status: 'planned',
      minimalDurationValue: 2,
      minimalDurationUnit: 'hours',
      schedulingSettingsApply: true,
      planningMode: "leaf",
      sortOrder: 0,
      createdAt: '2024-01-01',
      updatedAt: '2024-01-01',
    },
  ];

  const startDate = new Date('2024-03-01');
  const endDate = new Date('2024-03-31');

  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('should generate PDF without errors', () => {
    expect(() => {
      exportGanttChartToPDF({
        requests: mockRequests,
        spaces: mockSpaces,
        startDate,
        endDate,
      });
    }).not.toThrow();
  });

  it('should filter out unscheduled requests', () => {
    exportGanttChartToPDF({
      requests: mockRequests,
      spaces: mockSpaces,
      startDate,
      endDate,
    });

    // Should only process 2 scheduled requests (req-1 and req-2)
    // roundedRect is called for bars + legend items
    expect(mockPDFInstance.roundedRect).toHaveBeenCalled();
    const barCalls = mockPDFInstance.roundedRect.mock.calls.filter(
      (call: any[]) => call[4] === 1 // bars have radius 1
    );
    expect(barCalls.length).toBe(2); // 2 scheduled requests
  });

  it('should use custom filename when provided', () => {
    const customFilename = 'custom-gantt.pdf';

    exportGanttChartToPDF({
      requests: mockRequests,
      spaces: mockSpaces,
      startDate,
      endDate,
      filename: customFilename,
    });

    expect(mockPDFInstance.save).toHaveBeenCalledWith(customFilename);
  });

  it('should generate default filename when not provided', () => {
    exportGanttChartToPDF({
      requests: mockRequests,
      spaces: mockSpaces,
      startDate,
      endDate,
    });

    expect(mockPDFInstance.save).toHaveBeenCalled();
    const savedFilename = mockPDFInstance.save.mock.calls[0][0];
    expect(savedFilename).toMatch(/^gantt-chart-\d{4}-\d{2}-\d{2}\.pdf$/);
  });

  it('should handle empty request list', () => {
    expect(() => {
      exportGanttChartToPDF({
        requests: [],
        spaces: mockSpaces,
        startDate,
        endDate,
      });
    }).not.toThrow();
  });

  it('should handle empty spaces list', () => {
    expect(() => {
      exportGanttChartToPDF({
        requests: mockRequests,
        spaces: [],
        startDate,
        endDate,
      });
    }).not.toThrow();
  });

  it('should render header with date range', () => {
    exportGanttChartToPDF({
      requests: mockRequests,
      spaces: mockSpaces,
      startDate,
      endDate,
    });

    // Check that header text is rendered
    expect(mockPDFInstance.text).toHaveBeenCalledWith(
      'Space Utilization Gantt Chart',
      expect.any(Number),
      expect.any(Number)
    );

    // Check that date range is rendered
    const textCalls = mockPDFInstance.text.mock.calls.map((call: any) => call[0]);
    expect(textCalls.some((text: string) => text.includes('Mar 1, 2024'))).toBe(true);
  });

  it('should render legend with status colors', () => {
    exportGanttChartToPDF({
      requests: mockRequests,
      spaces: mockSpaces,
      startDate,
      endDate,
    });

    // Check legend text is rendered
    const textCalls = mockPDFInstance.text.mock.calls.map((call: any) => call[0]);
    expect(textCalls).toContain('Status Legend');
    expect(textCalls).toContain('Planned');
    expect(textCalls).toContain('In Progress');
    expect(textCalls).toContain('Done');
  });

  it('should render statistics section', () => {
    exportGanttChartToPDF({
      requests: mockRequests,
      spaces: mockSpaces,
      startDate,
      endDate,
    });

    const textCalls = mockPDFInstance.text.mock.calls.map((call: any) => call[0]);
    expect(textCalls).toContain('Statistics');
    expect(textCalls.some((text: string) => text.includes('Total Requests'))).toBe(true);
    expect(textCalls.some((text: string) => text.includes('Spaces Used'))).toBe(true);
  });

  it('should apply correct colors for different request statuses', () => {
    exportGanttChartToPDF({
      requests: mockRequests,
      spaces: mockSpaces,
      startDate,
      endDate,
    });

    // Check that setFillColor was called with different colors
    expect(mockPDFInstance.setFillColor).toHaveBeenCalledWith(59, 130, 246); // blue for planned
    expect(mockPDFInstance.setFillColor).toHaveBeenCalledWith(249, 115, 22); // orange for in_progress
  });

  it('should group requests by space', () => {
    exportGanttChartToPDF({
      requests: mockRequests,
      spaces: mockSpaces,
      startDate,
      endDate,
    });

    // Should render space names
    const textCalls = mockPDFInstance.text.mock.calls.map((call: any) => call[0]);
    expect(textCalls).toContain('Conference Room A');
    expect(textCalls).toContain('Conference Room B');
  });
});
