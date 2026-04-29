import { useCallback, useState } from 'react';
import TextField, { type TextFieldProps } from '@mui/material/TextField';
import { useLiveAnnouncer } from '@/hooks/useLiveAnnouncer';

interface AccessibleFormFieldProps extends Omit<TextFieldProps, 'error' | 'id'> {
  /** Stable HTML `id` for the input — used to link the error message via `aria-describedby`. */
  fieldId: string;
  /**
   * Synchronous validator.  Return an error string when the value is invalid,
   * or `undefined` when it is valid.  Runs in <1 ms, well within the 200 ms
   * threshold required by AC-5 / NFR-048.
   */
  validate?: (value: string) => string | undefined;
  /** Trigger validation on blur (default: `true`). */
  validateOnBlur?: boolean;
  /**
   * Trigger re-validation on change after the field has been touched (default: `false`).
   * Set to `true` to clear errors as the user corrects invalid input.
   */
  validateOnChange?: boolean;
}

/**
 * Accessible MUI `TextField` with inline validation and ARIA error announcements
 * (US_100, AC-5, WCAG 3.3.1 Error Identification, WCAG 3.3.3 Error Suggestion).
 *
 * When validation fires and an error is detected:
 * 1. `aria-invalid="true"` is set on the input — screen readers announce "invalid entry"
 *    when the user focuses the field.
 * 2. The error text is rendered with `role="alert"` — triggers an immediate live-region
 *    announcement (assertive semantics, cross-browser/screen-reader compatible).
 * 3. `aria-describedby` links the input to the error element — screen readers read the
 *    error after the field label when navigating via Tab.
 * 4. `useLiveAnnouncer` fires an `assertive` announcement as a belt-and-suspenders
 *    fallback — ensures compatibility with NVDA, JAWS, and VoiceOver, which handle
 *    `role="alert"` and `aria-live` slightly differently.
 *
 * Usage:
 * ```tsx
 * <AccessibleFormField
 *   fieldId="registration-email"
 *   label="Email address"
 *   required
 *   validate={(v) => (!v ? 'Email is required' : undefined)}
 *   validateOnBlur
 *   validateOnChange
 * />
 * ```
 */
export function AccessibleFormField({
  fieldId,
  validate,
  validateOnBlur = true,
  validateOnChange = false,
  helperText,
  onBlur,
  onChange,
  inputProps,
  required,
  ...rest
}: AccessibleFormFieldProps) {
  const [error, setError]   = useState<string | undefined>();
  const [touched, setTouched] = useState(false);
  const { announce }          = useLiveAnnouncer();

  const errorId  = `${fieldId}-error`;
  const helperId = `${fieldId}-helper`;

  const runValidation = useCallback(
    (value: string) => {
      if (!validate) return;
      const msg = validate(value);
      setError(msg);
      if (msg) {
        announce(msg, 'assertive');
      }
    },
    [validate, announce],
  );

  const handleBlur = useCallback(
    (e: React.FocusEvent<HTMLInputElement>) => {
      setTouched(true);
      if (validateOnBlur) {
        runValidation(e.target.value);
      }
      onBlur?.(e);
    },
    [validateOnBlur, runValidation, onBlur],
  );

  const handleChange = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      if (validateOnChange && touched) {
        runValidation(e.target.value);
      }
      (onChange as React.ChangeEventHandler<HTMLInputElement> | undefined)?.(e);
    },
    [validateOnChange, touched, runValidation, onChange],
  );

  const hasError = touched && !!error;

  return (
    <TextField
      {...rest}
      id={fieldId}
      required={required}
      error={hasError}
      onBlur={handleBlur}
      onChange={handleChange}
      helperText={
        hasError ? (
          // role="alert" fires an assertive announcement the moment the node appears —
          // more reliable than aria-live on the parent for cross-AT compatibility.
          <span id={errorId} role="alert">
            {error}
          </span>
        ) : helperText ? (
          <span id={helperId}>{helperText}</span>
        ) : undefined
      }
      inputProps={{
        ...inputProps,
        'aria-invalid': hasError || undefined,
        'aria-describedby': hasError ? errorId : helperText ? helperId : undefined,
        'aria-required': required,
      }}
    />
  );
}
