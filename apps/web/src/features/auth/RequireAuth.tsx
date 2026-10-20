import { Navigate, useLocation } from 'react-router-dom';
import { useSession, useSessionExpiry } from '../../auth/session';

interface RequireAuthProps {
  children: React.ReactNode;
}

/**
 * AU-11: renders its children only for a signed-in agent. Anyone else is sent to /login, which
 * brings them back here afterwards. Nothing protected is rendered - not even briefly - without
 * a session, and when the session ends (sign-out, expiry, a 401) the user is sent away at once.
 */
export function RequireAuth({ children }: RequireAuthProps) {
  const session = useSession();
  const location = useLocation();

  useSessionExpiry(session);

  if (!session) {
    return <Navigate to="/login" replace state={{ from: `${location.pathname}${location.search}` }} />;
  }

  return children;
}
