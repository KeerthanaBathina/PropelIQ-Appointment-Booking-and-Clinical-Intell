/**
 * ProfileHeader — US_043 SCR-013 patient info header (wireframe profile-header).
 *
 * Displays patient avatar (primary-100 bg, primary-700 text), name, DOB, ID,
 * source document count, and current version number.
 *
 * Design tokens: primary.100 bg, primary.700 text per wireframe CSS variables.
 */

import Avatar from '@mui/material/Avatar';
import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import Skeleton from '@mui/material/Skeleton';
import Typography from '@mui/material/Typography';
import type { PatientProfile360Dto } from '@/hooks/usePatientProfile';

// ─── Helpers ──────────────────────────────────────────────────────────────────

function getInitials(name: string): string {
  const parts = name.trim().split(/\s+/);
  if (parts.length >= 2) return `${parts[0][0]}${parts[parts.length - 1][0]}`.toUpperCase();
  return name.slice(0, 2).toUpperCase();
}

function formatDob(isoDate: string): string {
  try {
    return new Date(isoDate).toLocaleDateString('en-US', {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
    });
  } catch {
    return isoDate;
  }
}

// ─── Props ────────────────────────────────────────────────────────────────────

interface ProfileHeaderProps {
  profile: PatientProfile360Dto | undefined;
  isLoading: boolean;
  sourceDocumentCount?: number;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function ProfileHeader({ profile, isLoading, sourceDocumentCount }: ProfileHeaderProps) {
  if (isLoading) {
    return (
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 2, mb: 3 }}>
        <Skeleton variant="circular" width={64} height={64} />
        <Box sx={{ flexGrow: 1 }}>
          <Skeleton variant="text" width={240} height={32} />
          <Skeleton variant="text" width={320} height={20} sx={{ mt: 0.5 }} />
        </Box>
      </Box>
    );
  }

  if (!profile) return null;

  const initials = getInitials(profile.patientName);
  const docCount = sourceDocumentCount ?? 0;

  return (
    <Box
      sx={{ display: 'flex', alignItems: 'center', gap: 2, mb: 3 }}
      role="region"
      aria-label="Patient information"
    >
      {/* Avatar — primary-100 bg, primary-700 text per wireframe */}
      <Avatar
        sx={{
          width: 64,
          height: 64,
          bgcolor: 'primary.100',
          color: 'primary.700',
          fontSize: '1.5rem',
          fontWeight: 600,
          flexShrink: 0,
        }}
        aria-hidden="true"
      >
        {initials}
      </Avatar>

      <Box sx={{ flexGrow: 1 }}>
        <Box sx={{ display: 'flex', alignItems: 'center', gap: 1, flexWrap: 'wrap' }}>
          <Typography variant="h5" component="h1" sx={{ fontWeight: 500 }}>
            {profile.patientName}
          </Typography>
          <Chip label="Active" color="success" size="small" sx={{ height: 22 }} />
        </Box>

        <Typography variant="body2" color="text.secondary" sx={{ mt: 0.25 }}>
          <Box component="span" sx={{ mr: 2 }}>DOB: {formatDob(profile.dateOfBirth)}</Box>
          <Box component="span" sx={{ mr: 2 }}>ID: {profile.patientId.slice(0, 8).toUpperCase()}</Box>
          {docCount > 0 && (
            <Box component="span" sx={{ mr: 2 }}>
              {docCount} source document{docCount !== 1 ? 's' : ''}
            </Box>
          )}
          {profile.currentVersionNumber > 0 && (
            <Box component="span">
              Version {profile.currentVersionNumber}
            </Box>
          )}
        </Typography>

        {profile.lastConsolidatedAt && (
          <Typography variant="caption" color="text.disabled">
            Last consolidated:{' '}
            {new Date(profile.lastConsolidatedAt).toLocaleString('en-US', {
              dateStyle: 'medium',
              timeStyle: 'short',
            })}
          </Typography>
        )}
      </Box>
    </Box>
  );
}
