import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { emptyFilters } from '../features/tickets/hooks/useTicketList';
import { page, ticketEscalation } from '../test/fixtures';
import { ApiError } from './client';
import { ticketsApi } from './tickets';

const fetchMock = vi.fn<typeof fetch>();

function respond(status: number, body: unknown) {
  fetchMock.mockResolvedValueOnce(
    new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }),
  );
}

describe('ticketsApi', () => {
  beforeEach(() => vi.stubGlobal('fetch', fetchMock));

  afterEach(() => {
    vi.unstubAllGlobals();
    fetchMock.mockReset();
  });

  it('sends slaStatus with the other filters', async () => {
    respond(200, page([]));

    await ticketsApi.getTickets({ ...emptyFilters, slaStatus: 'AtRisk', priority: 'High', search: ' printer ', page: 2 });

    const url = new URL(String(fetchMock.mock.calls[0][0]), 'http://localhost');
    expect(url.pathname).toBe('/api/tickets');
    expect(url.searchParams.get('slaStatus')).toBe('AtRisk');
    expect(url.searchParams.get('priority')).toBe('High');
    expect(url.searchParams.get('search')).toBe('printer');
    expect(url.searchParams.get('page')).toBe('2');
  });

  it('leaves slaStatus out when no SLA state is chosen', async () => {
    respond(200, page([]));

    await ticketsApi.getTickets(emptyFilters);

    expect(String(fetchMock.mock.calls[0][0])).not.toContain('slaStatus');
  });

  it('posts an escalation to the ticket and returns the API response', async () => {
    respond(200, { ticket: { id: 12 }, escalation: ticketEscalation() });

    const result = await ticketsApi.escalate(12, { reason: 'Customer is blocked', escalatedBy: 'Priya' });

    const [url, init] = fetchMock.mock.calls[0];
    expect(url).toBe('/api/tickets/12/escalate');
    expect(init?.method).toBe('POST');
    expect(JSON.parse(String(init?.body))).toEqual({ reason: 'Customer is blocked', escalatedBy: 'Priya' });
    expect(result.escalation.toPriority).toBe('High');
  });

  it('turns a 409 into an ApiError carrying the API message', async () => {
    respond(409, { title: 'Conflict', status: 409, detail: 'Ticket TCK-0012 is already Critical and cannot be escalated further.' });

    const error = await ticketsApi.escalate(12, { reason: 'Customer is blocked', escalatedBy: 'Priya' }).catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(409);
    expect((error as ApiError).message).toBe('Ticket TCK-0012 is already Critical and cannot be escalated further.');
  });

  it('reads the escalation history of a ticket', async () => {
    respond(200, [ticketEscalation()]);

    const history = await ticketsApi.getEscalations(12);

    expect(fetchMock.mock.calls[0][0]).toBe('/api/tickets/12/escalations');
    expect(history).toHaveLength(1);
  });
});
