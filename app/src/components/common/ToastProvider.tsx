/**
 * ToastProvider — application-wide transient notification surface (US_038 UXR-505, UXR-606; US_039 AC-2, AC-3, AC-5).
 *
 * Provides a React context + hook for showing MUI Snackbar/Alert toasts from anywhere in the
 * component tree without prop-drilling.
 *
 * Usage:
 *   const { showToast } = useToast();
 *   showToast({ message: 'Document uploaded.', severity: 'success' });
 *
 *   // Parsing helpers (US_039):
 *   const { showParsingStarted, showParsingComplete, showParsingFailed } = useToast();
 *
 * Only one toast is shown at a time; subsequent calls replace the current message.
 * Auto-hides after `duration` ms (default 4000).
 * Toasts with an `action` node (e.g. Review Results) do not auto-hide so the user can interact.
 */

import { createContext, useCallback, useContext, useState } from 'react';
import type { ReactNode } from 'react';
import Alert from '@mui/material/Alert';
import Button from '@mui/material/Button';
import Snackbar from '@mui/material/Snackbar';
import type { AlertColor } from '@mui/material/Alert';

// ─── Types ────────────────────────────────────────────────────────────────────

export interface ToastOptions {
  message: string;
  severity?: AlertColor;
  /** Auto-hide duration in ms. Defaults to 4000. Pass `null` to disable auto-hide (e.g. for action toasts). */
  duration?: number | null;
  /** Optional action node rendered inside the Snackbar (e.g. a link button). */
  action?: ReactNode;
}

interface ToastContextValue {
  showToast: (opts: ToastOptions) => void;
  /** Shows a toast indicating AI parsing has started for `fileName` (US_039 AC-2). */
  showParsingStarted: (fileName: string) => void;
  /** Shows a toast indicating AI parsing completed with a link to review results (US_039 AC-3). */
  showParsingComplete: (fileName: string, reviewUrl: string) => void;
  /** Shows a toast indicating permanent parsing failure with a manual-review action (US_039 AC-5, EC-2). */
  showParsingFailed: (fileName: string, onManualReview: () => void) => void;
  /** Shows a toast for single-item verification success (US_041 AC-4). */
  showVerificationComplete: (itemLabel: string) => void;
  /** Shows a toast for bulk verification success with item count (US_041 EC-2). */
  showBulkVerificationComplete: (count: number) => void;
  /** Shows a toast when a document preview fails to load (US_042 EC-3). */
  showPreviewLoadFailed: (fileName: string) => void;
  /** Shows a toast after a successful document replacement upload (US_042 AC-2). */
  showReplacementSuccess: (fileName: string) => void;
  /** Shows a toast indicating the replaced document is queued for reprocessing (US_042 AC-2). */
  showReprocessingStarted: (fileName: string) => void;
}

// ─── Context ──────────────────────────────────────────────────────────────────

const ToastContext = createContext<ToastContextValue | null>(null);

// ─── Provider ─────────────────────────────────────────────────────────────────

interface ToastProviderProps {
  children: ReactNode;
}

export function ToastProvider({ children }: ToastProviderProps) {
  const [open, setOpen] = useState(false);
  const [options, setOptions] = useState<ToastOptions>({
    message: '',
    severity: 'info',
    duration: 4000,
  });

  const showToast = useCallback((opts: ToastOptions) => {
    setOptions({ severity: 'info', duration: 4000, ...opts });
    setOpen(true);
  }, []);

  const handleClose = (_: React.SyntheticEvent | Event, reason?: string) => {
    if (reason === 'clickaway') return;
    setOpen(false);
  };

  // ── Parsing-specific helpers (US_039) ─────────────────────────────────────

  const showParsingStarted = useCallback((fileName: string) => {
    showToast({
      message: `AI parsing started for “${fileName}”.`,
      severity: 'info',
      duration: 4000,
    });
  }, [showToast]);

  const showParsingComplete = useCallback((fileName: string, reviewUrl: string) => {
    showToast({
      message: `“${fileName}” has been parsed. Results are ready.`,
      severity: 'success',
      // Keep open until user acts (no auto-hide) so they can click Review Results.
      duration: null,
      action: (
        <Button
          color="inherit"
          size="small"
          href={reviewUrl}
          aria-label={`Review AI parsing results for ${fileName}`}
        >
          Review Results
        </Button>
      ),
    });
  }, [showToast]);

  const showParsingFailed = useCallback((fileName: string, onManualReview: () => void) => {
    showToast({
      message: `AI parsing failed for “${fileName}” after all retries.`,
      severity: 'error',
      duration: null,
      action: (
        <Button
          color="inherit"
          size="small"
          onClick={() => {
            setOpen(false);
            onManualReview();
          }}
          aria-label={`Request manual review for ${fileName}`}
        >
          Manual Review
        </Button>
      ),
    });
  }, [showToast]);

  // ── Verification helpers (US_041 AC-4, EC-2) ──────────────────────────────

  const showVerificationComplete = useCallback((itemLabel: string) => {
    showToast({
      message: `"${itemLabel}" verified successfully.`,
      severity: 'success',
      duration: 4000,
    });
  }, [showToast]);

  const showBulkVerificationComplete = useCallback((count: number) => {
    showToast({
      message: `${count} item${count !== 1 ? 's' : ''} verified successfully.`,
      severity: 'success',
      duration: 4000,
    });
  }, [showToast]);

  // ── Preview / replacement helpers (US_042) ───────────────────────────────────

  const showPreviewLoadFailed = useCallback((fileName: string) => {
    showToast({
      message: `Preview could not be loaded for "${fileName}". Please try again.`,
      severity: 'error',
      duration: 6000,
    });
  }, [showToast]);

  const showReplacementSuccess = useCallback((fileName: string) => {
    showToast({
      message: `"${fileName}" replaced successfully.`,
      severity: 'success',
      duration: 4000,
    });
  }, [showToast]);

  const showReprocessingStarted = useCallback((fileName: string) => {
    showToast({
      message: `"${fileName}" has been queued for reprocessing.`,
      severity: 'info',
      duration: 4000,
    });
  }, [showToast]);

  return (
    <ToastContext.Provider value={{
      showToast,
      showParsingStarted,
      showParsingComplete,
      showParsingFailed,
      showVerificationComplete,
      showBulkVerificationComplete,
      showPreviewLoadFailed,
      showReplacementSuccess,
      showReprocessingStarted,
    }}>
      {children}
      <Snackbar
        open={open}
        autoHideDuration={options.duration ?? null}
        onClose={handleClose}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
        action={options.action}
      >
        <Alert
          onClose={handleClose}
          severity={options.severity}
          variant="filled"
          sx={{ width: '100%', minWidth: 300 }}
          role="status"
          aria-live="polite"
          action={options.action}
        >
          {options.message}
        </Alert>
      </Snackbar>
    </ToastContext.Provider>
  );
}

// ─── Hook ─────────────────────────────────────────────────────────────────────

export function useToast(): ToastContextValue {
  const ctx = useContext(ToastContext);
  if (!ctx) {
    throw new Error('useToast must be used inside a ToastProvider');
  }
  return ctx;
}
