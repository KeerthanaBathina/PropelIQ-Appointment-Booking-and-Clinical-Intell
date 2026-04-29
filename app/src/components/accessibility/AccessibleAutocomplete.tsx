import Autocomplete, { type AutocompleteProps } from '@mui/material/Autocomplete';

type AccessibleAutocompleteProps<
  T,
  Multiple extends boolean | undefined = false,
  DisableClearable extends boolean | undefined = false,
  FreeSolo extends boolean | undefined = false,
> = AutocompleteProps<T, Multiple, DisableClearable, FreeSolo> & {
  /**
   * Descriptive label for the dropdown listbox read by screen readers.
   * MUI's default listbox has no label — screen readers announce "list" without context.
   * Example: `"Provider search results"`, `"Medication suggestions"`.
   */
  listboxAriaLabel: string;
};

/**
 * MUI Autocomplete wrapper that adds an `aria-label` to the dropdown listbox
 * (US_100, AC-3, edge case 1).
 *
 * MUI renders the suggestions list as a `<ul role="listbox">` without an `aria-label`,
 * so screen readers announce only "list".  This wrapper passes the label via
 * `ListboxProps` so the listbox has full context.
 *
 * Usage:
 * ```tsx
 * <AccessibleAutocomplete
 *   listboxAriaLabel="Provider search results"
 *   options={providers}
 *   getOptionLabel={(p) => p.name}
 *   renderInput={(params) => <TextField {...params} label="Search providers" />}
 * />
 * ```
 */
export function AccessibleAutocomplete<
  T,
  Multiple extends boolean | undefined = false,
  DisableClearable extends boolean | undefined = false,
  FreeSolo extends boolean | undefined = false,
>({
  listboxAriaLabel,
  ListboxProps,
  ...rest
}: AccessibleAutocompleteProps<T, Multiple, DisableClearable, FreeSolo>) {
  return (
    <Autocomplete
      {...rest}
      ListboxProps={{
        ...ListboxProps,
        'aria-label': listboxAriaLabel,
      }}
    />
  );
}
