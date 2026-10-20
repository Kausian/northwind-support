import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { spokenText } from '../../../test/a11y';
import { ticketEscalation } from '../../../test/fixtures';
import { EscalationHistory } from './EscalationHistory';

describe('EscalationHistory', () => {
  it('says so when the ticket was never escalated', () => {
    render(<EscalationHistory escalations={[]} isLoading={false} onRetry={vi.fn()} />);

    expect(screen.getByText('Not escalated yet')).toBeInTheDocument();
    expect(screen.queryByRole('list')).not.toBeInTheDocument();
  });

  it('shows a loading state before the first answer', () => {
    render(<EscalationHistory isLoading onRetry={vi.fn()} />);

    expect(screen.getByRole('status')).toHaveTextContent('Loading escalation history');
  });

  it('shows an error with a retry', async () => {
    const onRetry = vi.fn();
    render(<EscalationHistory isLoading={false} error="Unable to load the escalation history." onRetry={onRetry} />);

    expect(screen.getByRole('alert')).toHaveTextContent('Unable to load the escalation history.');
    await userEvent.click(screen.getByRole('button', { name: 'Try again' }));
    expect(onRetry).toHaveBeenCalled();
  });

  it('lists every escalation in the order the API gives (newest first), with all of its fields', () => {
    render(
      <EscalationHistory
        isLoading={false}
        onRetry={vi.fn()}
        escalations={[
          ticketEscalation({
            id: 2,
            fromPriority: 'High',
            toPriority: 'Critical',
            fromAgent: { id: 1, fullName: 'Alex Turner' },
            toAgent: null,
            reason: 'Whole region down',
            escalatedBy: 'Ben Osei',
            escalatedAtUtc: '2026-09-24T12:00:00Z',
          }),
          ticketEscalation({ id: 1, fromAgent: null, fromDueAtUtc: null }),
        ]}
      />,
    );

    const [newest, oldest] = screen.getAllByRole('listitem');

    expect(within(newest).getByText('Whole region down')).toBeInTheDocument();
    expect(within(newest).getByText('Ben Osei')).toBeInTheDocument();
    expect(within(newest).getByText('High')).toBeInTheDocument();
    expect(within(newest).getByText('Critical')).toBeInTheDocument();
    expect(spokenText(within(newest).getByText(/Alex Turner/))).toBe('Alex Turner to Unassigned');
    expect(within(newest).getByRole('time')).toHaveAttribute('dateTime', '2026-09-24T12:00:00Z');

    expect(within(oldest).getByText('Customer cannot close their books')).toBeInTheDocument();
    expect(within(oldest).getByText('Priya Nair')).toBeInTheDocument();
    expect(spokenText(within(oldest).getByText(/Unassigned/))).toBe('Unassigned to Sara Lindqvist');
    // A ticket that had no due date before says so with a dash, not an invalid date.
    expect(within(oldest).getByText(/^-/)).toBeInTheDocument();
  });
});
