import Box from '@mui/material/Box';
import LinearProgress from '@mui/material/LinearProgress';
import Typography from '@mui/material/Typography';
import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutline';
import RadioButtonUncheckedIcon from '@mui/icons-material/RadioButtonUnchecked';
import { getPasswordStrength } from '@/validation/registrationSchema';

interface Props {
  password: string;
}

type StrengthLevel = 0 | 1 | 2 | 3 | 4;

const STRENGTH_COLOR: Record<StrengthLevel, 'error' | 'warning' | 'success'> = {
  0: 'error',
  1: 'error',
  2: 'warning',
  3: 'success',
  4: 'success',
};

const STRENGTH_PERCENT: Record<StrengthLevel, number> = {
  0: 0,
  1: 25,
  2: 50,
  3: 75,
  4: 100,
};

const STRENGTH_LABEL_SX: Record<StrengthLevel, string> = {
  0: 'error.main',
  1: 'error.main',
  2: 'warning.main',
  3: 'success.main',
  4: 'success.main',
};

const CRITERIA_ITEMS = [
  { key: 'length' as const, label: '8+ characters' },
  { key: 'uppercase' as const, label: '1 uppercase letter' },
  { key: 'number' as const, label: '1 number' },
  { key: 'special' as const, label: '1 special character' },
];

export default function PasswordStrengthIndicator({ password }: Props) {
  if (!password) return null;

  const strength = getPasswordStrength(password);
  const color = STRENGTH_COLOR[strength.level];
  const percent = STRENGTH_PERCENT[strength.level];
  const labelSx = STRENGTH_LABEL_SX[strength.level];

  return (
    <Box sx={{ mt: 1 }} role="region" aria-label="Password strength">
      <LinearProgress
        variant="determinate"
        value={percent}
        color={color}
        sx={{ height: 4, borderRadius: 1, bgcolor: 'grey.200' }}
      />
      <Typography
        variant="caption"
        sx={{ color: labelSx, display: 'block', mt: 0.5, textTransform: 'capitalize' }}
        aria-live="polite"
      >
        {strength.label}
      </Typography>
      <Box component="ul" sx={{ listStyle: 'none', p: 0, m: 0, mt: 0.75 }}>
        {CRITERIA_ITEMS.map(({ key, label }) => {
          const met = strength.criteria[key];
          return (
            <Box
              component="li"
              key={key}
              sx={{ display: 'flex', alignItems: 'center', gap: 0.5, mb: 0.25 }}
            >
              {met ? (
                <CheckCircleOutlineIcon
                  sx={{ fontSize: 14, color: 'success.main' }}
                  aria-hidden="true"
                />
              ) : (
                <RadioButtonUncheckedIcon
                  sx={{ fontSize: 14, color: 'text.disabled' }}
                  aria-hidden="true"
                />
              )}
              <Typography
                variant="caption"
                sx={{ color: met ? 'success.main' : 'text.secondary' }}
              >
                {label}
              </Typography>
            </Box>
          );
        })}
      </Box>
    </Box>
  );
}
