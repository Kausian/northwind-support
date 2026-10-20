import { EmptyState } from '../../../components/EmptyState';
import { ErrorMessage } from '../../../components/ErrorMessage';
import { Spinner } from '../../../components/Spinner';
import { formatDateTime } from '../../../lib/format';
import type { AgentSummary, TicketEscalation } from '../../../types/api';
import { PriorityBadge } from './TicketBadges';

interface EscalationHistoryProps {
  escalations?: TicketEscalation[];
  isLoading: boolean;
  error?: string;
  onRetry: () => void;
}

/** Every escalation of the ticket, newest first as the API returns them. */
export function EscalationHistory({ escalations, isLoading, error, onRetry }: EscalationHistoryProps) {
  return (
    <section className="card" aria-labelledby="escalation-history-heading" aria-busy={isLoading}>
      <h2 id="escalation-history-heading">Escalation history</h2>

      {error && <ErrorMessage message={error} onRetry={onRetry} />}

      {isLoading && !escalations && <Spinner label="Loading escalation history" />}

      {escalations && escalations.length === 0 && (
        <EmptyState title="Not escalated yet" description="Escalations will be listed here, newest first." />
      )}

      {escalations && escalations.length > 0 && (
        <ol className="history">
          {escalations.map((escalation) => (
            <li key={escalation.id} className="history__entry">
              <p className="history__meta">
                <time dateTime={escalation.escalatedAtUtc}>{formatDateTime(escalation.escalatedAtUtc)}</time>
                {' by '}
                <strong>{escalation.escalatedBy}</strong>
              </p>

              <dl className="history__changes">
                <div>
                  <dt>Priority</dt>
                  <dd>
                    <PriorityBadge priority={escalation.fromPriority} /> <span aria-hidden="true">→</span>
                    <span className="visually-hidden"> to </span> <PriorityBadge priority={escalation.toPriority} />
                  </dd>
                </div>
                <div>
                  <dt>Agent</dt>
                  <dd>
                    {agentName(escalation.fromAgent)} <span aria-hidden="true">→</span>
                    <span className="visually-hidden"> to </span> {agentName(escalation.toAgent)}
                  </dd>
                </div>
                <div>
                  <dt>Due</dt>
                  <dd>
                    {formatDateTime(escalation.fromDueAtUtc)} <span aria-hidden="true">→</span>
                    <span className="visually-hidden"> to </span> {formatDateTime(escalation.toDueAtUtc)}
                  </dd>
                </div>
              </dl>

              <p className="history__reason">{escalation.reason}</p>
            </li>
          ))}
        </ol>
      )}
    </section>
  );
}

function agentName(agent: AgentSummary | null): string {
  return agent?.fullName ?? 'Unassigned';
}
