import { describe, it, expect } from 'vitest';
import { validateSpaceRequirements } from './validation';
import type { Request, RequestRequirement } from '@/types/requests';
import type { SpaceCapability } from '@/lib/api/space-capability-api';

// Helper to create minimal valid Request objects for testing
function createTestRequest(overrides: Partial<Request> & { requirements?: RequestRequirement[] } = {}): Request {
  return {
    id: 'req-1',
    name: 'Test Request',
    minimalDurationValue: 60,
    minimalDurationUnit: 'minutes',
    status: 'planned',
    planningMode: 'leaf',
    sortOrder: 0,
    schedulingSettingsApply: true,
    createdAt: new Date().toISOString(),
    updatedAt: new Date().toISOString(),
    ...overrides,
  };
}

describe('validateSpaceRequirements', () => {
  describe('empty requirements', () => {
    it('should return no conflicts when request has no requirements', () => {
      const request = createTestRequest({ requirements: [] });

      const capabilities: SpaceCapability[] = [];

      const conflicts = validateSpaceRequirements(request, capabilities);
      expect(conflicts).toEqual([]);
    });

    it('should return no conflicts when request has undefined requirements', () => {
      const request = createTestRequest();

      const capabilities: SpaceCapability[] = [];

      const conflicts = validateSpaceRequirements(request, capabilities);
      expect(conflicts).toEqual([]);
    });
  });

  describe('missing capabilities', () => {
    it('should create conflict when space is missing required capability', () => {
      const request = createTestRequest({ requirements: [
          {
            id: 'req-req-1',
            requestId: 'req-1',
            criterionId: 'crit-1',
            value: 50,
            criterion: {
              id: 'crit-1',
              name: 'Capacity',
              dataType: 'Number',
            },
          },
        ] });

      const capabilities: SpaceCapability[] = [];

      const conflicts = validateSpaceRequirements(request, capabilities);
      expect(conflicts).toHaveLength(1);
      expect(conflicts[0]).toMatchObject({
        id: 'req-1-crit-1-missing',
        kind: 'connector_mismatch',
        severity: 'error',
        message: 'Space is missing required capability: Capacity',
      });
    });
  });

  describe('numeric requirements', () => {
    it('should pass when capability meets numeric requirement', () => {
      const request = createTestRequest({ requirements: [
          {
            id: 'req-req-1',
            requestId: 'req-1',
            criterionId: 'crit-1',
            value: 50,
            criterion: {
              id: 'crit-1',
              name: 'Capacity',
              dataType: 'Number',
            },
          },
        ] });

      const capabilities: SpaceCapability[] = [
        {
          id: 'cap-1',
          spaceId: 'space-1',
          criterionId: 'crit-1',
          value: 100,
          createdAt: new Date().toISOString(),
          updatedAt: new Date().toISOString(),
          criterion: {
            id: 'crit-1',
            name: 'Capacity',
            dataType: 'Number',
          },
        },
      ];

      const conflicts = validateSpaceRequirements(request, capabilities);
      expect(conflicts).toEqual([]);
    });

    it('should pass when capability exactly matches numeric requirement', () => {
      const request = createTestRequest({ requirements: [
          {
            id: 'req-req-1',
            requestId: 'req-1',
            criterionId: 'crit-1',
            value: 50,
            criterion: {
              id: 'crit-1',
              name: 'Capacity',
              dataType: 'Number',
            },
          },
        ] });

      const capabilities: SpaceCapability[] = [
        {
          id: 'cap-1',
          spaceId: 'space-1',
          criterionId: 'crit-1',
          value: 50,
          createdAt: new Date().toISOString(),
          updatedAt: new Date().toISOString(),
          criterion: {
            id: 'crit-1',
            name: 'Capacity',
            dataType: 'Number',
          },
        },
      ];

      const conflicts = validateSpaceRequirements(request, capabilities);
      expect(conflicts).toEqual([]);
    });

    it('should create conflict when capability is less than numeric requirement', () => {
      const request = createTestRequest({ requirements: [
          {
            id: 'req-req-1',
            requestId: 'req-1',
            criterionId: 'crit-1',
            value: 100,
            criterion: {
              id: 'crit-1',
              name: 'Capacity',
              dataType: 'Number',
              unit: 'people',
            },
          },
        ] });

      const capabilities: SpaceCapability[] = [
        {
          id: 'cap-1',
          spaceId: 'space-1',
          criterionId: 'crit-1',
          value: 50,
          createdAt: new Date().toISOString(),
          updatedAt: new Date().toISOString(),
          criterion: {
            id: 'crit-1',
            name: 'Capacity',
            dataType: 'Number',
            unit: 'people',
          },
        },
      ];

      const conflicts = validateSpaceRequirements(request, capabilities);
      expect(conflicts).toHaveLength(1);
      expect(conflicts[0]).toMatchObject({
        id: 'req-1-crit-1-insufficient',
        kind: 'load_exceeded',
        severity: 'error',
        message: 'Capacity: Space has 50 people, but requires 100 people',
      });
    });

    it('should handle lowercase dataType from legacy data', () => {
      const request = createTestRequest({ requirements: [
          {
            id: 'req-req-1',
            requestId: 'req-1',
            criterionId: 'crit-1',
            value: 10,
            criterion: {
              id: 'crit-1',
              name: 'Monitors',
              dataType: 'number',
            },
          },
        ] });

      const capabilities: SpaceCapability[] = [
        {
          id: 'cap-1',
          spaceId: 'space-1',
          criterionId: 'crit-1',
          value: 5,
          createdAt: new Date().toISOString(),
          updatedAt: new Date().toISOString(),
          criterion: {
            id: 'crit-1',
            name: 'Monitors',
            dataType: 'number',
          },
        },
      ];

      const conflicts = validateSpaceRequirements(request, capabilities);
      expect(conflicts).toHaveLength(1);
      expect(conflicts[0].kind).toBe('load_exceeded');
    });
  });

  describe('boolean requirements', () => {
    it('should pass when boolean capability matches true requirement', () => {
      const request = createTestRequest({ requirements: [
          {
            id: 'req-req-1',
            requestId: 'req-1',
            criterionId: 'crit-1',
            value: true,
            criterion: {
              id: 'crit-1',
              name: 'Projector',
              dataType: 'Boolean',
            },
          },
        ] });

      const capabilities: SpaceCapability[] = [
        {
          id: 'cap-1',
          spaceId: 'space-1',
          criterionId: 'crit-1',
          value: true,
          createdAt: new Date().toISOString(),
          updatedAt: new Date().toISOString(),
          criterion: {
            id: 'crit-1',
            name: 'Projector',
            dataType: 'Boolean',
          },
        },
      ];

      const conflicts = validateSpaceRequirements(request, capabilities);
      expect(conflicts).toEqual([]);
    });

    it('should pass when boolean requirement is false', () => {
      const request = createTestRequest({ requirements: [
          {
            id: 'req-req-1',
            requestId: 'req-1',
            criterionId: 'crit-1',
            value: false,
            criterion: {
              id: 'crit-1',
              name: 'Projector',
              dataType: 'Boolean',
            },
          },
        ] });

      const capabilities: SpaceCapability[] = [
        {
          id: 'cap-1',
          spaceId: 'space-1',
          criterionId: 'crit-1',
          value: false,
          createdAt: new Date().toISOString(),
          updatedAt: new Date().toISOString(),
          criterion: {
            id: 'crit-1',
            name: 'Projector',
            dataType: 'Boolean',
          },
        },
      ];

      const conflicts = validateSpaceRequirements(request, capabilities);
      expect(conflicts).toEqual([]);
    });

    it('should create conflict when boolean capability is false but requirement is true', () => {
      const request = createTestRequest({ requirements: [
          {
            id: 'req-req-1',
            requestId: 'req-1',
            criterionId: 'crit-1',
            value: true,
            criterion: {
              id: 'crit-1',
              name: 'Projector',
              dataType: 'Boolean',
            },
          },
        ] });

      const capabilities: SpaceCapability[] = [
        {
          id: 'cap-1',
          spaceId: 'space-1',
          criterionId: 'crit-1',
          value: false,
          createdAt: new Date().toISOString(),
          updatedAt: new Date().toISOString(),
          criterion: {
            id: 'crit-1',
            name: 'Projector',
            dataType: 'Boolean',
          },
        },
      ];

      const conflicts = validateSpaceRequirements(request, capabilities);
      expect(conflicts).toHaveLength(1);
      expect(conflicts[0]).toMatchObject({
        id: 'req-1-crit-1-boolean',
        kind: 'connector_mismatch',
        severity: 'error',
        message: 'Projector: Required but not available in space',
      });
    });
  });

  describe('string requirements', () => {
    it('should pass when string capability matches requirement', () => {
      const request = createTestRequest({ requirements: [
          {
            id: 'req-req-1',
            requestId: 'req-1',
            criterionId: 'crit-1',
            value: 'HDMI',
            criterion: {
              id: 'crit-1',
              name: 'Connector Type',
              dataType: 'String',
            },
          },
        ] });

      const capabilities: SpaceCapability[] = [
        {
          id: 'cap-1',
          spaceId: 'space-1',
          criterionId: 'crit-1',
          value: 'HDMI',
          createdAt: new Date().toISOString(),
          updatedAt: new Date().toISOString(),
          criterion: {
            id: 'crit-1',
            name: 'Connector Type',
            dataType: 'String',
          },
        },
      ];

      const conflicts = validateSpaceRequirements(request, capabilities);
      expect(conflicts).toEqual([]);
    });

    it('should create conflict when string capability does not match requirement', () => {
      const request = createTestRequest({ requirements: [
          {
            id: 'req-req-1',
            requestId: 'req-1',
            criterionId: 'crit-1',
            value: 'HDMI',
            criterion: {
              id: 'crit-1',
              name: 'Connector Type',
              dataType: 'String',
            },
          },
        ] });

      const capabilities: SpaceCapability[] = [
        {
          id: 'cap-1',
          spaceId: 'space-1',
          criterionId: 'crit-1',
          value: 'VGA',
          createdAt: new Date().toISOString(),
          updatedAt: new Date().toISOString(),
          criterion: {
            id: 'crit-1',
            name: 'Connector Type',
            dataType: 'String',
          },
        },
      ];

      const conflicts = validateSpaceRequirements(request, capabilities);
      expect(conflicts).toHaveLength(1);
      expect(conflicts[0]).toMatchObject({
        id: 'req-1-crit-1-string',
        kind: 'connector_mismatch',
        severity: 'warning',
        message: 'Connector Type: Space has "VGA", but requires "HDMI"',
      });
    });
  });

  describe('enum requirements', () => {
    it('should pass when enum capability matches requirement', () => {
      const request = createTestRequest({ requirements: [
          {
            id: 'req-req-1',
            requestId: 'req-1',
            criterionId: 'crit-1',
            value: 'Theater',
            criterion: {
              id: 'crit-1',
              name: 'Seating Style',
              dataType: 'Enum',
            },
          },
        ] });

      const capabilities: SpaceCapability[] = [
        {
          id: 'cap-1',
          spaceId: 'space-1',
          criterionId: 'crit-1',
          value: 'Theater',
          createdAt: new Date().toISOString(),
          updatedAt: new Date().toISOString(),
          criterion: {
            id: 'crit-1',
            name: 'Seating Style',
            dataType: 'Enum',
          },
        },
      ];

      const conflicts = validateSpaceRequirements(request, capabilities);
      expect(conflicts).toEqual([]);
    });

    it('should create conflict when enum capability does not match requirement', () => {
      const request = createTestRequest({ requirements: [
          {
            id: 'req-req-1',
            requestId: 'req-1',
            criterionId: 'crit-1',
            value: 'Theater',
            criterion: {
              id: 'crit-1',
              name: 'Seating Style',
              dataType: 'Enum',
            },
          },
        ] });

      const capabilities: SpaceCapability[] = [
        {
          id: 'cap-1',
          spaceId: 'space-1',
          criterionId: 'crit-1',
          value: 'Classroom',
          createdAt: new Date().toISOString(),
          updatedAt: new Date().toISOString(),
          criterion: {
            id: 'crit-1',
            name: 'Seating Style',
            dataType: 'Enum',
          },
        },
      ];

      const conflicts = validateSpaceRequirements(request, capabilities);
      expect(conflicts).toHaveLength(1);
      expect(conflicts[0]).toMatchObject({
        id: 'req-1-crit-1-enum',
        kind: 'size_mismatch',
        severity: 'error',
        message: 'Seating Style: Space has "Classroom", but requires "Theater"',
      });
    });
  });

  describe('multiple requirements', () => {
    it('should validate all requirements and return multiple conflicts', () => {
      const request = createTestRequest({ requirements: [
          {
            id: 'req-req-1',
            requestId: 'req-1',
            criterionId: 'crit-capacity',
            value: 100,
            criterion: {
              id: 'crit-1',
              name: 'Capacity',
              dataType: 'Number',
            },
          },
          {
            id: 'req-req-2',
            requestId: 'req-1',
            criterionId: 'crit-projector',
            value: true,
            criterion: {
              id: 'crit-1',
              name: 'Projector',
              dataType: 'Boolean',
            },
          },
          {
            id: 'req-req-3',
            requestId: 'req-1',
            criterionId: 'crit-seating',
            value: 'Theater',
            criterion: {
              id: 'crit-1',
              name: 'Seating',
              dataType: 'Enum',
            },
          },
        ] });

      const capabilities: SpaceCapability[] = [
        {
          id: 'cap-1',
          spaceId: 'space-1',
          criterionId: 'crit-capacity',
          value: 50, // Too small
          createdAt: new Date().toISOString(),
          updatedAt: new Date().toISOString(),
          criterion: {
            id: 'crit-capacity',
            name: 'Capacity',
            dataType: 'Number',
          },
        },
        {
          id: 'cap-2',
          spaceId: 'space-1',
          criterionId: 'crit-projector',
          value: false, // Not available
          createdAt: new Date().toISOString(),
          updatedAt: new Date().toISOString(),
          criterion: {
            id: 'crit-projector',
            name: 'Projector',
            dataType: 'Boolean',
          },
        },
        {
          id: 'cap-3',
          spaceId: 'space-1',
          criterionId: 'crit-seating',
          value: 'Classroom', // Wrong style
          createdAt: new Date().toISOString(),
          updatedAt: new Date().toISOString(),
          criterion: {
            id: 'crit-seating',
            name: 'Seating',
            dataType: 'Enum',
          },
        },
      ];

      const conflicts = validateSpaceRequirements(request, capabilities);
      expect(conflicts).toHaveLength(3);
      expect(conflicts[0].kind).toBe('load_exceeded');
      expect(conflicts[1].kind).toBe('connector_mismatch');
      expect(conflicts[2].kind).toBe('size_mismatch');
    });

    it('should pass when all requirements are met', () => {
      const request = createTestRequest({ requirements: [
          {
            id: 'req-req-1',
            requestId: 'req-1',
            criterionId: 'crit-capacity',
            value: 50,
            criterion: {
              id: 'crit-1',
              name: 'Capacity',
              dataType: 'Number',
            },
          },
          {
            id: 'req-req-2',
            requestId: 'req-1',
            criterionId: 'crit-projector',
            value: true,
            criterion: {
              id: 'crit-1',
              name: 'Projector',
              dataType: 'Boolean',
            },
          },
        ] });

      const capabilities: SpaceCapability[] = [
        {
          id: 'cap-1',
          spaceId: 'space-1',
          criterionId: 'crit-capacity',
          value: 100,
          createdAt: new Date().toISOString(),
          updatedAt: new Date().toISOString(),
          criterion: {
            id: 'crit-capacity',
            name: 'Capacity',
            dataType: 'Number',
          },
        },
        {
          id: 'cap-2',
          spaceId: 'space-1',
          criterionId: 'crit-projector',
          value: true,
          createdAt: new Date().toISOString(),
          updatedAt: new Date().toISOString(),
          criterion: {
            id: 'crit-projector',
            name: 'Projector',
            dataType: 'Boolean',
          },
        },
      ];

      const conflicts = validateSpaceRequirements(request, capabilities);
      expect(conflicts).toEqual([]);
    });
  });

  describe('edge cases', () => {
    it('should handle unknown dataType as connector_mismatch conflict', () => {
      const request = createTestRequest({ requirements: [
          {
            id: 'req-req-1',
            requestId: 'req-1',
            criterionId: 'crit-1',
            value: 'custom',
            criterion: {
              id: 'crit-1',
              name: 'Custom Field',
              dataType: 'unknown-type',
            },
          },
        ] });

      const capabilities: SpaceCapability[] = [
        {
          id: 'cap-1',
          spaceId: 'space-1',
          criterionId: 'crit-1',
          value: 'different',
          createdAt: new Date().toISOString(),
          updatedAt: new Date().toISOString(),
          criterion: {
            id: 'crit-1',
            name: 'Custom Field',
            dataType: 'unknown-type',
          },
        },
      ];

      const conflicts = validateSpaceRequirements(request, capabilities);
      expect(conflicts).toHaveLength(1);
      expect(conflicts[0]).toMatchObject({
        id: 'req-1-crit-1-unknown',
        kind: 'connector_mismatch',
        severity: 'warning',
      });
    });

    it('should not create conflict for unknown dataType when values match', () => {
      const request = createTestRequest({ requirements: [
          {
            id: 'req-req-1',
            requestId: 'req-1',
            criterionId: 'crit-1',
            value: 'custom',
            criterion: {
              id: 'crit-1',
              name: 'Custom Field',
              dataType: 'unknown-type',
            },
          },
        ] });

      const capabilities: SpaceCapability[] = [
        {
          id: 'cap-1',
          spaceId: 'space-1',
          criterionId: 'crit-1',
          value: 'custom',
          createdAt: new Date().toISOString(),
          updatedAt: new Date().toISOString(),
          criterion: {
            id: 'crit-1',
            name: 'Custom Field',
            dataType: 'unknown-type',
          },
        },
      ];

      const conflicts = validateSpaceRequirements(request, capabilities);
      expect(conflicts).toEqual([]);
    });

    it('should handle case-insensitive dataType matching', () => {
      // Backend sends PascalCase but code should be resilient to any casing
      const request = createTestRequest({ requirements: [
          {
            id: 'req-req-1',
            requestId: 'req-1',
            criterionId: 'crit-1',
            value: true,
            criterion: {
              id: 'crit-1',
              name: 'Feature',
              dataType: 'boolean', // lowercase
            },
          },
          {
            id: 'req-req-2',
            requestId: 'req-1',
            criterionId: 'crit-2',
            value: 'Theater',
            criterion: {
              id: 'crit-1',
              name: 'Layout',
              dataType: 'ENUM', // uppercase
            },
          },
        ] });

      const capabilities: SpaceCapability[] = [
        {
          id: 'cap-1',
          spaceId: 'space-1',
          criterionId: 'crit-1',
          value: false,
          createdAt: new Date().toISOString(),
          updatedAt: new Date().toISOString(),
          criterion: {
            id: 'crit-1',
            name: 'Feature',
            dataType: 'boolean',
          },
        },
        {
          id: 'cap-2',
          spaceId: 'space-1',
          criterionId: 'crit-2',
          value: 'Classroom',
          createdAt: new Date().toISOString(),
          updatedAt: new Date().toISOString(),
          criterion: {
            id: 'crit-2',
            name: 'Layout',
            dataType: 'ENUM',
          },
        },
      ];

      const conflicts = validateSpaceRequirements(request, capabilities);
      expect(conflicts).toHaveLength(2);
      expect(conflicts[0].kind).toBe('connector_mismatch'); // boolean
      expect(conflicts[1].kind).toBe('size_mismatch'); // enum
    });
  });
});
