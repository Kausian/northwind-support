import { Badge } from '../../../components/Badge';
import { formatDateTime } from '../../../lib/format';
import type { SlaStatus } from '../../../types/api';
import { slaPresentation } from '../sla';

interface SlaIndicatorProps {
  slaStatus: SlaStatus;
  dueAtUtc: string | null;
}

/**
 * Where a ticket stands against its SLA, and when it is due - the one SLA display used by both
 * the ticket list and the detail page. The state is carried by its text and symbol, not only
 * by colour, and is announced as "SLA: At risk".
 */
export function SlaIndicator({ slaStatus, dueAtUtc }: SlaIndicatorProps) {
  const { label, symbol, tone } = slaPresentation[slaStatus];

  return (
    <div className="sla-cell" data-sla-status={slaStatus}>
      <Badge tone={tone}>
        <span aria-hidden="true">{symbol} </span>
        <span className="visually-hidden">SLA: </span>
        {label}
      </Badge>
      <span className="sla-cell__due">{dueAtUtc ? `Due ${formatDateTime(dueAtUtc)}` : 'No due date'}</span>
    </div>
  );
}
