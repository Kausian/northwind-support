import { fireEvent, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ApiError } from '../../../api/client';
import { ticketsApi } from '../../../api/tickets';
import { ticketDetail, ticketEscalation } from '../../../test/fixtures';
import type { EscalationResult, TicketDetail } from '../../../types/api';
import { EscalationPanel } from './EscalationPanel';

vi.mock('../../../api/tickets', () => ({
  ticketsApi: { escalate: vi.fn() },
}));

const escalate = vi.mocked(ticketsApi.escalate);

const success: EscalationResult = {
  ticket: ticketDetail({ priority: 'High' }),
  escalation: ticketEscalation(),
};

function renderPanel(ticket: TicketDetail = ticketDetail()) {
  const onEscalated = vi.fn();
  render(<EscalationPanel ticket={ticket} onEscalated={onEscalated} />);
  return onEscalated;
}

async function fillIn(reason: string, actor = 'Priya Nair') {
  const user = userEvent.setup();
  const reasonBox = screen.getByLabelText('Reason');
  await user.clear(reasonBox);
  if (reason) {
    await user.click(reasonBox);
    await user.paste(reason);
  }
  const actorBox = screen.getByLabelText('Escalated by');
  await user.clear(actorBox);
  if (actor) {
    await user.type(actorBox, actor);
  }
  return user;
}

