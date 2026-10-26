import type { ProblemDetails } from '../types/common';

/**
 * Custom error class wrapping API and network failures.
 */
export class ApiError extends Error {
  readonly status: number;
  readonly problem?: ProblemDetails;
  readonly isNetworkError: boolean;

  constructor(
    message: string,
    status: number,
    problem?: ProblemDetails,
    isNetworkError = false
  ) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.problem = problem;
    this.isNetworkError = isNetworkError;
    Object.setPrototypeOf(this, new.target.prototype);
  }
}

export interface RequestOptions extends Omit<RequestInit, 'body'> {
  params?: Record<string, unknown>;
  body?: unknown;
}

const DEFAULT_BASE_URL = 'http://localhost:5299';

let tokenProvider: (() => string | null) | null = null;
let unauthorizedHandler: (() => void) | null = null;

/**
 * Registers a dynamic provider for the JWT Bearer token.
 */
export const setTokenProvider = (provider: () => string | null): void => {
  tokenProvider = provider;
};

/**
 * Clears the registered token provider.
 */
export const clearTokenProvider = (): void => {
  tokenProvider = null;
};

/**
 * Registers a handler called when a protected endpoint returns HTTP 401.
 */
export const setUnauthorizedHandler = (handler: () => void): void => {
  unauthorizedHandler = handler;
};

/**
 * Clears the registered unauthorized handler.
 */
export const clearUnauthorizedHandler = (): void => {
  unauthorizedHandler = null;
};

export const getBaseUrl = (): string => {
  let envUrl: unknown;

  try {
    if (typeof import.meta !== 'undefined' && import.meta.env) {
      envUrl = import.meta.env.VITE_API_BASE_URL;
    }
  } catch {
    // import.meta may not have env in some runtimes
  }

  if (!envUrl && typeof process !== 'undefined' && process.env) {
    envUrl = process.env.VITE_API_BASE_URL;
  }

  const rawUrl =
    typeof envUrl === 'string' && envUrl.trim().length > 0
      ? envUrl.trim()
      : DEFAULT_BASE_URL;

  return rawUrl.replace(/\/+$/, '');
};

export const buildQueryString = (params?: Record<string, unknown>): string => {
  if (!params) return '';

  const searchParams = new URLSearchParams();

  for (const [key, value] of Object.entries(params)) {
    if (value === undefined || value === null || value === '') {
      continue;
    }
    if (typeof value === 'boolean') {
      searchParams.append(key, value ? 'true' : 'false');
    } else {
      searchParams.append(key, String(value));
    }
  }

  const query = searchParams.toString();
  return query ? `?${query}` : '';
};

/**
 * Dispatches an HTTP request with error parsing and ProblemDetails support.
 */
export const request = async <T>(
  endpoint: string,
  options: RequestOptions = {}
): Promise<T> => {
  const baseUrl = getBaseUrl();
  const normalizedEndpoint = endpoint.startsWith('/') ? endpoint : `/${endpoint}`;
  const queryString = buildQueryString(options.params);
  const url = `${baseUrl}${normalizedEndpoint}${queryString}`;

  const headers = new Headers(options.headers);

  // Attach JWT Bearer token if token provider is registered and authorization header is not yet present
  if (!headers.has('Authorization') && tokenProvider) {
    const token = tokenProvider();
    if (token) {
      headers.set('Authorization', `Bearer ${token}`);
    }
  }

  let body: BodyInit | undefined;

  if (options.body !== undefined && options.body !== null) {
    if (!headers.has('Content-Type')) {
      headers.set('Content-Type', 'application/json');
    }
    body = JSON.stringify(options.body);
  }

  const fetchInit: RequestInit = {
    ...options,
    headers,
    body,
  };

  let response: Response;
  try {
    response = await fetch(url, fetchInit);
  } catch (error: unknown) {
    if (error instanceof DOMException && error.name === 'AbortError') {
      throw error;
    }
    const message = error instanceof Error ? error.message : 'Unknown network failure';
    throw new ApiError(
      `Network error: Unable to connect to backend at ${baseUrl}. (${message})`,
      0,
      undefined,
      true
    );
  }

  if (response.status === 204) {
    return undefined as unknown as T;
  }

  const contentType = response.headers.get('content-type') || '';
  const isJson =
    contentType.includes('application/json') ||
    contentType.includes('application/problem+json');

  if (!response.ok) {
    const isLoginEndpoint =
      normalizedEndpoint === '/api/auth/login' ||
      normalizedEndpoint === '/auth/login';

    // 401 on protected endpoint triggers session cleanup & redirect via handler
    if (response.status === 401 && !isLoginEndpoint) {
      try {
        unauthorizedHandler?.();
      } catch {
        // Prevent unauthorized handler exceptions from suppressing ApiError
      }
    }

    if (isJson) {
      let problem: ProblemDetails | undefined;
      try {
        problem = (await response.json()) as ProblemDetails;
      } catch {
        // Fallback if JSON parsing fails unexpectedly
      }

      const formattedErrors = problem?.errors
        ? Object.entries(problem.errors)
            .map(([field, errs]) => `${field}: ${errs.join(', ')}`)
            .join('; ')
        : undefined;

      const message =
        problem?.detail ||
        formattedErrors ||
        problem?.title ||
        `Request failed with status ${response.status}`;

      throw new ApiError(message, response.status, problem, false);
    }

    const errorText = await response.text().catch(() => '');
    throw new ApiError(
      errorText || `Request failed with status ${response.status}`,
      response.status,
      undefined,
      false
    );
  }

  if (isJson) {
    return (await response.json()) as T;
  }

  return (await response.text()) as unknown as T;
};

export const apiClient = {
  get: <T>(endpoint: string, options?: RequestOptions) =>
    request<T>(endpoint, { ...options, method: 'GET' }),

  post: <T>(endpoint: string, body?: unknown, options?: RequestOptions) =>
    request<T>(endpoint, { ...options, method: 'POST', body }),

  put: <T>(endpoint: string, body?: unknown, options?: RequestOptions) =>
    request<T>(endpoint, { ...options, method: 'PUT', body }),

  patch: <T>(endpoint: string, body?: unknown, options?: RequestOptions) =>
    request<T>(endpoint, { ...options, method: 'PATCH', body }),

  delete: <T>(endpoint: string, options?: RequestOptions) =>
    request<T>(endpoint, { ...options, method: 'DELETE' }),

  setTokenProvider,
  clearTokenProvider,
  setUnauthorizedHandler,
  clearUnauthorizedHandler,
};
