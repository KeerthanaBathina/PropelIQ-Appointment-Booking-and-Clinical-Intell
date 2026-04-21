import type { ReactNode } from 'react';
import { useAuth, type UserRole } from '@/hooks/useAuth';

interface RoleGuardProps {
  /** Render children only when the authenticated user has this role. */
  role: UserRole;
  children: ReactNode;
  /** Optional fallback to render when role does not match (defaults to null). */
  fallback?: ReactNode;
}

/**
 * Inline role gate — renders `children` only when the current user's role
 * matches the required `role` prop.  Unlike `ProtectedRoute`, this component
 * does NOT redirect; it simply shows/hides content within a page.
 *
 * Example:
 *   <RoleGuard role="Admin">
 *     <AdminOnlySection />
 *   </RoleGuard>
 */
export default function RoleGuard({ role, children, fallback = null }: RoleGuardProps) {
  const { hasRole } = useAuth();
  return hasRole(role) ? <>{children}</> : <>{fallback}</>;
}
