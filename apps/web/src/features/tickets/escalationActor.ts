import { useSession } from '../../auth/session';

/**
 * Who is escalating a ticket, for display: the signed-in agent. It is shown, never sent - the
 * API takes the actor from the access token (AU-8), so it cannot be typed or spoofed here.
 */
export function useEscalationActor(): string {
  return useSession()?.agent.fullName ?? '';
}
