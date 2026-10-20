import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { sessionStore } from '../auth/sessionStore';
import { signInAs } from '../test/session';
import { ApiError, request } from './client';

const fetchMock = vi.fn<typeof fetch>();

function respond(status: number, body: unknown = {}) {
  fetchMock.mockResolvedValueOnce(
    new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } }),
  );
}

function sentHeaders(): Record<string, string> {
  return (fetchMock.mock.calls[0][1]?.headers ?? {}) as Record<string, string>;
}

describe('request (API client)', () => {
  beforeEach(() => vi.stubGlobal('fetch', fetchMock));

  afterEach(() => {
    vi.unstubAllGlobals();
    fetchMock.mockReset();
  });

  it('attaches the signed-in agent\'s token to every call', async () => {
    signInAs('Priya Nair', { accessToken: 'the.jwt.token' });
    respond(200, []);

    await request('/tickets');

    expect(sentHeaders().Authorization).toBe('Bearer the.jwt.token');
  });

  it('sends no Authorization header when nobody is signed in', async () => {
    respond(200, []);

    await request('/auth/login', { method: 'POST' });

    expect(sentHeaders()).not.toHaveProperty('Authorization');
  });

  it('ends the session as expired when the API refuses the token (401)', async () => {
    signInAs();
    respond(401, { title: 'Unauthorized', status: 401, detail: 'Your session has expired. Sign in again.' });

    const error = await request('/tickets').catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).status).toBe(401);
    expect(sessionStore.get()).toBeUndefined();
    expect(sessionStore.endReason()).toBe('expired');
  });

  it('treats a 401 on a call made without a token as that call\'s answer only', async () => {
    sessionStore.end('signedOut');
    respond(401, { title: 'Unauthorized', status: 401, detail: 'Email or password is incorrect.' });

    const error = await request('/auth/login', { method: 'POST' }).catch((e: unknown) => e);

    expect((error as ApiError).message).toBe('Email or password is incorrect.');
    expect(sessionStore.endReason()).toBe('signedOut');
  });

  it('keeps the session on other failures', async () => {
    const session = signInAs();
    respond(409, { title: 'Conflict', status: 409, detail: 'Already Critical.' });

    await request('/tickets/1/escalate', { method: 'POST' }).catch(() => undefined);

    expect(sessionStore.get()).toBe(session);
  });
});
