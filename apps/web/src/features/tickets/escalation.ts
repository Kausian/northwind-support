import type { TicketDetail, TicketPriority } from '../../types/api';

/** The API's limits (TicketEscalation.ReasonMinLength and friends), so bad input never reaches it. */
export const escalationLimits = {
  reasonMin: 5,
  reasonMax: 500,
} as const;

const nextPriorities: Record<TicketPriority, TicketPriority | undefined> = {
  Low: 'Medium',
  Medium: 'High',
  High: 'Critical',
  Critical: undefined,
};

/** The priority an escalation would move the ticket to: one level up, or none from Critical. */
export function nextPriority(priority: TicketPriority): TicketPriority | undefined {
  return nextPriorities[priority];
}

/**
 * Why the ticket cannot be escalated, or undefined when it can. Mirrors the API's 409 rules so
 * the control can be disabled up front; the API stays the authority and its 409 is still shown.
 */
export function escalationBlocker(ticket: Pick<TicketDetail, 'status' | 'priority'>): string | undefined {
  if (ticket.status === 'Resolved' || ticket.status === 'Closed') {
    return `${ticket.status} tickets cannot be escalated.`;
  }

  if (ticket.priority === 'Critical') {
    return 'This ticket is already Critical, the highest priority.';
  }

  return undefined;
}

export interface EscalationForm {
  reason: string;
}

export type EscalationErrors = Partial<Record<keyof EscalationForm, string>>;

/** The same rules as the API's EscalateTicketRequestValidator, on trimmed input. */
export function validateEscalation(form: EscalationForm): EscalationErrors {
  const errors: EscalationErrors = {};
  const reason = form.reason.trim();

  if (reason.length === 0) {
    errors.reason = 'A reason for the escalation is required.';
  } else if (reason.length < escalationLimits.reasonMin || reason.length > escalationLimits.reasonMax) {
    errors.reason = `The reason must be between ${escalationLimits.reasonMin} and ${escalationLimits.reasonMax} characters.`;
  }

  return errors;
}
