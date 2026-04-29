/**
 * MUI DatePicker accessibility wrapper (US_100, AC-3, edge case 1).
 *
 * Fixes two MUI DatePicker ARIA gaps:
 *  1. The calendar popup has no `aria-label` — screen readers announce "dialog" with no context.
 *  2. The open-picker icon button has no descriptive `aria-label` — announced as "button".
 *
 * Requires `@mui/x-date-pickers` — install before use:
 *   npm install @mui/x-date-pickers dayjs
 *
 * Usage:
 * ```tsx
 * <AccessibleDatePicker
 *   label="Date of birth"
 *   ariaLabel="Date of birth"
 *   value={value}
 *   onChange={onChange}
 * />
 * ```
 *
 * Note: If `@mui/x-date-pickers` is not yet available in this project, use a plain
 * MUI `TextField` with `type="date"` and apply the `aria-label` directly via `inputProps`.
 *
 * WCAG reference: 4.1.2 Name, Role, Value.
 */

// ── Type-only dependency surface ──────────────────────────────────────────────
// The wrapper is defined generically so it compiles even before the x-date-pickers
// package is installed.  At runtime the consumer must supply the DatePicker component
// from '@mui/x-date-pickers/DatePicker'.

export interface AccessibleDatePickerSlotProps {
  textField?: Record<string, unknown>;
  openPickerButton?: Record<string, unknown>;
  popper?: Record<string, unknown>;
  [key: string]: unknown;
}

export interface AccessibleDatePickerBaseProps {
  ariaLabel: string;
  calendarAriaLabel?: string;
  slotProps?: AccessibleDatePickerSlotProps;
  [key: string]: unknown;
}

/**
 * Returns merged `slotProps` for any MUI DatePicker component that add the required
 * ARIA attributes.  Pass the result directly to the `slotProps` prop.
 *
 * @example
 * ```tsx
 * import { DatePicker } from '@mui/x-date-pickers/DatePicker';
 * import { buildAccessibleDatePickerSlotProps } from '@/components/accessibility/AccessibleDatePicker';
 *
 * <DatePicker
 *   label="Appointment date"
 *   slotProps={buildAccessibleDatePickerSlotProps({
 *     ariaLabel: 'Appointment date',
 *     existing: props.slotProps,
 *   })}
 * />
 * ```
 */
export function buildAccessibleDatePickerSlotProps({
  ariaLabel,
  calendarAriaLabel = 'Choose date',
  existing = {},
}: {
  ariaLabel: string;
  calendarAriaLabel?: string;
  existing?: AccessibleDatePickerSlotProps;
}): AccessibleDatePickerSlotProps {
  return {
    ...existing,
    textField: {
      ...(existing.textField ?? {}),
      inputProps: {
        ...((existing.textField as Record<string, unknown>)?.inputProps ?? {}),
        'aria-label': ariaLabel,
      },
    },
    openPickerButton: {
      ...(existing.openPickerButton ?? {}),
      'aria-label': calendarAriaLabel,
    },
    popper: {
      ...(existing.popper ?? {}),
      role: 'dialog',
      'aria-label': calendarAriaLabel,
    },
  };
}
