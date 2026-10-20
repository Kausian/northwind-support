import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ticketsApi } from '../../api/tickets';
import { ticketDetail, ticketEscalation } from '../../test/fixtures';
import { signInAs } from '../../test/session';
import { TicketDetailPage } from './TicketDetailPage';

vi.mock('../../api/tickets', () => ({
  ticketsApi: { getTicket: vi.fn(), getEscalations: vi.fn(), escalate: vi.fn() },
}));
vi.mock('../../api/agents', () => ({ agentsApi: { getAgents: vi.fn().mockResolvedValue([]) } }));
vi.mock('../../api/categories', () => ({ categoriesApi: { getCategories: vi.fn().mockResolvedValue([]) } }));
vi.mock('../../api/customers', () => ({ customersApi: { getCustomers: vi.fn().mockResolvedValue([]) } }));

const api = vi.mocked(ticketsApi);

function renderPage() {
  render(
    <MemoryRouter initialEntries={['/tickets/12']}>
      <Routes>
        <Route path="/tickets/:id" element={<TicketDetailPage />} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('TicketDetailPage escalation', () => {
  beforeEach(() => {
    signInAs('Priya Nair');
    api.getTicket.mockResolvedValue(ticketDetail({ priority: 'Medium' }));
    api.getEscalations.mockResolvedValueOnce([]);
  });

  afterEach(() => vi.clearAllMocks());

  it('refreshes the ticket and its history in place after escalating', async () => {
    const escalated = ticketDetail({ priority: 'High', dueAtUtc: '2026-09-24T18:00:00Z' });
    const recorded = ticketEscalation({ reason: 'Customer cannot close their books' });
    api.escalate.mockResolvedValue({ ticket: escalated, escalation: recorded });
    api.getEscalations.mockResolvedValueOnce([recorded]);

    renderPage();

    const history = await screen.findByRole('region', { name: 'Escalation history' });
    expect(await within(history).findByText('Not escalated yet')).toBeInTheDocument();

    const user = userEvent.setup();
    await user.type(screen.getByLabelText('Reason'), 'Customer cannot close their books');
    await user.click(screen.getByRole('button', { name: 'Escalate to High' }));

    // The history is fetched again and shows the new row.
    expect(await within(history).findByText('Customer cannot close their books')).toBeInTheDocument();
    expect(api.getEscalations).toHaveBeenCalledTimes(2);

    // The ticket is taken from the escalation response: now High, so the next step is Critical.
    expect(screen.getByRole('button', { name: 'Escalate to Critical' })).toBeEnabled();
    const summaryPriority = document.querySelector('.summary dd');
    expect(summaryPriority).toHaveTextContent('High');

    // No reload of the page or the ticket was needed for any of that.
    expect(api.getTicket).toHaveBeenCalledTimes(1);
  });

  it('keeps the ticket and history as they were when the API refuses with a 409', async () => {
    const { ApiError } = await import('../../api/client');
    api.escalate.mockRejectedValue(new ApiError(409, 'Ticket TCK-0012 is Resolved and cannot be escalated.'));

    renderPage();
    await screen.findByText('Not escalated yet');

    const user = userEvent.setup();
    await user.type(screen.getByLabelText('Reason'), 'Customer is blocked');
    await user.click(screen.getByRole('button', { name: 'Escalate to High' }));

    expect(await screen.findByText('Ticket TCK-0012 is Resolved and cannot be escalated.')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Escalate to High' })).toBeInTheDocument();
    expect(api.getEscalations).toHaveBeenCalledTimes(1);
  });
});
