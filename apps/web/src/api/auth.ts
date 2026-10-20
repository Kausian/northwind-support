import type { AgentProfile, LoginResponse } from '../types/api';
import { request } from './client';

export const authApi = {
  login: (email: string, password: string) =>
    request<LoginResponse>('/auth/login', {
      method: 'POST',
      body: JSON.stringify({ email, password }),
    }),

  me: () => request<AgentProfile>('/auth/me'),
};
