// Minimal API client — wraps fetch with base URL, JSON handling, and auth interceptors
import { useAuthStore } from '@/hooks/useAuth';

const BASE_URL = import.meta.env.VITE_API_BASE_URL as string;

export class ApiError extends Error {
  constructor(
    public readonly status: number,
    message: string,
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

// ─── Activity reset registration ─────────────────────────────────────────────
// SessionTimeoutProvider calls registerActivityReset() after mount so the
// interceptor can notify the inactivity timer on every 2xx response (AC-2).
let _activityResetFn: (() => void) | null = null;

export function registerActivityReset(fn: () => void): void {
  _activityResetFn = fn;
}

export function unregisterActivityReset(): void {
  _activityResetFn = null;
}

// ─── Invalidation registration ────────────────────────────────────────────────
// SessionTimeoutProvider registers its invalidation callback so a server-side
// 401 immediately clears the session without waiting for the client timer.
let _invalidateFn: (() => void) | null = null;

export function registerSessionInvalidate(fn: () => void): void {
  _invalidateFn = fn;
}

export function unregisterSessionInvalidate(): void {
  _invalidateFn = null;
}

/** Attaches the Bearer token from auth store when available. */
function buildHeaders(extra?: Record<string, string>): Record<string, string> {
  const token = useAuthStore.getState().accessToken;
  return {
    'Content-Type': 'application/json',
    ...(token ? { Authorization: `Bearer ${token}` } : {}),
    ...extra,
  };
}

/**
 * Handle 401 → trigger session invalidation (or fallback to clear + redirect);
 * 403 → redirect to /access-denied.
 */
function handleAuthError(status: number): void {
  if (status === 401) {
    if (_invalidateFn) {
      _invalidateFn();
    } else {
      useAuthStore.getState().clearAuth();
      window.location.replace('/login?expired=true');
    }
  } else if (status === 403) {
    window.location.replace('/access-denied');
  }
}

/** Notify inactivity timer of a successful 2xx response (AC-2). */
function notifyActivity(): void {
  _activityResetFn?.();
}

export async function apiPost<T>(path: string, body: unknown): Promise<T> {
  const response = await fetch(`${BASE_URL}${path}`, {
    method: 'POST',
    headers: buildHeaders(),
    body: JSON.stringify(body),
  });

  if (!response.ok) {
    handleAuthError(response.status);
    const text = await response.text().catch(() => response.statusText);
    throw new ApiError(response.status, text || `Request failed with status ${response.status}`);
  }

  notifyActivity();

  const contentType = response.headers.get('content-type');
  if (contentType?.includes('application/json')) {
    return response.json() as Promise<T>;
  }

  return null as T;
}

export async function apiDelete<T>(path: string): Promise<T> {
  const response = await fetch(`${BASE_URL}${path}`, {
    method: 'DELETE',
    headers: buildHeaders(),
  });

  if (!response.ok) {
    handleAuthError(response.status);
    const text = await response.text().catch(() => response.statusText);
    throw new ApiError(response.status, text || `Request failed with status ${response.status}`);
  }

  notifyActivity();

  const contentType = response.headers.get('content-type');
  if (contentType?.includes('application/json')) {
    return response.json() as Promise<T>;
  }

  return null as T;
}

export async function apiGet<T>(path: string): Promise<T> {
  const response = await fetch(`${BASE_URL}${path}`, {
    method: 'GET',
    headers: buildHeaders(),
  });

  if (!response.ok) {
    handleAuthError(response.status);
    const text = await response.text().catch(() => response.statusText);
    throw new ApiError(response.status, text || `Request failed with status ${response.status}`);
  }

  notifyActivity();

  const contentType = response.headers.get('content-type');
  if (contentType?.includes('application/json')) {
    return response.json() as Promise<T>;
  }

  return null as T;
}
