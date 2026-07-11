import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { RequestRowActions } from './RequestRowActions';
import { makeRequest } from '@foundation/src/test-utils/request-fixtures';

const request = makeRequest({ id: 'r-1', name: 'Main Hall Booking' });

function makeHandlers() {
  return {
    onEdit: vi.fn(),
    onDelete: vi.fn(),
  };
}

describe('RequestRowActions', () => {
  it('renders nothing when canEdit is false (viewer)', () => {
    const { container } = render(
      <RequestRowActions
        request={request}
        canEdit={false}
        {...makeHandlers()}
      />,
    );
    expect(container.innerHTML).toBe('');
  });

  it('renders Edit and Delete buttons by default (canEdit defaults to true)', () => {
    render(<RequestRowActions request={request} {...makeHandlers()} />);
    expect(screen.getByRole('button', { name: 'Edit Main Hall Booking' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Delete Main Hall Booking' })).toBeInTheDocument();
  });

  it('does not render a "Move to" action', () => {
    render(<RequestRowActions request={request} {...makeHandlers()} />);
    expect(screen.queryByText(/Move to/i)).not.toBeInTheDocument();
  });

  it('invokes onEdit when the Edit button is clicked', async () => {
    const handlers = makeHandlers();
    render(<RequestRowActions request={request} {...handlers} />);
    await userEvent.click(screen.getByRole('button', { name: 'Edit Main Hall Booking' }));
    expect(handlers.onEdit).toHaveBeenCalledWith(request);
  });

  it('invokes onDelete when the Delete button is clicked', async () => {
    const handlers = makeHandlers();
    render(<RequestRowActions request={request} {...handlers} />);
    await userEvent.click(screen.getByRole('button', { name: 'Delete Main Hall Booking' }));
    expect(handlers.onDelete).toHaveBeenCalledWith(request);
  });

  it('stops propagation on Edit click so a parent row onClick does not also fire', async () => {
    const handlers = makeHandlers();
    const parentOnClick = vi.fn();
    render(
      <div onClick={parentOnClick}>
        <RequestRowActions request={request} {...handlers} />
      </div>,
    );
    await userEvent.click(screen.getByRole('button', { name: 'Edit Main Hall Booking' }));
    expect(handlers.onEdit).toHaveBeenCalledWith(request);
    expect(parentOnClick).not.toHaveBeenCalled();
  });

  it('stops propagation on Delete click so a parent row onClick does not also fire', async () => {
    const handlers = makeHandlers();
    const parentOnClick = vi.fn();
    render(
      <div onClick={parentOnClick}>
        <RequestRowActions request={request} {...handlers} />
      </div>,
    );
    await userEvent.click(screen.getByRole('button', { name: 'Delete Main Hall Booking' }));
    expect(handlers.onDelete).toHaveBeenCalledWith(request);
    expect(parentOnClick).not.toHaveBeenCalled();
  });
});
