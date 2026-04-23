import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { SettingRow } from './SettingRow';
import type { TenantSettingDescriptor } from '@foundation/src/lib/api/tenant-settings-api';

vi.mock('./tenant-config-helpers', () => ({
  isModified: vi.fn((d) => d.currentValue !== d.defaultValue),
  formatRange: vi.fn((d) => {
    if (d.minValue != null && d.maxValue != null) return `${d.minValue}–${d.maxValue}`;
    return null;
  }),
  isColorSetting: vi.fn((d) => d.valueType === 'string' && d.key.endsWith('_color')),
  validate: vi.fn(() => null),
}));

const { validate } = await import('./tenant-config-helpers');

function makeDescriptor(overrides: Partial<TenantSettingDescriptor> = {}): TenantSettingDescriptor {
  return {
    key: 'max_items',
    category: 'scheduling',
    displayName: 'Max Items',
    description: 'Maximum number of items',
    valueType: 'int',
    defaultValue: '10',
    scope: 'tenant',
    minValue: '1',
    maxValue: '100',
    currentValue: '10',
    ...overrides,
  };
}

const defaultProps = {
  descriptor: makeDescriptor(),
  editValue: '10',
  onChange: vi.fn(),
  onReset: vi.fn(),
  isResetting: false,
};

describe('SettingRow', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders display name and description', () => {
    render(<SettingRow {...defaultProps} />);
    expect(screen.getByText('Max Items')).toBeInTheDocument();
    expect(screen.getByText('Maximum number of items')).toBeInTheDocument();
  });

  it('renders text input for string type', () => {
    render(<SettingRow {...defaultProps} />);
    const input = screen.getByRole('textbox');
    expect(input).toHaveValue('10');
  });

  it('calls onChange when text input changes', () => {
    render(<SettingRow {...defaultProps} />);
    fireEvent.change(screen.getByRole('textbox'), { target: { value: '20' } });
    expect(defaultProps.onChange).toHaveBeenCalledWith('max_items', '20');
  });

  it('renders switch for bool type', () => {
    const descriptor = makeDescriptor({ key: 'feature_flag', valueType: 'bool', defaultValue: 'False', currentValue: 'False' });
    render(<SettingRow {...defaultProps} descriptor={descriptor} editValue="True" />);
    const switchEl = screen.getByRole('switch');
    expect(switchEl).toBeInTheDocument();
    expect(switchEl).toHaveAttribute('data-state', 'checked');
  });

  it('toggles switch off calls onChange with False', () => {
    const descriptor = makeDescriptor({ key: 'feature_flag', valueType: 'bool', defaultValue: 'False', currentValue: 'True' });
    render(<SettingRow {...defaultProps} descriptor={descriptor} editValue="True" />);
    fireEvent.click(screen.getByRole('switch'));
    expect(defaultProps.onChange).toHaveBeenCalledWith('feature_flag', 'False');
  });

  it('shows modified badge when value differs from default', () => {
    const descriptor = makeDescriptor({ currentValue: '50' });
    render(<SettingRow {...defaultProps} descriptor={descriptor} editValue="50" />);
    expect(screen.getByText('modified')).toBeInTheDocument();
  });

  it('does not show modified badge when values match', () => {
    render(<SettingRow {...defaultProps} />);
    expect(screen.queryByText('modified')).not.toBeInTheDocument();
  });

  it('shows reset button when modified', () => {
    const descriptor = makeDescriptor({ currentValue: '50' });
    render(<SettingRow {...defaultProps} descriptor={descriptor} editValue="50" />);
    expect(screen.getByRole('button')).toBeInTheDocument();
  });

  it('calls onReset when reset button clicked', () => {
    const descriptor = makeDescriptor({ currentValue: '50' });
    render(<SettingRow {...defaultProps} descriptor={descriptor} editValue="50" />);
    fireEvent.click(screen.getByRole('button'));
    expect(defaultProps.onReset).toHaveBeenCalledWith('max_items');
  });

  it('shows validation error', () => {
    vi.mocked(validate).mockReturnValue('Value out of range');
    render(<SettingRow {...defaultProps} editValue="999" />);
    expect(screen.getByText('Value out of range')).toBeInTheDocument();
  });

  it('renders color input for color settings', () => {
    const descriptor = makeDescriptor({ key: 'brand_color', valueType: 'string', defaultValue: '#000000', currentValue: '#000000' });
    render(<SettingRow {...defaultProps} descriptor={descriptor} editValue="#ff0000" />);
    // Should render both a color input and a text input
    const textInput = screen.getByRole('textbox');
    expect(textInput).toHaveValue('#ff0000');
  });

  it('uses numeric inputMode for int type', () => {
    render(<SettingRow {...defaultProps} />);
    const input = screen.getByRole('textbox');
    expect(input).toHaveAttribute('inputMode', 'numeric');
  });

  it('uses decimal inputMode for double type', () => {
    const descriptor = makeDescriptor({ valueType: 'double' });
    render(<SettingRow {...defaultProps} descriptor={descriptor} />);
    const input = screen.getByRole('textbox');
    expect(input).toHaveAttribute('inputMode', 'decimal');
  });
});
