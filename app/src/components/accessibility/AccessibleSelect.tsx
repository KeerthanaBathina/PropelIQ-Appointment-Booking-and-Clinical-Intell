import Select, { type SelectProps } from '@mui/material/Select';

interface AccessibleSelectProps extends SelectProps {
  /** Descriptive label read by screen readers (required — MUI native Select lacks one). */
  ariaLabel: string;
  /** Whether the field is required; injected as `aria-required`. */
  ariaRequired?: boolean;
  /** ID of a helper/error text element to associate via `aria-describedby`. */
  helperTextId?: string;
}

/**
 * MUI Select wrapper that fixes the ARIA gap where the underlying `<select>` element
 * lacks `aria-label` and `aria-required` attributes (US_100, AC-3, edge case 1).
 *
 * MUI renders the native element inside `inputProps`; passing ARIA attributes there
 * ensures they land on the actual form control that screen readers interrogate.
 *
 * Usage:
 * ```tsx
 * <AccessibleSelect ariaLabel="Appointment type" ariaRequired value={value} onChange={onChange}>
 *   <MenuItem value="checkup">Routine check-up</MenuItem>
 * </AccessibleSelect>
 * ```
 */
export function AccessibleSelect({
  ariaLabel,
  ariaRequired = false,
  helperTextId,
  inputProps,
  ...rest
}: AccessibleSelectProps) {
  return (
    <Select
      {...rest}
      inputProps={{
        ...inputProps,
        'aria-label': ariaLabel,
        'aria-required': ariaRequired,
        ...(helperTextId ? { 'aria-describedby': helperTextId } : {}),
      }}
    />
  );
}
