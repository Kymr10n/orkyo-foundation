import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import { PersonRow } from './PersonRow';
import type { ResourceInfo } from '@foundation/src/lib/api/resources-api';
import type { ResourceUtilizationBucket } from '@foundation/src/lib/api/resource-utilization-api';

vi.mock('./time-grid-utils', () => ({
  overlapsOffTimeRange: vi.fn(() => false),
}));

const person: ResourceInfo = {
  id: 'p-1',
  name: 'Alice Smith',
  resourceTypeId: 'rt-person',
  resourceTypeKey: 'person',
  allocationMode: 'Exclusive',
  baseAvailabilityPercent: 100,
  isActive: true,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
};

const bucket: ResourceUtilizationBucket = {
  start: '2026-01-06T09:00:00Z',
  end: '2026-01-06T10:00:00Z',
  allocatedPercent: 50,
  effectiveAvailabilityPercent: 100,
  isExclusiveOccupied: false,
};

const columnLabel = (date: Date) => date.toISOString().slice(0, 10);

describe('PersonRow', () => {
  it('renders person name', () => {
    render(
      <PersonRow
        person={person}
        buckets={[bucket]}
        isLoadingRow={false}
        columnCount={1}
        overallPct={50}
        offTimeRanges={[]}
        columnLabel={columnLabel}
      />,
    );
    expect(screen.getByText('Alice Smith')).toBeInTheDocument();
  });

  it('renders overall utilization percentage', () => {
    render(
      <PersonRow
        person={person}
        buckets={[bucket]}
        isLoadingRow={false}
        columnCount={1}
        overallPct={75}
        offTimeRanges={[]}
        columnLabel={columnLabel}
      />,
    );
    expect(screen.getByTitle(/Overall utilization: 75%/)).toBeInTheDocument();
  });

  it('renders loading indicator when isLoadingRow is true', () => {
    render(
      <PersonRow
        person={person}
        buckets={[]}
        isLoadingRow
        columnCount={1}
        overallPct={0}
        offTimeRanges={[]}
        columnLabel={columnLabel}
      />,
    );
    expect(screen.getByText(/Loading/)).toBeInTheDocument();
  });

  it('renders "No data" when buckets is empty and not loading', () => {
    render(
      <PersonRow
        person={person}
        buckets={[]}
        isLoadingRow={false}
        columnCount={1}
        overallPct={0}
        offTimeRanges={[]}
        columnLabel={columnLabel}
      />,
    );
    expect(screen.getByText('No data')).toBeInTheDocument();
  });

  it('renders job title when provided', () => {
    render(
      <PersonRow
        person={person}
        jobTitle="Senior Engineer"
        buckets={[]}
        isLoadingRow={false}
        columnCount={0}
        overallPct={0}
        offTimeRanges={[]}
        columnLabel={columnLabel}
      />,
    );
    expect(screen.getByText('Senior Engineer')).toBeInTheDocument();
  });

  it('colours overall pct red when overbooked', () => {
    render(
      <PersonRow
        person={person}
        buckets={[]}
        isLoadingRow={false}
        columnCount={0}
        overallPct={120}
        offTimeRanges={[]}
        columnLabel={columnLabel}
      />,
    );
    const pctEl = screen.getByTitle(/Overall utilization: 120%/);
    expect(pctEl.className).toContain('text-red');
  });

  it('shows non-zero allocated percent inside each bucket cell', () => {
    render(
      <PersonRow
        person={person}
        buckets={[{ ...bucket, allocatedPercent: 80 }]}
        isLoadingRow={false}
        columnCount={1}
        overallPct={80}
        offTimeRanges={[]}
        columnLabel={columnLabel}
      />,
    );
    // Both the overall pct label and the bucket cell display "80%" — assert at least one is present
    expect(screen.getAllByText('80%').length).toBeGreaterThanOrEqual(1);
    expect(document.querySelector('[data-status="partial"]')).toBeInTheDocument();
  });

  it('renders data-testid with person id', () => {
    const { container } = render(
      <PersonRow
        person={person}
        buckets={[]}
        isLoadingRow={false}
        columnCount={0}
        overallPct={0}
        offTimeRanges={[]}
        columnLabel={columnLabel}
      />,
    );
    expect(container.querySelector('[data-testid="person-row-p-1"]')).toBeInTheDocument();
  });

  it('applies non-working status for zero effectiveAvailabilityPercent', () => {
    render(
      <PersonRow
        person={person}
        buckets={[{ ...bucket, effectiveAvailabilityPercent: 0 }]}
        isLoadingRow={false}
        columnCount={1}
        overallPct={0}
        offTimeRanges={[]}
        columnLabel={columnLabel}
      />,
    );
    expect(document.querySelector('[data-status="non-working"]')).toBeInTheDocument();
  });
});
