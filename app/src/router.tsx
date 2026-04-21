import { lazy, Suspense } from 'react';
import { Route, Routes } from 'react-router-dom';
import CircularProgress from '@mui/material/CircularProgress';
import Box from '@mui/material/Box';
import ProtectedRoute from '@/guards/ProtectedRoute';

// Route-level code splitting — each page chunk is only loaded when navigated to
const PlaceholderPage = lazy(() => import('@/pages/PlaceholderPage'));
const NotFoundPage = lazy(() => import('@/pages/NotFoundPage'));
const LoginPage = lazy(() => import('@/pages/LoginPage'));
const RegistrationPage = lazy(() => import('@/pages/RegistrationPage'));
const EmailVerificationPage = lazy(() => import('@/pages/EmailVerificationPage'));
const DashboardRouterPage = lazy(() => import('@/pages/DashboardRouterPage'));
const AccessDeniedPage = lazy(() => import('@/pages/AccessDeniedPage'));
const ForgotPasswordPage = lazy(() => import('@/pages/ForgotPasswordPage'));
const ResetPasswordPage = lazy(() => import('@/pages/ResetPasswordPage'));
const PatientDashboard = lazy(() => import('@/pages/PatientDashboard'));
const StaffDashboard = lazy(() => import('@/pages/StaffDashboard'));
const AdminDashboard = lazy(() => import('@/pages/AdminDashboard'));
const AppointmentBookingPage = lazy(() => import('@/pages/AppointmentBookingPage'));
const AppointmentHistoryPage = lazy(() => import('@/pages/AppointmentHistoryPage'));

function RouteLoadingFallback() {
  return (
    <Box sx={{ display: 'flex', justifyContent: 'center', mt: 8 }}>
      <CircularProgress />
    </Box>
  );
}

function AppRoutes() {
  return (
    <Suspense fallback={<RouteLoadingFallback />}>
      <Routes>
        {/* Public routes */}
        <Route path="/" element={<PlaceholderPage />} />
        <Route path="/login" element={<LoginPage />} />
        <Route path="/register" element={<RegistrationPage />} />
        <Route path="/verify-email" element={<EmailVerificationPage />} />
        <Route path="/forgot-password" element={<ForgotPasswordPage />} />
        <Route path="/reset-password" element={<ResetPasswordPage />} />
        <Route path="/access-denied" element={<AccessDeniedPage />} />

        {/* SCR-002 — Dashboard router (any authenticated user) */}
        <Route
          path="/dashboard"
          element={
            <ProtectedRoute allowedRoles={['Patient', 'Staff', 'Admin']}>
              <DashboardRouterPage />
            </ProtectedRoute>
          }
        />

        {/* Patient routes — SCR-005 through SCR-009 */}
        <Route
          path="/patient/dashboard"
          element={
            <ProtectedRoute allowedRoles={['Patient']}>
              <PatientDashboard />
            </ProtectedRoute>
          }
        />
        {/* SCR-006 — Appointment Booking */}
        <Route
          path="/patient/appointments/book"
          element={
            <ProtectedRoute allowedRoles={['Patient']}>
              <AppointmentBookingPage />
            </ProtectedRoute>
          }
        />
        {/* SCR-007 — Appointment History */}
        <Route
          path="/patient/appointments/history"
          element={
            <ProtectedRoute allowedRoles={['Patient']}>
              <AppointmentHistoryPage />
            </ProtectedRoute>
          }
        />

        <Route
          path="/patient/*"
          element={
            <ProtectedRoute allowedRoles={['Patient']}>
              <PlaceholderPage />
            </ProtectedRoute>
          }
        />

        {/* Staff routes — SCR-010 through SCR-014, SCR-016 */}
        <Route
          path="/staff/dashboard"
          element={
            <ProtectedRoute allowedRoles={['Staff']}>
              <StaffDashboard />
            </ProtectedRoute>
          }
        />
        <Route
          path="/staff/*"
          element={
            <ProtectedRoute allowedRoles={['Staff']}>
              <PlaceholderPage />
            </ProtectedRoute>
          }
        />

        {/* Admin routes — SCR-015 */}
        <Route
          path="/admin/dashboard"
          element={
            <ProtectedRoute allowedRoles={['Admin']}>
              <AdminDashboard />
            </ProtectedRoute>
          }
        />
        <Route
          path="/admin/*"
          element={
            <ProtectedRoute allowedRoles={['Admin']}>
              <PlaceholderPage />
            </ProtectedRoute>
          }
        />

        <Route path="*" element={<NotFoundPage />} />
      </Routes>
    </Suspense>
  );
}

export default AppRoutes;
