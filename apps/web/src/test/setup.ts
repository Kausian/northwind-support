import '@testing-library/jest-dom/vitest';
import { afterEach } from 'vitest';
import { sessionStore } from '../auth/sessionStore';

// Every test starts signed out; tests that need an agent call signInAs().
afterEach(() => {
  sessionStore.end('signedOut');
  window.sessionStorage.clear();
});
