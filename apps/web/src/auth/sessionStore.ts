import type { AgentProfile } from '../types/api';

/** The signed-in agent and the access token the API issued them. */
export interface Session {
  accessToken: string;
  expiresAtUtc: string;
  agent: AgentProfile;
}

/** Why the last session ended, so the sign-in page can say "your session expired". */
export type SessionEndReason = 'expired' | 'signedOut';

/**
 * The one place the access token lives (AU-10). Kept in `sessionStorage`: it survives a page
 * reload but not closing the tab, and is not shared with other tabs. Like any storage readable
 * from JavaScript it is exposed to an XSS bug, which is why the token is short-lived and React
 * escapes all output; an httpOnly cookie would be the production alternative (see README).
 */
const storageKey = 'northwind.session';

let current: Session | undefined = readStored();
let lastEndReason: SessionEndReason | undefined;
const listeners = new Set<() => void>();

export function isExpired(session: Session, now = Date.now()): boolean {
  return Date.parse(session.expiresAtUtc) <= now;
}

export const sessionStore = {
  get(): Session | undefined {
    return current;
  },

  endReason(): SessionEndReason | undefined {
    return lastEndReason;
  },

  start(session: Session) {
    current = session;
    lastEndReason = undefined;
    write(session);
    emit();
  },

  end(reason: SessionEndReason) {
    current = undefined;
    lastEndReason = reason;
    write(undefined);
    emit();
  },

  /** For useSyncExternalStore: tells React when the session starts or ends. */
  subscribe(listener: () => void): () => void {
    listeners.add(listener);
    return () => listeners.delete(listener);
  },
};

function emit() {
  listeners.forEach((listener) => listener());
}

function readStored(): Session | undefined {
  try {
    const raw = window.sessionStorage.getItem(storageKey);
    if (!raw) {
      return undefined;
    }

    const session = JSON.parse(raw) as Session;
    if (isExpired(session)) {
      window.sessionStorage.removeItem(storageKey);
      return undefined;
    }

    return session;
  } catch {
    return undefined;
  }
}

function write(session: Session | undefined) {
  try {
    if (session) {
      window.sessionStorage.setItem(storageKey, JSON.stringify(session));
    } else {
      window.sessionStorage.removeItem(storageKey);
    }
  } catch {
    // Storage unavailable (private mode, blocked): the session simply lasts until a reload.
  }
}
