import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { TooltipProvider } from '@foundation/src/components/ui/tooltip';
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

describe('RequestRowActions — sequencing a group', () => {
  const group = makeRequest({ id: 'g-1', name: 'Q3 Structural Frames', planningMode: 'summary' });
  const label = 'Sequence the tasks in Q3 Structural Frames';

  function renderRow(props: Partial<React.ComponentProps<typeof RequestRowActions>>) {
    return render(
      <TooltipProvider>
        <RequestRowActions request={group} {...makeHandlers()} {...props} />
      </TooltipProvider>,
    );
  }

  it('offers the planner on a group that has tasks to order', async () => {
    const onOpenPlan = vi.fn();
    renderRow({ childCount: 2, onOpenPlan });
    await userEvent.click(screen.getByRole('button', { name: label }));
    expect(onOpenPlan).toHaveBeenCalledWith(group);
  });

  it('hides the planner on a group holding one task — ordering one thing is not an operation', () => {
    renderRow({ childCount: 1, onOpenPlan: vi.fn() });
    expect(screen.queryByRole('button', { name: label })).not.toBeInTheDocument();
  });

  it('hides the planner on a task, which has no children to order', () => {
    render(
      <TooltipProvider>
        <RequestRowActions
          request={makeRequest({ id: 'r-2', name: 'Lift weldments', planningMode: 'leaf' })}
          childCount={3}
          onOpenPlan={vi.fn()}
          {...makeHandlers()}
        />
      </TooltipProvider>,
    );
    expect(screen.queryByRole('button', { name: /Sequence the tasks/ })).not.toBeInTheDocument();
  });

  it('hides the planner when no handler is supplied', () => {
    renderRow({ childCount: 5 });
    expect(screen.queryByRole('button', { name: label })).not.toBeInTheDocument();
  });

  it('stops propagation so opening the planner does not also select the row', async () => {
    const onOpenPlan = vi.fn();
    const parentOnClick = vi.fn();
    render(
      <div onClick={parentOnClick}>
        <TooltipProvider>
          <RequestRowActions request={group} childCount={4} onOpenPlan={onOpenPlan} {...makeHandlers()} />
        </TooltipProvider>
      </div>,
    );
    await userEvent.click(screen.getByRole('button', { name: label }));
    expect(onOpenPlan).toHaveBeenCalledWith(group);
    expect(parentOnClick).not.toHaveBeenCalled();
  });
});
