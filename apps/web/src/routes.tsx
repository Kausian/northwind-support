import { createBrowserRouter, Navigate, type RouteObject } from 'react-router-dom';
import { App } from './App';
import { LoginPage } from './features/auth/LoginPage';
import { RequireAuth } from './features/auth/RequireAuth';
import { NotFoundPage } from './features/NotFoundPage';
import { CustomerDetailPage } from './features/customers/CustomerDetailPage';
import { CustomerListPage } from './features/customers/CustomerListPage';
import { CreateTicketPage } from './features/tickets/CreateTicketPage';
import { TicketDetailPage } from './features/tickets/TicketDetailPage';
import { TicketListPage } from './features/tickets/TicketListPage';

/** The route table, exported so tests can mount it in a memory router. */
export const routes: RouteObject[] = [
  // The only page open without signing in.
  { path: '/login', element: <LoginPage /> },
  {
    path: '/',
    element: (
      <RequireAuth>
        <App />
      </RequireAuth>
    ),
    children: [
      { index: true, element: <Navigate to="/tickets" replace /> },
      { path: 'tickets', element: <TicketListPage /> },
      { path: 'tickets/new', element: <CreateTicketPage /> },
      { path: 'tickets/:id', element: <TicketDetailPage /> },
      { path: 'customers', element: <CustomerListPage /> },
      { path: 'customers/:id', element: <CustomerDetailPage /> },
      { path: '*', element: <NotFoundPage /> },
    ],
  },
];

export const router = createBrowserRouter(routes);
