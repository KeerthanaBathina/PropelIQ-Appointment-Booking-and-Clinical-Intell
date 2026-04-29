import CssBaseline from '@mui/material/CssBaseline';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { BrowserRouter } from 'react-router-dom';
import AppRoutes from '@/router';
import { SessionTimeoutProvider } from '@/context/SessionTimeoutProvider';
import RoleThemeProvider from '@/theme/RoleThemeProvider';
import { ToastProvider } from '@/components/common/ToastProvider';
import { SkipToContent } from '@/components/accessibility/SkipToContent';
import { LiveAnnouncerProvider } from '@/components/accessibility/LiveAnnouncer';

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
    /*
     * LiveAnnouncerProvider — outermost wrapper so the visually-hidden ARIA live
     * regions exist even if inner providers throw.  Any component at any depth can
     * call useLiveAnnouncer() to announce messages to screen readers (US_100, AC-5).
     */
    <LiveAnnouncerProvider>
      <QueryClientProvider client={queryClient}>
        <BrowserRouter>
          {/* RoleThemeProvider reads the Zustand auth store to apply per-role MUI palette (UXR-403) */}
          <RoleThemeProvider>
            <CssBaseline />
            {/*
             * SkipToContent — first focusable element in the page (US_100, AC-2, WCAG 2.4.1).
             * Pressing Tab reveals the link; activating it jumps focus to #main-content,
             * bypassing header/sidebar/navigation for keyboard-only users.
             */}
            <SkipToContent />
            {/* SessionTimeoutProvider activates only when accessToken is present (UXR-603) */}
            <SessionTimeoutProvider>
              {/* ToastProvider supplies showToast() for upload feedback (US_038 UXR-505) */}
              <ToastProvider>
                {/*
                 * landmark regions — screen readers expose these as navigation shortcuts
                 * (US_100, AC-3, WCAG 1.3.6, WAI-ARIA Landmarks).
                 *
                 * <main> receives id="main-content" and tabIndex={-1} so the SkipToContent
                 * link can programmatically focus it without placing it in the natural tab order.
                 */}
                <main
                  id="main-content"
                  role="main"
                  tabIndex={-1}
                  aria-label="Main content"
                  style={{ outline: 'none' }}
                >
                  <AppRoutes />
                </main>
              </ToastProvider>
            </SessionTimeoutProvider>
          </RoleThemeProvider>
        </BrowserRouter>
      </QueryClientProvider>
    </LiveAnnouncerProvider>
  );
}

export default App;