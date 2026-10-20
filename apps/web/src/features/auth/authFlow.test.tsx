import { act, render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { createMemoryRouter, RouterProvider } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { authApi } from '../../api/auth';
import { ApiError } from '../../api/client';
import { ticketsApi } from '../../api/tickets';
import { sessionStore } from '../../auth/sessionStore';
import { routes } from '../../routes';
import { page, ticketListItem } from '../../test/fixtures';
import { signInAs } from '../../test/session';

vi.mock('../../api/auth', () => ({ authApi: { login: vi.fn(), me: vi.fn() } }));
vi.mock('../../api/tickets', () => ({ ticketsApi: { getTickets: vi.fn() } }));
vi.mock('../../api/agents', () => ({ agentsApi: { getAgents: vi.fn().mockResolvedValue([]) } }));
vi.mock('../../api/categories', () => ({ categoriesApi: { getCategories: vi.fn().mockResolvedValue([]) } }));
vi.mock('../../api/customers', () => ({ customersApi: { getCustomers: vi.fn().mockResolvedValue([]) } }));

const login = vi.mocked(authApi.login);
const getTickets = vi.mocked(ticketsApi.getTickets);

const loginResponse = {
  accessToken: 'header.payload.signature',
  expiresAtUtc: new Date(Date.now() + 60 * 60 * 1000).toISOString(),
  agent: { id: 4, fullName: 'Sara Lindqvist', email: 'sara@northwind.example' },
};

function renderAt(path: string) {
  const router = createMemoryRouter(routes, { initialEntries: [path] });
  render(<RouterProvider router={router} />);
  return router;
}

async function signInWith(email: string, password: string) {
  const user = userEvent.setup();
  if (email) {
    await user.type(screen.getByLabelText('Email'), email);
  }
  if (password) {
    await user.type(screen.getByLabelText('Password'), password);
  }
  await user.click(screen.getByRole('button', { name: 'Sign in' }));
}

describe('authentication in the web app', () => {
  beforeEach(() => {
    getTickets.mockResolvedValue(page([ticketListItem()]));
  });

  afterEach(() => vi.clearAllMocks());

  describe('protected routes (AU-11)', () => {
    it.each(['/tickets', '/tickets/12', '/customers', '/'])('sends a signed-out visitor from %s to /login without rendering it', (path) => {
      const router = renderAt(path);

      expect(router.state.location.pathname).toBe('/login');
      expect(screen.getByRole('heading', { name: 'Sign in' })).toBeInTheDocument();
      expect(screen.queryByRole('heading', { name: 'Tickets' })).not.toBeInTheDocument();
      expect(getTickets).not.toHaveBeenCalled();
    });

    it('returns the agent to the page they wanted after signing in', async () => {
      login.mockResolvedValue(loginResponse);
      const router = renderAt('/customers?tier=Premium');

      await signInWith('sara@northwind.example', 'Local-Dev-Only-1!');

      expect(login).toHaveBeenCalledWith('sara@northwind.example', 'Local-Dev-Only-1!');
      expect(router.state.location.pathname).toBe('/customers');
      expect(router.state.location.search).toBe('?tier=Premium');
    });

    it('goes to the ticket list when sign-in was not prompted by another page', async () => {
      login.mockResolvedValue(loginResponse);
      const router = renderAt('/login');

      await signInWith('sara@northwind.example', 'Local-Dev-Only-1!');

      expect(router.state.location.pathname).toBe('/tickets');
      expect(await screen.findByRole('heading', { name: 'Tickets' })).toBeInTheDocument();
    });

    it('shows the signed-in agent and the protected page', async () => {
      signInAs('Sara Lindqvist');
      renderAt('/tickets');

      expect(await screen.findByRole('heading', { name: 'Tickets' })).toBeInTheDocument();
      expect(screen.getByText(/Signed in as/)).toHaveTextContent('Signed in as Sara Lindqvist');
    });
  });

  describe('login page (AU-9)', () => {
    it.each([
      ['', '', 'Enter your email address.', 'Enter your password.'],
      ['sara', 'x', 'Enter an email address like name@example.com.', undefined],
      ['sara@northwind.example', '', undefined, 'Enter your password.'],
    ])('validates %j / %j without calling the API', async (email, password, emailError, passwordError) => {
      renderAt('/login');

      await signInWith(email, password);

      if (emailError) {
        expect(screen.getByText(emailError)).toBeInTheDocument();
        expect(screen.getByLabelText('Email')).toHaveAttribute('aria-invalid', 'true');
      }
      if (passwordError) {
        expect(screen.getByText(passwordError)).toBeInTheDocument();
      }
      expect(login).not.toHaveBeenCalled();
    });

    it('labels its fields for assistive technology', () => {
      renderAt('/login');

      expect(screen.getByLabelText('Email')).toHaveAttribute('type', 'email');
      expect(screen.getByLabelText('Password')).toHaveAttribute('type', 'password');
      expect(screen.getByLabelText('Password')).toHaveAttribute('autocomplete', 'current-password');
    });

    it('shows a pending state while signing in', async () => {
      login.mockImplementation(() => new Promise(() => {}));
      renderAt('/login');

      await signInWith('sara@northwind.example', 'Local-Dev-Only-1!');

      expect(screen.getByRole('button', { name: 'Signing in...' })).toBeDisabled();
      expect(screen.getByLabelText('Email')).toBeDisabled();
    });

    it('shows the API\'s message when the credentials are refused, and stays signed out', async () => {
      login.mockRejectedValue(new ApiError(401, 'Email or password is incorrect.'));
      const router = renderAt('/login');

      await signInWith('sara@northwind.example', 'wrong-password');

      expect(await screen.findByRole('alert')).toHaveTextContent('Email or password is incorrect.');
      expect(screen.getByLabelText('Password')).toHaveValue('');
      expect(router.state.location.pathname).toBe('/login');
      expect(sessionStore.get()).toBeUndefined();
    });
  });

  describe('ending the session', () => {
    it('signs out from the header, clears the session and returns to /login (AU-12)', async () => {
      signInAs('Sara Lindqvist');
      window.sessionStorage.setItem('unrelated', 'kept');
      const router = renderAt('/tickets');

      await userEvent.click(await screen.findByRole('button', { name: 'Sign out' }));

      expect(router.state.location.pathname).toBe('/login');
      expect(sessionStore.get()).toBeUndefined();
      expect(window.sessionStorage.getItem('northwind.session')).toBeNull();
      expect(screen.queryByText(/Session has expired/i)).not.toBeInTheDocument();
    });

    it('sends the agent to /login, saying why, when an API call is refused with 401 (AU-13)', async () => {
      signInAs('Sara Lindqvist');
      const router = renderAt('/tickets');
      expect(await screen.findByRole('heading', { name: 'Tickets' })).toBeInTheDocument();

      // What the API client does on a 401 for a request that carried the token.
      act(() => sessionStore.end('expired'));

      expect(router.state.location.pathname).toBe('/login');
      expect(router.state.location.state).toEqual({ from: '/tickets' });
      expect(screen.getByRole('status')).toHaveTextContent('Your session has expired. Sign in again.');
    });

    it('ends the session by itself when the token expires', async () => {
      vi.useFakeTimers({ shouldAdvanceTime: true });
      try {
        signInAs('Sara Lindqvist', { expiresAtUtc: new Date(Date.now() + 5_000).toISOString() });
        const router = renderAt('/tickets');
        expect(router.state.location.pathname).toBe('/tickets');

        await act(async () => {
          await vi.advanceTimersByTimeAsync(5_000);
        });

        expect(router.state.location.pathname).toBe('/login');
        expect(sessionStore.endReason()).toBe('expired');
      } finally {
        vi.useRealTimers();
      }
    });

    it('does not restore an already expired session from storage', async () => {
      window.sessionStorage.setItem(
        'northwind.session',
        JSON.stringify({ ...loginResponse, expiresAtUtc: new Date(Date.now() - 1000).toISOString() }),
      );

      vi.resetModules();
      const { sessionStore: freshStore } = await import('../../auth/sessionStore');

      expect(freshStore.get()).toBeUndefined();
      expect(window.sessionStorage.getItem('northwind.session')).toBeNull();
    });
  });
});
