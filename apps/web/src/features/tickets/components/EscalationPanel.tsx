import { useRef, useState } from 'react';
import { ApiError, toErrorMessage } from '../../../api/client';
import { ticketsApi } from '../../../api/tickets';
import { Field } from '../../../components/Field';
import type { EscalationResult, TicketDetail } from '../../../types/api';
import {
  escalationBlocker,
  escalationLimits,
  nextPriority,
  validateEscalation,
  type EscalationErrors,
} from '../escalation';
import { useEscalationActor } from '../escalationActor';
import { PriorityBadge } from './TicketBadges';

interface EscalationPanelProps {
  ticket: TicketDetail;
  /** Called with the API's response once the escalation is recorded. */
  onEscalated: (result: EscalationResult) => void;
}

/**
 * Escalates a ticket one priority level (BR-7). Checks the same rules as the API before calling
 * it, refuses a second submission while one is in flight, and shows the API's own rejection -
 * including a 409 when the ticket changed underneath the user.
 */
export function EscalationPanel({ ticket, onEscalated }: EscalationPanelProps) {
  const actor = useEscalationActor();
  const [reason, setReason] = useState('');
  const [errors, setErrors] = useState<EscalationErrors>({});
  const [submitError, setSubmitError] = useState<string>();
  const [confirmation, setConfirmation] = useState<string>();
  const [isPending, setIsPending] = useState(false);

  // State updates land on the next render; the ref closes the gap a fast double click could use.
  const inFlight = useRef(false);

  const blocker = escalationBlocker(ticket);
  const next = nextPriority(ticket.priority);
  const reasonLength = reason.trim().length;

  const submit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();

    if (inFlight.current || blocker) {
      return;
    }

    const form = { reason };
    const validationErrors = validateEscalation(form);
    setErrors(validationErrors);
    setSubmitError(undefined);
    setConfirmation(undefined);

    if (Object.keys(validationErrors).length > 0) {
      return;
    }

    inFlight.current = true;
    setIsPending(true);

    try {
      // Only the reason: the API records the signed-in agent from the token.
      const result = await ticketsApi.escalate(ticket.id, { reason: form.reason.trim() });

      setReason('');
      setConfirmation(`Escalated from ${result.escalation.fromPriority} to ${result.escalation.toPriority}.`);
      onEscalated(result);
    } catch (caught) {
      if (caught instanceof ApiError && caught.status === 400) {
        setErrors(fromApiErrors(caught.fieldErrors));
      }

      setSubmitError(toErrorMessage(caught, 'Could not escalate the ticket.'));
    } finally {
      inFlight.current = false;
      setIsPending(false);
    }
  };

  return (
    <section className="card" aria-labelledby="escalation-heading">
      <h2 id="escalation-heading">Escalate</h2>

      {blocker || !next ? (
        <p className="field__hint" id="escalation-blocked">
          {blocker}
        </p>
      ) : (
        <p className="escalation__change">
          <PriorityBadge priority={ticket.priority} /> <span aria-hidden="true">→</span>
          <span className="visually-hidden"> to </span> <PriorityBadge priority={next} />
          <span className="field__hint">
            {' '}
            The due date restarts from now and the owner is re-checked.
          </span>
        </p>
      )}

      <form className="form" onSubmit={submit} noValidate>
        <Field
          id="escalation-reason"
          label="Reason"
          error={errors.reason}
          hint={`${reasonLength}/${escalationLimits.reasonMax} characters, at least ${escalationLimits.reasonMin}.`}
        >
          {(fieldProps) => (
            <textarea
              {...fieldProps}
              rows={3}
              value={reason}
              disabled={Boolean(blocker) || isPending}
              onChange={(event) => setReason(event.target.value)}
            />
          )}
        </Field>

        <p className="field__hint escalation__actor">
          Escalating as <strong>{actor}</strong>
        </p>

        {submitError && (
          <p className="field__error" role="alert">
            {submitError}
          </p>
        )}

        {confirmation && (
          <p className="field__hint" role="status">
            {confirmation}
          </p>
        )}

        <div className="button-row">
          <button
            type="submit"
            className="button button--primary"
            disabled={Boolean(blocker) || isPending}
            aria-describedby={blocker ? 'escalation-blocked' : undefined}
          >
            {isPending ? 'Escalating...' : next && !blocker ? `Escalate to ${next}` : 'Escalate'}
          </button>
        </div>
      </form>
    </section>
  );
}

/** The API names fields as C# does (Reason); the form uses camelCase. */
function fromApiErrors(fieldErrors: Record<string, string[]>): EscalationErrors {
  const reason = fieldErrors.Reason ?? fieldErrors.reason;
  return reason ? { reason: reason[0] } : {};
}
