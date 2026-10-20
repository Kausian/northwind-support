import { sessionStore, type Session } from '../auth/sessionStore';

/** Starts a session as if the agent had just signed in; valid for an hour unless told otherwise. */
export function signInAs(fullName = 'Priya Nair', overrides: Partial<Session> = {}): Session {
  const session: Session = {
    accessToken: 'header.payload.signature',
    expiresAtUtc: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
    agent: { id: 3, fullName, email: 'priya.nair@northwind.example' },
    ...overrides,
  };

  sessionStore.start(session);
  return session;
}
