import { useState } from 'react';
import { Navigate, useLocation, useNavigate } from 'react-router-dom';
import { toErrorMessage } from '../../api/client';
import { signIn, useSession } from '../../auth/session';
import { sessionStore } from '../../auth/sessionStore';
import { Field } from '../../components/Field';

interface LoginErrors {
  email?: string;
  password?: string;
}

/** Where to go after signing in: the page that sent the user here, or the ticket list. */
function returnPath(state: unknown): string {
  const from = (state as { from?: unknown } | null)?.from;
  return typeof from === 'string' && from.startsWith('/') && from !== '/login' ? from : '/tickets';
}

function validate(email: string, password: string): LoginErrors {
  const errors: LoginErrors = {};

  if (email.trim() === '') {
    errors.email = 'Enter your email address.';
  } else if (!/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(email.trim())) {
    errors.email = 'Enter an email address like name@example.com.';
  }

  if (password === '') {
    errors.password = 'Enter your password.';
  }

  return errors;
}

/** AU-9: sign-in with email and password, then back to the page the agent wanted. */
export function LoginPage() {
  const session = useSession();
  const location = useLocation();
  const navigate = useNavigate();

  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [errors, setErrors] = useState<LoginErrors>({});
  const [submitError, setSubmitError] = useState<string>();
  const [isPending, setIsPending] = useState(false);

  const target = returnPath(location.state);
  const expired = sessionStore.endReason() === 'expired';

  if (session) {
    return <Navigate to={target} replace />;
  }

  const submit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();

    if (isPending) {
      return;
    }

    const validationErrors = validate(email, password);
    setErrors(validationErrors);
    setSubmitError(undefined);

    if (Object.keys(validationErrors).length > 0) {
      return;
    }

    setIsPending(true);

    try {
      await signIn(email.trim(), password);
      void navigate(target, { replace: true });
    } catch (caught) {
      // The API's 401 message is deliberately the same for every wrong combination.
      setSubmitError(toErrorMessage(caught, 'Could not sign in.'));
      setPassword('');
      setIsPending(false);
    }
  };

  return (
    <main className="login">
      <form className="card form login__form" onSubmit={submit} noValidate aria-labelledby="login-heading">
        <h1 id="login-heading">Sign in</h1>
        <p className="field__hint">Northwind Support Desk - agents only.</p>

        {expired && !submitError && (
          <p className="notice" role="status">
            Your session has expired. Sign in again.
          </p>
        )}

        <Field id="login-email" label="Email" error={errors.email}>
          {(fieldProps) => (
            <input
              {...fieldProps}
              type="email"
              autoComplete="username"
              value={email}
              disabled={isPending}
              onChange={(event) => setEmail(event.target.value)}
            />
          )}
        </Field>

        <Field id="login-password" label="Password" error={errors.password}>
          {(fieldProps) => (
            <input
              {...fieldProps}
              type="password"
              autoComplete="current-password"
              value={password}
              disabled={isPending}
              onChange={(event) => setPassword(event.target.value)}
            />
          )}
        </Field>

        {submitError && (
          <p className="field__error" role="alert">
            {submitError}
          </p>
        )}

        <div className="button-row">
          <button type="submit" className="button button--primary" disabled={isPending}>
            {isPending ? 'Signing in...' : 'Sign in'}
          </button>
        </div>
      </form>
    </main>
  );
}
