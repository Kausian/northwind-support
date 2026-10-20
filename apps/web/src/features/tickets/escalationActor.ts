import { useCallback, useState } from 'react';

/**
 * Who is escalating a ticket. **Temporary, until sign-in (Task 3):** the API still takes
 * `escalatedBy` from the request, so the user types their name and it is remembered in this
 * browser for convenience. Task 3 replaces this one hook with the signed-in agent's name (and the
 * API reads the actor from the token), so nothing else in the escalation UI has to change.
 */
const storageKey = 'northwind.escalatedBy';

export interface EscalationActor {
  actor: string;
  setActor: (actor: string) => void;
  /** False once the actor comes from the signed-in user rather than from the form. */
  isEditable: boolean;
}

export function useEscalationActor(): EscalationActor {
  const [actor, setActorState] = useState(readStored);

  const setActor = useCallback((value: string) => {
    setActorState(value);
    store(value);
  }, []);

  return { actor, setActor, isEditable: true };
}

// Storage can be unavailable (private windows, blocked site data); the form still works without it.
function readStored(): string {
  try {
    return window.localStorage.getItem(storageKey) ?? '';
  } catch {
    return '';
  }
}

function store(value: string) {
  try {
    window.localStorage.setItem(storageKey, value);
  } catch {
    // Only a convenience: forgetting the name is harmless.
  }
}
