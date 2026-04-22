/* eslint-disable @typescript-eslint/no-explicit-any */
import { describe, it, expect, vi } from 'vitest';
import { 
  arrayToCSV, 
  csvToArray, 
  getExportFilename,
  getSupportedFormats,
  isImportSupported,
  downloadFile,
} from './import-export';

describe('Import/Export Utilities', () => {
  describe('arrayToCSV', () => {
    it('should convert simple array to CSV', () => {
      const data = [
        { name: 'Room A', capacity: 10 },
        { name: 'Room B', capacity: 20 }
      ];
      
      const csv = arrayToCSV(data);
      expect(csv).toBe('name,capacity\nRoom A,10\nRoom B,20');
    });

    it('should handle strings with commas', () => {
      const data = [{ name: 'Room A, Floor 1', size: 100 }];
      const csv = arrayToCSV(data);
      expect(csv).toContain('"Room A, Floor 1"');
    });

    it('should handle strings with quotes', () => {
      const data = [{ name: 'Room "A"', size: 100 }];
      const csv = arrayToCSV(data);
      expect(csv).toContain('"Room ""A"""');
    });

    it('should handle null and undefined values', () => {
      const data = [{ name: 'Room A', capacity: null, notes: undefined }];
      const csv = arrayToCSV(data);
      expect(csv).toBe('name,capacity,notes\nRoom A,,');
    });

    it('should handle arrays and objects as JSON', () => {
      const data = [{ name: 'Room A', tags: ['tag1', 'tag2'] }];
      const csv = arrayToCSV(data);
      // Arrays are JSON stringified and quoted with escaped inner quotes
      expect(csv).toContain('[""tag1"",""tag2""]');
    });

    it('should return empty string for empty array', () => {
      expect(arrayToCSV([])).toBe('');
    });

    it('should use custom headers if provided', () => {
      const data = [{ name: 'A', size: 10, unused: 'x' }];
      const csv = arrayToCSV(data, ['name', 'size']);
      expect(csv).toBe('name,size\nA,10');
    });
  });

  describe('csvToArray', () => {
    it('should parse simple CSV', () => {
      const csv = 'name,capacity\nRoom A,10\nRoom B,20';
      const result = csvToArray(csv);
      
      expect(result).toEqual([
        { name: 'Room A', capacity: '10' },
        { name: 'Room B', capacity: '20' }
      ]);
    });

    it('should handle quoted values with commas', () => {
      const csv = 'name,size\n"Room A, Floor 1",100';
      const result = csvToArray(csv);
      
      expect(result[0].name).toBe('Room A, Floor 1');
    });

    it('should handle escaped quotes', () => {
      const csv = 'name,size\n"Room ""A""",100';
      const result = csvToArray(csv);
      
      expect(result[0].name).toBe('Room "A"');
    });

    it('should handle empty values', () => {
      const csv = 'name,capacity,notes\nRoom A,,';
      const result = csvToArray(csv);
      
      expect(result[0]).toEqual({ name: 'Room A', capacity: '', notes: '' });
    });

    it('should use custom headers if provided', () => {
      const csv = 'Room A,10\nRoom B,20';
      const result = csvToArray(csv, ['name', 'capacity']);
      
      expect(result).toEqual([
        { name: 'Room A', capacity: '10' },
        { name: 'Room B', capacity: '20' }
      ]);
    });

    it('should return empty array for empty string', () => {
      expect(csvToArray('')).toEqual([]);
    });
  });

  describe('getExportFilename', () => {
    it('should generate filename without site', () => {
      const filename = getExportFilename('spaces', 'csv');
      expect(filename).toMatch(/^spaces-\d{4}-\d{2}-\d{2}\.csv$/);
    });

    it('should include site ID when provided', () => {
      const filename = getExportFilename('spaces', 'csv', 'site-123');
      expect(filename).toMatch(/^site-123-spaces-\d{4}-\d{2}-\d{2}\.csv$/);
    });

    it('should use correct extension for format', () => {
      expect(getExportFilename('utilization', 'pdf')).toMatch(/\.pdf$/);
      expect(getExportFilename('requests', 'csv')).toMatch(/\.csv$/);
      expect(getExportFilename('criteria', 'json')).toMatch(/\.json$/);
    });
  });

  describe('getSupportedFormats', () => {
    it('should return PDF only for utilization', () => {
      const formats = getSupportedFormats('utilization');
      expect(formats.export).toEqual(['pdf']);
      expect(formats.import).toEqual([]);
    });

    it('should return CSV for spaces with import', () => {
      const formats = getSupportedFormats('spaces');
      expect(formats.export).toEqual(['csv']);
      expect(formats.import).toEqual(['csv']);
    });

    it('should return CSV only export for conflicts', () => {
      const formats = getSupportedFormats('conflicts');
      expect(formats.export).toEqual(['csv']);
      expect(formats.import).toEqual(['csv']);
    });

    it('should return CSV and JSON for criteria with both', () => {
      const formats = getSupportedFormats('criteria');
      expect(formats.export).toEqual(['csv', 'json']);
      expect(formats.import).toEqual(['csv', 'json']);
    });
  });

  describe('isImportSupported', () => {
    it('should return false for utilization', () => {
      expect(isImportSupported('utilization')).toBe(false);
    });

    it('should return true for spaces', () => {
      expect(isImportSupported('spaces')).toBe(true);
    });

    it('should return true for requests', () => {
      expect(isImportSupported('requests')).toBe(true);
    });

    it('should return true for conflicts', () => {
      expect(isImportSupported('conflicts')).toBe(true);
    });

    it('should return true for criteria', () => {
      expect(isImportSupported('criteria')).toBe(true);
    });

    it('should return true for users', () => {
      expect(isImportSupported('users')).toBe(true);
    });
  });

  describe('downloadFile', () => {
    it('should create and trigger download for string content', () => {
      // Mock DOM APIs
      const mockLink = {
        href: '',
        download: '',
        click: vi.fn(),
      };
      const createElementSpy = vi.spyOn(document, 'createElement').mockReturnValue(mockLink as any);
      const createObjectURLSpy = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:mock-url');
      const revokeObjectURLSpy = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});
      const appendChildSpy = vi.spyOn(document.body, 'appendChild').mockImplementation(() => mockLink as any);
      const removeChildSpy = vi.spyOn(document.body, 'removeChild').mockImplementation(() => mockLink as any);

      downloadFile('test content', 'test.txt', 'text/plain');

      expect(createElementSpy).toHaveBeenCalledWith('a');
      expect(createObjectURLSpy).toHaveBeenCalled();
      expect(mockLink.href).toBe('blob:mock-url');
      expect(mockLink.download).toBe('test.txt');
      expect(mockLink.click).toHaveBeenCalled();
      expect(appendChildSpy).toHaveBeenCalled();
      expect(removeChildSpy).toHaveBeenCalled();
      expect(revokeObjectURLSpy).toHaveBeenCalledWith('blob:mock-url');

      // Cleanup
      createElementSpy.mockRestore();
      createObjectURLSpy.mockRestore();
      revokeObjectURLSpy.mockRestore();
      appendChildSpy.mockRestore();
      removeChildSpy.mockRestore();
    });

    it('should handle Blob content', () => {
      const mockLink = {
        href: '',
        download: '',
        click: vi.fn(),
      };
      const createElementSpy = vi.spyOn(document, 'createElement').mockReturnValue(mockLink as any);
      const createObjectURLSpy = vi.spyOn(URL, 'createObjectURL').mockReturnValue('blob:mock-url');
      const revokeObjectURLSpy = vi.spyOn(URL, 'revokeObjectURL').mockImplementation(() => {});
      vi.spyOn(document.body, 'appendChild').mockImplementation(() => mockLink as any);
      vi.spyOn(document.body, 'removeChild').mockImplementation(() => mockLink as any);

      const blob = new Blob(['test'], { type: 'text/plain' });
      downloadFile(blob, 'test.txt', 'text/plain');

      expect(createObjectURLSpy).toHaveBeenCalledWith(blob);

      // Cleanup
      createElementSpy.mockRestore();
      createObjectURLSpy.mockRestore();
      revokeObjectURLSpy.mockRestore();
    });
  });

  describe('getExportFilename', () => {
    it('should generate filename without siteId', () => {
      const filename = getExportFilename('spaces', 'csv');
      expect(filename).toMatch(/^spaces-\d{4}-\d{2}-\d{2}\.csv$/);
    });

    it('should generate filename with siteId', () => {
      const filename = getExportFilename('spaces', 'csv', 'site-123');
      expect(filename).toMatch(/^site-123-spaces-\d{4}-\d{2}-\d{2}\.csv$/);
    });

    it('should handle different formats', () => {
      const csvFile = getExportFilename('requests', 'csv');
      const jsonFile = getExportFilename('requests', 'json');
      const pdfFile = getExportFilename('utilization', 'pdf');

      expect(csvFile).toContain('.csv');
      expect(jsonFile).toContain('.json');
      expect(pdfFile).toContain('.pdf');
    });
  });

  describe('Round-trip conversion', () => {
    it('should maintain data integrity through CSV round-trip', () => {
      const original = [
        { name: 'Room A', capacity: 10, active: true },
        { name: 'Room B, Floor 1', capacity: 20, active: false }
      ];

      const csv = arrayToCSV(original);
      const parsed = csvToArray(csv);

      expect(parsed).toEqual([
        { name: 'Room A', capacity: '10', active: 'true' },
        { name: 'Room B, Floor 1', capacity: '20', active: 'false' }
      ]);
    });
  });
});
