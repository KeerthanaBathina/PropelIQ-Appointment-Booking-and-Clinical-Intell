/**
 * ProviderFilter — MUI Select dropdown for filtering slots by provider (US_017).
 *
 * Wireframe (SCR-006): Provider selector with "Any Provider" as the default.
 * On change the parent re-queries / filters the slot list.
 *
 * Accessibility: labeled via MUI InputLabel + htmlFor pairing (WCAG 2.1 AA).
 */

import FormControl from '@mui/material/FormControl';
import InputLabel from '@mui/material/InputLabel';
import MenuItem from '@mui/material/MenuItem';
import Select, { type SelectChangeEvent } from '@mui/material/Select';
import type { ProviderOption } from '@/hooks/useAppointmentSlots';

interface Props {
  providers: ProviderOption[];
  value: string;
  onChange: (providerId: string) => void;
  disabled?: boolean;
}

export default function ProviderFilter({ providers, value, onChange, disabled = false }: Props) {
  function handleChange(e: SelectChangeEvent<string>) {
    onChange(e.target.value);
  }

  return (
    <FormControl size="small" sx={{ minWidth: 200 }} disabled={disabled}>
      <InputLabel id="provider-filter-label">Provider (Optional)</InputLabel>
      <Select
        labelId="provider-filter-label"
        id="provider-filter"
        value={value}
        label="Provider (Optional)"
        onChange={handleChange}
        inputProps={{ 'aria-label': 'Select provider' }}
      >
        <MenuItem value="">Any Provider</MenuItem>
        {providers.map((p) => (
          <MenuItem key={p.providerId} value={p.providerId}>
            {p.providerName}
          </MenuItem>
        ))}
      </Select>
    </FormControl>
  );
}
