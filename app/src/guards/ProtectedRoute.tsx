import type { ReactNode } from 'react';
import { Navigate, useLocation } from 'react-router-dom';
import { useAuth, type UserRole } from '@/hooks/useAuth';

interface ProtectedRouteProps {
  /** Roles that are permitted to view the wrapped content. */
  allowedRoles: UserRole[];
  children: ReactNode;
}

/**
 * Enforces role-based access control at the route level.
 *
 * - Not authenticated  → redirect to /login (preserves intended destination)
 * - Authenticated but wrong role → redirect to /access-denied
 * - Authenticated and role allowed → render children
 */
export default function ProtectedRoute({ allowedRoles, children }: ProtectedRouteProps) {
  const { isAuthenticated, role } = useAuth();
  const location = useLocation();

  if (!isAuthenticated) {
    // Preserve the attempted path so login can redirect back after success
    return <Navigate to="/login" state={{ from: location }} replace />;
  }

  if (!role || !allowedRoles.includes(role)) {
    return <Navigate to="/access-denied" replace />;
  }

  return <>{children}</>;
}
