import React, {
  createContext,
  useContext,
  useState,
  useEffect,
  useCallback,
  useRef,
  useMemo,
} from 'react';
import type { AuthUser, LoginRequest, LoginResponse, UserRole } from '../types';
import { authService } from '../services';
import {
  setTokenProvider,
  clearTokenProvider,
  setUnauthorizedHandler,
  clearUnauthorizedHandler,
} from '../lib/apiClient';

export interface AuthContextType {
  user: AuthUser | null;
  token: string | null;
  isAuthenticated: boolean;
  isLoading: boolean;
  login: (credentials: LoginRequest) => Promise<LoginResponse>;
  logout: () => void;
  hasRole: (role: UserRole | UserRole[]) => boolean;
  isAdmin: boolean;
  isStaff: boolean;
}

const TOKEN_KEY = 'abonstock_auth_token';
const USER_KEY = 'abonstock_auth_user';

const AuthContext = createContext<AuthContextType | undefined>(undefined);

export const AuthProvider: React.FC<{ children: React.ReactNode }> = ({ children }) => {
  const [token, setToken] = useState<string | null>(() => {
    try {
      return typeof window !== 'undefined' ? window.sessionStorage.getItem(TOKEN_KEY) : null;
    } catch {
      return null;
    }
  });

  const [user, setUser] = useState<AuthUser | null>(() => {
    try {
      if (typeof window === 'undefined') return null;
      const rawUser = window.sessionStorage.getItem(USER_KEY);
      return rawUser ? (JSON.parse(rawUser) as AuthUser) : null;
    } catch {
      return null;
    }
  });

  const [isLoading, setIsLoading] = useState<boolean>(true);

  // Keep a ref to the current token so apiClient's synchronous tokenProvider can read it immediately
  const tokenRef = useRef<string | null>(token);
  tokenRef.current = token;

  const logout = useCallback(() => {
    try {
      if (typeof window !== 'undefined') {
        window.sessionStorage.removeItem(TOKEN_KEY);
        window.sessionStorage.removeItem(USER_KEY);
      }
    } catch {
      // Ignore storage errors during teardown
    }
    setToken(null);
    setUser(null);
  }, []);

  // Configure apiClient token provider and 401 unauthorized handler
  useEffect(() => {
    setTokenProvider(() => tokenRef.current);
    setUnauthorizedHandler(() => {
      logout();
    });

    return () => {
      clearTokenProvider();
      clearUnauthorizedHandler();
    };
  }, [logout]);

  // Session recovery and token validation on initial mount
  useEffect(() => {
    let isCancelled = false;

    const restoreSession = async () => {
      const storedToken = typeof window !== 'undefined' ? window.sessionStorage.getItem(TOKEN_KEY) : null;

      if (!storedToken) {
        setIsLoading(false);
        return;
      }

      try {
        // Authoritative verification with backend GET /api/auth/me
        const profile = await authService.getCurrentUser();
        if (!isCancelled) {
          setUser(profile);
          try {
            window.sessionStorage.setItem(USER_KEY, JSON.stringify(profile));
          } catch {
            // Ignore storage quota/permission errors
          }
        }
      } catch {
        // If token validation fails (expired or revoked), purge stored session
        if (!isCancelled) {
          logout();
        }
      } finally {
        if (!isCancelled) {
          setIsLoading(false);
        }
      }
    };

    restoreSession();

    return () => {
      isCancelled = true;
    };
  }, [logout]);

  const login = useCallback(
    async (credentials: LoginRequest): Promise<LoginResponse> => {
      const response = await authService.login(credentials);

      try {
        if (typeof window !== 'undefined') {
          window.sessionStorage.setItem(TOKEN_KEY, response.token);
          window.sessionStorage.setItem(USER_KEY, JSON.stringify(response.user));
        }
      } catch {
        // Ignore storage failures
      }

      tokenRef.current = response.token;
      setToken(response.token);
      setUser(response.user);

      return response;
    },
    []
  );

  const hasRole = useCallback(
    (role: UserRole | UserRole[]): boolean => {
      if (!user) return false;
      if (Array.isArray(role)) {
        return role.includes(user.role);
      }
      return user.role === role;
    },
    [user]
  );

  const value = useMemo<AuthContextType>(
    () => ({
      user,
      token,
      isAuthenticated: Boolean(token && user),
      isLoading,
      login,
      logout,
      hasRole,
      isAdmin: user?.role === 'Admin',
      isStaff: user?.role === 'Staff',
    }),
    [user, token, isLoading, login, logout, hasRole]
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
};

export const useAuth = (): AuthContextType => {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
};
