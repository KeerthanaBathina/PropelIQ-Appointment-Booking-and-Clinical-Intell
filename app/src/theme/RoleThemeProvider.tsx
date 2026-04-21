import type { ReactNode } from 'react';
import { createTheme, ThemeProvider } from '@mui/material/styles';
import { useAuth, type UserRole } from '@/hooks/useAuth';

// ─── Per-role MUI themes (UXR-403) ────────────────────────────────────────────
// Patient-facing screens use primary-500 (#1976D2) as the accent color.
// Staff-facing screens use secondary-500 (#7B1FA2) as the accent color.
// Admin-facing screens use neutral-700 (#616161) as the accent color.

const patientTheme = createTheme({
  palette: {
    primary: {
      main: '#1976D2', // primary-500
      contrastText: '#FFFFFF',
    },
    secondary: {
      main: '#7B1FA2', // secondary-500
    },
  },
});

const staffTheme = createTheme({
  palette: {
    primary: {
      main: '#7B1FA2', // secondary-500 promoted to primary for staff screens
      contrastText: '#FFFFFF',
    },
    secondary: {
      main: '#1976D2', // primary-500 demoted to secondary
    },
  },
});

const adminTheme = createTheme({
  palette: {
    primary: {
      main: '#616161', // neutral-700
      contrastText: '#FFFFFF',
    },
    secondary: {
      main: '#1976D2',
    },
  },
});

// Default theme shared across unguarded routes (login, register, etc.)
const defaultTheme = patientTheme;

const ROLE_THEME_MAP: Record<UserRole, ReturnType<typeof createTheme>> = {
  Patient: patientTheme,
  Staff: staffTheme,
  Admin: adminTheme,
};

interface RoleThemeProviderProps {
  children: ReactNode;
}

/**
 * Wraps children in an MUI ThemeProvider whose palette is derived from the
 * authenticated user's role.  Falls back to the patient (primary) theme for
 * unauthenticated or unresolved role states.
 */
export default function RoleThemeProvider({ children }: RoleThemeProviderProps) {
  const { role } = useAuth();
  const theme = role ? ROLE_THEME_MAP[role] : defaultTheme;

  return <ThemeProvider theme={theme}>{children}</ThemeProvider>;
}
