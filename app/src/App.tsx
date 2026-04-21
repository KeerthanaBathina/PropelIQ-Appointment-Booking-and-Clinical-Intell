import CssBaseline from '@mui/material/CssBaseline';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { BrowserRouter } from 'react-router-dom';
import AppRoutes from '@/router';
import { SessionTimeoutProvider } from '@/context/SessionTimeoutProvider';
import RoleThemeProvider from '@/theme/RoleThemeProvider';

// 5-minute stale time for healthcare data per NFR-030
const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 5 * 60 * 1000,
      retry: 1,
    },
  },
});

function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        {/* RoleThemeProvider reads the Zustand auth store to apply per-role MUI palette (UXR-403) */}
        <RoleThemeProvider>
          <CssBaseline />
          {/* SessionTimeoutProvider activates only when accessToken is present (UXR-603) */}
          <SessionTimeoutProvider>
            <AppRoutes />
          </SessionTimeoutProvider>
        </RoleThemeProvider>
      </BrowserRouter>
    </QueryClientProvider>
  );
}

export default App;