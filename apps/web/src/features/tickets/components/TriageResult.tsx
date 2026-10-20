import { formatDateTime, formatRelativeToNow } from '../../../lib/format';
import type { TicketCreated } from '../../../types/api';
import { PriorityBadge } from './TicketBadges';

interface TriageResultProps {
  ticket: TicketCreated;
}

/** What automatic triage decided for a ticket that was just raised, and why (from the API). */
export function TriageResult({ ticket }: TriageResultProps) {
  return (
    <dl className="triage" aria-label="Triage result">
      <dt>Priority</dt>
      <dd>
        <PriorityBadge priority={ticket.priority} />
        <span className="triage__reason">{ticket.triage.priority}</span>
      </dd>

      <dt>Due</dt>
      <dd>
        {ticket.dueAtUtc ? (
          <>
            {formatDateTime(ticket.dueAtUtc)} ({formatRelativeToNow(ticket.dueAtUtc)})
          </>
        ) : (
          'No due date'
        )}
        <span className="triage__reason">{ticket.triage.slaWindow}</span>
      </dd>

      <dt>Assigned to</dt>
      <dd>
        {ticket.assignedAgent?.fullName ?? <strong>Unassigned</strong>}
        <span className="triage__reason">{ticket.triage.assignment}</span>
      </dd>
    </dl>
  );
}
