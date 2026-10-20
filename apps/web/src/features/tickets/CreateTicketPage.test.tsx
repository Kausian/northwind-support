import { render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ticketsApi } from '../../api/tickets';
import { formatDateTime } from '../../lib/format';
import { ticketCreated } from '../../test/fixtures';
import { CreateTicketPage } from './CreateTicketPage';

vi.mock('../../api/tickets', () => ({ ticketsApi: { createTicket: vi.fn() } }));
vi.mock('../../api/agents', () => ({ agentsApi: { getAgents: vi.fn().mockResolvedValue([]) } }));
vi.mock('../../api/categories', () => ({
  categoriesApi: {
    getCategories: vi.fn().mockResolvedValue([
      { id: 5, name: 'Security', isActive: true, requiresSpecialist: true, forcesCriticalPriority: true },
    ]),
  },
}));
vi.mock('../../api/customers', () => ({
  customersApi: {
    getCustomers: vi.fn().mockResolvedValue([
      { id: 1, name: 'Contoso Ltd', email: 'it@contoso.example', phone: null, tier: 'Premium', openTicketCount: 0 },
    ]),
  },
}));

const createTicket = vi.mocked(ticketsApi.createTicket);

async function raiseTicket() {
  render(
    <MemoryRouter>
      <CreateTicketPage />
    </MemoryRouter>,
  );

  const user = userEvent.setup();
  await user.type(await screen.findByLabelText('Title'), 'Possible data exposure');
  await user.type(screen.getByLabelText('Description'), 'A shared link opens without its password.');
  await user.selectOptions(screen.getByLabelText('Customer'), 'Contoso Ltd (Premium)');
  await user.selectOptions(screen.getByLabelText('Category'), 'Security');
  await user.selectOptions(screen.getByLabelText('Requested priority'), 'Medium');
  await user.click(screen.getByRole('button', { name: 'Raise ticket' }));
}

describe('CreateTicketPage triage result', () => {
  afterEach(() => vi.clearAllMocks());

  describe('when an agent was assigned', () => {
    beforeEach(() =>
      createTicket.mockResolvedValue(
        ticketCreated({ assignedAgent: { id: 3, fullName: 'Priya Nair' }, dueAtUtc: '2026-09-24T10:12:00Z' }),
      ),
    );

    it('shows the priority, due date and owner triage chose, with the API reasons', async () => {
      await raiseTicket();

      const triage = within(await screen.findByLabelText('Triage result'));

      expect(screen.getByText(/Raised/)).toHaveTextContent('Raised TCK-0041 for Fabrikam Inc.');
      expect(triage.getByText('Critical')).toBeInTheDocument();
      expect(triage.getByText('Security tickets are always Critical (requested Medium).')).toBeInTheDocument();
      expect(
        triage.getByText((_, element) =>
          element?.tagName === 'DD' && (element.textContent ?? '').startsWith(formatDateTime('2026-09-24T10:12:00Z')),
        ),
      ).toBeInTheDocument();
      expect(triage.getByText('Critical priority: 4 h window, reduced to 2 h for a Premium customer.')).toBeInTheDocument();
      expect(triage.getByText('Priya Nair')).toBeInTheDocument();
      expect(triage.getByText(/Assigned to Priya Nair: fewest open tickets/)).toBeInTheDocument();
      expect(createTicket).toHaveBeenCalledWith(expect.objectContaining({ requestedPriority: 'Medium' }));
    });

    it('still offers to open the new ticket or raise another', async () => {
      await raiseTicket();

      expect(await screen.findByRole('button', { name: 'Open TCK-0041' })).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'Raise another' })).toBeInTheDocument();
    });
  });

  it('says plainly when nobody could be assigned, and why', async () => {
    createTicket.mockResolvedValue(
      ticketCreated({
        assignedAgent: null,
        triage: {
          priority: 'Security tickets are always Critical (requested Medium).',
          slaWindow: 'Critical priority: 4 h window, reduced to 2 h for a Premium customer.',
          assignment: 'Unassigned: no active Security specialist is below their open-ticket limit.',
        },
      }),
    );

    await raiseTicket();

    const triage = within(await screen.findByLabelText('Triage result'));

    expect(triage.getByText('Unassigned')).toBeInTheDocument();
    expect(
      triage.getByText('Unassigned: no active Security specialist is below their open-ticket limit.'),
    ).toBeInTheDocument();
  });
});
