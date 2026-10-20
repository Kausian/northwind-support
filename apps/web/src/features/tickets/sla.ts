import type { BadgeTone } from '../../components/Badge';
import type { SlaStatus } from '../../types/api';

interface SlaPresentation {
  /** What the status is called on screen, and in the filter. */
  label: string;
  /** A symbol per state, so the states differ in shape as well as colour. */
  symbol: string;
  tone: BadgeTone;
}

/**
 * How each SLA status the API reports is shown. The status itself is always worked out by the
 * API (BR-8); the web app only presents it.
 */
export const slaPresentation: Record<SlaStatus, SlaPresentation> = {
  WithinSla: { label: 'Within SLA', symbol: '●', tone: 'info' },
  AtRisk: { label: 'At risk', symbol: '▲', tone: 'warning' },
  Breached: { label: 'Breached', symbol: '✕', tone: 'danger' },
  Met: { label: 'Met', symbol: '✓', tone: 'success' },
  NotApplicable: { label: 'No SLA', symbol: '–', tone: 'neutral' },
};

/** Every status, in the order the filter offers them. */
export const slaStatuses: SlaStatus[] = ['WithinSla', 'AtRisk', 'Breached', 'Met', 'NotApplicable'];
