import type {
  PagedResult,
  TicketCreated,
  TicketDetail,
  TicketEscalation,
  TicketListItem,
} from '../types/api';

/** A list row with sensible defaults, so tests only state what they care about. */
export function ticketListItem(overrides: Partial<TicketListItem> = {}): TicketListItem {
  return {
    id: 12,
    reference: 'TCK-0012',
    title: 'Invoice total does not match the order',
    status: 'InProgress',
    priority: 'High',
    customer: { id: 2, name: 'Fabrikam Inc', tier: 'Standard' },
    category: { id: 2, name: 'Billing' },
    assignedAgent: { id: 4, fullName: 'Sara Lindqvist' },
    createdAtUtc: '2026-09-24T08:12:00Z',
    updatedAtUtc: '2026-09-25T10:03:00Z',
    dueAtUtc: '2026-09-24T16:12:00Z',
    resolvedAtUtc: null,
    slaStatus: 'Breached',
    ...overrides,
  };
}

export function page(items: TicketListItem[]): PagedResult<TicketListItem> {
  return {
    items,
    page: 1,
    pageSize: 20,
    totalCount: items.length,
    totalPages: 1,
  };
}

/** A ticket as the detail page loads it. */
export function ticketDetail(overrides: Partial<TicketDetail> = {}): TicketDetail {
  return {
    id: 12,
    reference: 'TCK-0012',
    title: 'Invoice total does not match the order',
    description: 'Invoice 5512 is EUR 240 higher than the confirmed order.',
    status: 'InProgress',
    priority: 'Medium',
    customer: { id: 2, name: 'Fabrikam Inc', tier: 'Standard', email: 'help@fabrikam.example', phone: null },
    category: { id: 2, name: 'Billing', requiresSpecialist: true },
    assignedAgent: { id: 4, fullName: 'Sara Lindqvist' },
    createdAtUtc: '2026-09-24T08:12:00Z',
    updatedAtUtc: '2026-09-25T10:03:00Z',
    dueAtUtc: '2026-09-25T08:12:00Z',
    resolvedAtUtc: null,
    slaStatus: 'WithinSla',
    ...overrides,
  };
}

/** One row of escalation history. */
export function ticketEscalation(overrides: Partial<TicketEscalation> = {}): TicketEscalation {
  return {
    id: 1,
    fromPriority: 'Medium',
    toPriority: 'High',
    fromAgent: { id: 4, fullName: 'Sara Lindqvist' },
    toAgent: { id: 4, fullName: 'Sara Lindqvist' },
    fromDueAtUtc: '2026-09-25T08:12:00Z',
    toDueAtUtc: '2026-09-24T18:00:00Z',
    reason: 'Customer cannot close their books',
    escalatedBy: 'Priya Nair',
    escalatedAtUtc: '2026-09-24T10:00:00Z',
    ...overrides,
  };
}

/** The response to raising a ticket, with triage's reasons. */
export function ticketCreated(overrides: Partial<TicketCreated> = {}): TicketCreated {
  return {
    ...ticketDetail({ id: 41, reference: 'TCK-0041', status: 'New', priority: 'Critical' }),
    triage: {
      priority: 'Security tickets are always Critical (requested Medium).',
      slaWindow: 'Critical priority: 4 h window, reduced to 2 h for a Premium customer.',
      assignment: 'Assigned to Priya Nair: fewest open tickets (4 of 8) among 1 eligible agent.',
    },
    ...overrides,
  };
}