describe('EscalationPanel', () => {
  beforeEach(() => {
    window.localStorage.clear();
    escalate.mockResolvedValue(success);
  });

  afterEach(() => vi.clearAllMocks());

  describe('availability', () => {
    it('shows the current and the next priority for an open ticket', () => {
      renderPanel(ticketDetail({ priority: 'Medium' }));

      expect(screen.getByRole('button', { name: 'Escalate to High' })).toBeEnabled();
      expect(screen.getByText('Medium')).toBeInTheDocument();
      expect(screen.getByText('High')).toBeInTheDocument();
    });

    it.each<[Partial<TicketDetail>, string]>([
      [{ priority: 'Critical' }, 'This ticket is already Critical, the highest priority.'],
      [{ status: 'Resolved' }, 'Resolved tickets cannot be escalated.'],
      [{ status: 'Closed', priority: 'Low' }, 'Closed tickets cannot be escalated.'],
    ])('is disabled, saying why, for %o', (overrides, why) => {
      renderPanel(ticketDetail(overrides));

      const button = screen.getByRole('button', { name: 'Escalate' });
      expect(button).toBeDisabled();
      expect(button).toHaveAccessibleDescription(why);
      expect(screen.getByLabelText('Reason')).toBeDisabled();
    });
  });

  describe('validation', () => {
    it.each([
      ['', 'A reason for the escalation is required.'],
      ['     ', 'A reason for the escalation is required.'],
      ['abcd', 'The reason must be between 5 and 500 characters.'],
      ['  abcd  ', 'The reason must be between 5 and 500 characters.'],
      ['x'.repeat(501), 'The reason must be between 5 and 500 characters.'],
    ])('rejects the reason %j without calling the API', async (reason, message) => {
      renderPanel();
      const user = await fillIn(reason);

      await user.click(screen.getByRole('button', { name: 'Escalate to High' }));

      expect(screen.getByText(message)).toBeInTheDocument();
      expect(screen.getByLabelText('Reason')).toHaveAttribute('aria-invalid', 'true');
      expect(escalate).not.toHaveBeenCalled();
    });

    it.each([['abcde'], ['x'.repeat(500)]])('accepts a reason at the length limit (%#)', async (reason) => {
      renderPanel();
      const user = await fillIn(reason);

      await user.click(screen.getByRole('button', { name: 'Escalate to High' }));

      expect(escalate).toHaveBeenCalledWith(12, { reason, escalatedBy: 'Priya Nair' });
    });

    it('requires the name of whoever escalates', async () => {
      renderPanel();
      const user = await fillIn('Customer is blocked', '');

      await user.click(screen.getByRole('button', { name: 'Escalate to High' }));

      expect(screen.getByText('Say who is escalating the ticket.')).toBeInTheDocument();
      expect(escalate).not.toHaveBeenCalled();
    });
  });

  describe('submitting', () => {
    it('sends the trimmed reason and actor, then reports the recorded change', async () => {
      const onEscalated = renderPanel();
      const user = await fillIn('   Customer is blocked   ', 'Priya Nair');

      await user.click(screen.getByRole('button', { name: 'Escalate to High' }));

      expect(escalate).toHaveBeenCalledWith(12, { reason: 'Customer is blocked', escalatedBy: 'Priya Nair' });
      expect(onEscalated).toHaveBeenCalledWith(success);
      expect(screen.getByRole('status')).toHaveTextContent('Escalated from Medium to High.');
      expect(screen.getByLabelText('Reason')).toHaveValue('');
    });

    it('shows a pending state and ignores further submissions until the API answers', async () => {
      let answer: (result: EscalationResult) => void = () => {};
      escalate.mockImplementation(() => new Promise((resolve) => (answer = resolve)));
      const onEscalated = renderPanel();
      const user = await fillIn('Customer is blocked');

      const button = screen.getByRole('button', { name: 'Escalate to High' });
      await user.click(button);
      await user.click(button);
      await user.keyboard('{Enter}');

      expect(escalate).toHaveBeenCalledTimes(1);
      expect(screen.getByRole('button', { name: 'Escalating...' })).toBeDisabled();
      expect(screen.getByLabelText('Reason')).toBeDisabled();

      answer(success);

      expect(await screen.findByRole('button', { name: 'Escalate to High' })).toBeEnabled();
      expect(onEscalated).toHaveBeenCalledTimes(1);
    });

    it('ignores a second form submission that arrives before the first one has finished', async () => {
      escalate.mockImplementation(() => new Promise(() => {}));
      renderPanel();
      await fillIn('Customer is blocked');

      // Submitting the form directly, as Enter in a text field does, bypasses the disabled button.
      const form = screen.getByRole('button', { name: 'Escalate to High' }).closest('form') as HTMLFormElement;
      fireEvent.submit(form);
      fireEvent.submit(form);

      expect(escalate).toHaveBeenCalledTimes(1);
    });

    it('remembers the actor for the next escalation', async () => {
      renderPanel();
      await fillIn('Customer is blocked', 'Priya Nair');

      expect(window.localStorage.getItem('northwind.escalatedBy')).toBe('Priya Nair');
    });
  });

  describe('API rejections', () => {
    it('shows a 409 and leaves the ticket as it was', async () => {
      escalate.mockRejectedValue(
        new ApiError(409, 'Ticket TCK-0012 is already Critical and cannot be escalated further.'),
      );
      const onEscalated = renderPanel();
      const user = await fillIn('Customer is blocked');

      await user.click(screen.getByRole('button', { name: 'Escalate to High' }));

      expect(await screen.findByRole('alert')).toHaveTextContent(
        'Ticket TCK-0012 is already Critical and cannot be escalated further.',
      );
      expect(onEscalated).not.toHaveBeenCalled();
      // Nothing is lost: the reason is still there to retry or copy.
      expect(screen.getByLabelText('Reason')).toHaveValue('Customer is blocked');
    });

    it('shows a 404', async () => {
      escalate.mockRejectedValue(new ApiError(404, "Ticket '12' was not found."));
      const onEscalated = renderPanel();
      const user = await fillIn('Customer is blocked');

      await user.click(screen.getByRole('button', { name: 'Escalate to High' }));

      expect(await screen.findByRole('alert')).toHaveTextContent("Ticket '12' was not found.");
      expect(onEscalated).not.toHaveBeenCalled();
    });

    it('puts a 400 from the API on the field it is about', async () => {
      escalate.mockRejectedValue(
        new ApiError(400, 'One or more validation errors occurred.', {
          status: 400,
          errors: { Reason: ['The reason must be between 5 and 500 characters.'] },
        }),
      );
      renderPanel();
      const user = await fillIn('Customer is blocked');

      await user.click(screen.getByRole('button', { name: 'Escalate to High' }));

      expect(await screen.findByText('The reason must be between 5 and 500 characters.')).toBeInTheDocument();
      expect(screen.getByLabelText('Reason')).toHaveAttribute('aria-invalid', 'true');
      expect(screen.getByRole('alert')).toHaveTextContent('One or more validation errors occurred.');
    });

    it('explains a failure that never reached the API', async () => {
      escalate.mockRejectedValue(new TypeError('Failed to fetch'));
      renderPanel();
      const user = await fillIn('Customer is blocked');

      await user.click(screen.getByRole('button', { name: 'Escalate to High' }));

      expect(await screen.findByRole('alert')).toHaveTextContent('Could not escalate the ticket. (Failed to fetch)');
    });
  });
});
