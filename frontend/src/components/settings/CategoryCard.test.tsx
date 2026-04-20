import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { CategoryCard } from './CategoryCard';

vi.mock('./SettingRow', () => ({
  SettingRow: ({ descriptor }: { descriptor: { key: string; displayName: string } }) => (
    <div data-testid={`setting-${descriptor.key}`}>{descriptor.displayName}</div>
  ),
}));

const mockSettings = [
  {
    key: 'security.mfa',
    displayName: 'MFA Required',
    category: 'security',
    description: 'Require multi-factor authentication',
    valueType: 'bool',
    currentValue: 'True',
    defaultValue: 'False',
    minValue: null,
    maxValue: null,
    enumValues: null,
  },
  {
    key: 'security.session_timeout',
    displayName: 'Session Timeout',
    category: 'security',
    description: 'Session duration in minutes',
    valueType: 'int',
    currentValue: '30',
    defaultValue: '30',
    minValue: '5',
    maxValue: '480',
    enumValues: null,
  },
];

describe('CategoryCard', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders category label from CATEGORY_META', () => {
    render(
      <CategoryCard
        category="security"
        settings={mockSettings as never[]}
        editValues={{ 'security.mfa': 'True', 'security.session_timeout': '30' }}
        onChange={vi.fn()}
        onReset={vi.fn()}
        resettingKey={null}
      />,
    );
    expect(screen.getByText('Security')).toBeInTheDocument();
    expect(screen.getByText('Authentication and access-control settings')).toBeInTheDocument();
  });

  it('renders a SettingRow for each setting', () => {
    render(
      <CategoryCard
        category="security"
        settings={mockSettings as never[]}
        editValues={{}}
        onChange={vi.fn()}
        onReset={vi.fn()}
        resettingKey={null}
      />,
    );
    expect(screen.getByTestId('setting-security.mfa')).toBeInTheDocument();
    expect(screen.getByTestId('setting-security.session_timeout')).toBeInTheDocument();
  });

  it('falls back to category name for unknown categories', () => {
    render(
      <CategoryCard
        category="custom_cat"
        settings={[]}
        editValues={{}}
        onChange={vi.fn()}
        onReset={vi.fn()}
        resettingKey={null}
      />,
    );
    expect(screen.getByText('custom_cat')).toBeInTheDocument();
  });
});
