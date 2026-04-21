import { jwtDecode } from 'jwt-decode';
import { create } from 'zustand';
import { persist } from 'zustand/middleware';

// ─── Role constants ───────────────────────────────────────────────────────────
export type UserRole = 'Patient' | 'Staff' | 'Admin';

// JWT claim shape returned by UPACIP.Api identity tokens
interface JwtClaims {
  sub: string;
  email: string;
  /** ASP.NET Core Identity role claim */
  'http://schemas.microsoft.com/ws/2008/06/identity/claims/role'?: UserRole;
  /** Short-form role claim (some token configs) */
  role?: UserRole;
  exp: number;
  iat: number;
}

function extractRole(token: string): UserRole | null {
  try {
    const claims = jwtDecode<JwtClaims>(token);
    return (
      claims['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'] ??
      claims.role ??
      null
    );
  } catch {
    return null;
  }
}

// ─── Auth store (Zustand persisted to sessionStorage) ────────────────────────
export interface LastLogin {
  /** ISO 8601 UTC timestamp of the previous successful login. */
  timestamp: string;
  /** Client IP address recorded by the backend at last login. */
  ipAddress: string;
}

interface AuthState {
  accessToken: string | null;
  role: UserRole | null;
  email: string | null;
  /** Last-login metadata returned in the login response (AC-4, US_016). Null on first-ever login. */
  lastLogin: LastLogin | null;

  // Actions
  setTokens: (accessToken: string) => void;
  setLastLogin: (data: LastLogin | null) => void;
  clearAuth: () => void;
  refreshRole: () => void;
}

export const useAuthStore = create<AuthState>()(
  persist(
    (set, get) => ({
      accessToken: null,
      role: null,
      email: null,
      lastLogin: null,

      setTokens(accessToken: string) {
        let role: UserRole | null = null;
        let email: string | null = null;
        try {
          const claims = jwtDecode<JwtClaims>(accessToken);
          role =
            claims['http://schemas.microsoft.com/ws/2008/06/identity/claims/role'] ??
            claims.role ??
            null;
          email = claims.email ?? null;
        } catch {
          // malformed token — cleared below
        }
        set({ accessToken, role, email });
      },

      setLastLogin(data: LastLogin | null) {
        set({ lastLogin: data });
      },

      clearAuth() {
        set({ accessToken: null, role: null, email: null, lastLogin: null });
      },

      // Re-derive role from the currently stored token (edge case: token refreshed).
      refreshRole() {
        const { accessToken } = get();
        if (!accessToken) return;
        const role = extractRole(accessToken);
        set({ role });
      },
    }),
    {
      name: 'upacip-auth',
      storage: {
        getItem: (key) => {
          const value = sessionStorage.getItem(key);
          return value ? JSON.parse(value) : null;
        },
        setItem: (key, value) => sessionStorage.setItem(key, JSON.stringify(value)),
        removeItem: (key) => sessionStorage.removeItem(key),
      },
    },
  ),
);

// ─── useAuth hook — role helpers ──────────────────────────────────────────────
export function useAuth() {
  const { accessToken, role, email, lastLogin, setTokens, setLastLogin, clearAuth, refreshRole } = useAuthStore();

  const isAuthenticated = !!accessToken;

  function getUserRole(): UserRole | null {
    return role;
  }

  function hasRole(targetRole: UserRole): boolean {
    return role === targetRole;
  }

  function isPatient(): boolean {
    return role === 'Patient';
  }

  function isStaff(): boolean {
    return role === 'Staff';
  }

  function isAdmin(): boolean {
    return role === 'Admin';
  }

  /** Dashboard path derived from current role. Falls back to /login when no role. */
  function getDashboardPath(): string {
    if (role === 'Patient') return '/patient/dashboard';
    if (role === 'Staff') return '/staff/dashboard';
    if (role === 'Admin') return '/admin/dashboard';
    return '/login';
  }

  /**
   * Stores the new access token received from POST /api/auth/extend-session.
   * Delegates to setTokens which re-derives role + email from the JWT.
   */
  function extendSession(newAccessToken: string): void {
    setTokens(newAccessToken);
  }

  /**
   * Clears all auth state and any storage entry.
   * Navigate to /login?expired=true separately (done by SessionTimeoutProvider).
   */
  function invalidateSession(): void {
    clearAuth();
  }

  return {
    accessToken,
    role,
    email,
    lastLogin,
    isAuthenticated,
    getUserRole,
    hasRole,
    isPatient,
    isStaff,
    isAdmin,
    getDashboardPath,
    setTokens,
    setLastLogin,
    clearAuth,
    refreshRole,
    extendSession,
    invalidateSession,
  };
}
