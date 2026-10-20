import { useEffect, useSyncExternalStore } from 'react';
import { authApi } from '../api/auth';
import { sessionStore, type Session } from './sessionStore';

/** The current session, re-rendering when the agent signs in or out (or the session ends). */
export function useSession(): Session | undefined {
  return useSyncExternalStore(sessionStore.subscribe, sessionStore.get);
}

/** Signs in through the API; throws the API's error (a 401 for wrong credentials) unchanged. */
export async function signIn(email: string, password: string): Promise<Session> {
  const response = await authApi.login(email, password);
  const session: Session = {
    accessToken: response.accessToken,
    expiresAtUtc: response.expiresAtUtc,
    agent: response.agent,
  };

  sessionStore.start(session);
  return session;
}

export function signOut() {
  sessionStore.end('signedOut');
}

/**
 * Ends the session the moment the token expires, rather than waiting for the next request to
 * be refused (AU-13 covers that case too).
 */
export function useSessionExpiry(session: Session | undefined) {
  useEffect(() => {
    if (!session) {
      return undefined;
    }

    const remaining = Date.parse(session.expiresAtUtc) - Date.now();
    const handle = window.setTimeout(() => sessionStore.end('expired'), Math.max(remaining, 0));

    return () => window.clearTimeout(handle);
  }, [session]);
}
