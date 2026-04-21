/**
 * WalkInPatientSearchResults — Selectable list of patient search matches (US_022 AC-2).
 *
 * Displays name, DOB, and phone for each matching patient.  Staff click or
 * press Enter/Space to select a record; keyboard roving focus is supported so
 * every action is reachable without a mouse (UXR-202).
 *
 * Loading state: three Skeleton rows (UXR-502).
 * Empty state: instructional message when the search returns no matches.
 * Selected state: secondary-accent highlight (UXR-403 staff colour treatment).
 *
 * @param results          - Matching patient records from the search API.
 * @param selectedPatientId - Currently selected record (null = nothing selected).
 * @param onSelect         - Called when a record is activated.
 * @param loading          - True while the search query is in-flight.
 * @param searchTerm       - The active search term — used to show a contextual empty message.
 */

import Box from '@mui/material/Box';
import List from '@mui/material/List';
import ListItemButton from '@mui/material/ListItemButton';
import ListItemText from '@mui/material/ListItemText';
import Skeleton from '@mui/material/Skeleton';
import Typography from '@mui/material/Typography';
import type { PatientSearchResult } from '@/hooks/useWalkInPatientSearch';

// ─── Helpers ──────────────────────────────────────────────────────────────────

function formatDob(dob: string): string {
  const [y, m, d] = dob.split('-').map(Number);
  return new Date(y, m - 1, d).toLocaleDateString('en-US', {
    month: 'short',
    day: 'numeric',
    year: 'numeric',
  });
}

// ─── Types ────────────────────────────────────────────────────────────────────

interface Props {
  results: PatientSearchResult[];
  selectedPatientId: string | null;
  onSelect: (patient: PatientSearchResult) => void;
  loading: boolean;
  searchTerm: string;
}

// ─── Skeleton rows (UXR-502) ──────────────────────────────────────────────────

function SearchSkeletons() {
  return (
    <Box role="status" aria-label="Searching for patients…">
      {[0, 1, 2].map((i) => (
        <Box key={i} sx={{ px: 2, py: 1 }}>
          <Skeleton variant="text" width="60%" height={20} />
          <Skeleton variant="text" width="40%" height={16} />
        </Box>
      ))}
    </Box>
  );
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function WalkInPatientSearchResults({
  results,
  selectedPatientId,
  onSelect,
  loading,
  searchTerm,
}: Props) {
  if (loading) {
    return <SearchSkeletons />;
  }

  if (results.length === 0 && searchTerm.trim().length >= 2) {
    return (
      <Typography
        variant="body2"
        color="text.secondary"
        sx={{ px: 2, py: 1.5 }}
        role="status"
      >
        No patients found matching &ldquo;{searchTerm}&rdquo;. You can create a new patient
        record below.
      </Typography>
    );
  }

  if (results.length === 0) {
    return null;
  }

  return (
    <List
      dense
      aria-label="Patient search results"
      sx={{
        border: '1px solid',
        borderColor: 'divider',
        borderRadius: 1,
        maxHeight: 240,
        overflow: 'auto',
      }}
    >
      {results.map((patient) => {
        const isSelected = patient.patientId === selectedPatientId;
        return (
          <ListItemButton
            key={patient.patientId}
            selected={isSelected}
            onClick={() => onSelect(patient)}
            onKeyDown={(e) => {
              if (e.key === 'Enter' || e.key === ' ') {
                e.preventDefault();
                onSelect(patient);
              }
            }}
            aria-pressed={isSelected}
            sx={{
              '&.Mui-selected': {
                bgcolor: 'secondary.50',
                borderLeft: '3px solid',
                borderLeftColor: 'secondary.500',
              },
              '&.Mui-selected:hover': {
                bgcolor: 'secondary.100',
              },
            }}
          >
            <ListItemText
              primary={
                <Typography variant="subtitle2" component="span">
                  {patient.fullName}
                </Typography>
              }
              secondary={
                <Typography variant="caption" color="text.secondary" component="span">
                  DOB: {formatDob(patient.dateOfBirth)} · {patient.phone}
                </Typography>
              }
            />
          </ListItemButton>
        );
      })}
    </List>
  );
}
