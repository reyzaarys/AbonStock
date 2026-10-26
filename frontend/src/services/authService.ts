import { apiClient } from '../lib/apiClient.ts';
import type { AuthUser, LoginRequest, LoginResponse } from '../types';

export const authService = {
  /**
   * Submits credentials to the backend login endpoint.
   * Does not require prior authentication.
   */
  login: (credentials: LoginRequest, signal?: AbortSignal): Promise<LoginResponse> =>
    apiClient.post<LoginResponse>('/api/auth/login', credentials, { signal }),

  /**
   * Retrieves the current authenticated user profile using the active JWT Bearer token.
   */
  getCurrentUser: (signal?: AbortSignal): Promise<AuthUser> =>
    apiClient.get<AuthUser>('/api/auth/me', { signal }),
};
