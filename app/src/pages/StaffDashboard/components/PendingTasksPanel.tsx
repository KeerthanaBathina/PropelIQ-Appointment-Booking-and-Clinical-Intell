/**
 * PendingTasksPanel — List of staff pending review tasks (US_057 AC-3, SCR-010).
 *
 * Each task shows: category label (subtitle2) + description (caption) + action button.
 * Click navigation (AC-3):
 *   DocumentReview    → /staff/patients/:patientId  (SCR-013)
 *   CodeApproval      → /staff/coding               (SCR-014)
 *   ConflictResolution → /staff/patients/:patientId (SCR-013)
 *
 * Loading: skeleton placeholders (UXR-502).
 * Empty: "No pending tasks" message.
 *
 * Usage:
 *   <PendingTasksPanel tasks={data.pendingTasks} isLoading={isLoading} />
 */

import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Divider from '@mui/material/Divider';
import Skeleton from '@mui/material/Skeleton';
import Typography from '@mui/material/Typography';
import { useNavigate } from 'react-router-dom';
import type { PendingTask, PendingTaskCategory } from '@/types/staffDashboard';

// ─── Category config ──────────────────────────────────────────────────────────

const CATEGORY_META: Record<PendingTaskCategory, { label: string; action: string }> = {
  DocumentReview:     { label: 'Document Review',     action: 'Review' },
  CodeApproval:       { label: 'Code Approval',        action: 'Review' },
  ConflictResolution: { label: 'Conflict Resolution',  action: 'Resolve' },
};

function resolveRoute(category: PendingTaskCategory, patientId: string): string {
  switch (category) {
    case 'CodeApproval':
      return '/staff/coding';
    case 'DocumentReview':
    case 'ConflictResolution':
    default:
      return `/staff/patients/${patientId}`;
  }
}

// ─── Props ────────────────────────────────────────────────────────────────────

interface Props {
  tasks?: PendingTask[];
  isLoading: boolean;
}

// ─── Component ────────────────────────────────────────────────────────────────

export default function PendingTasksPanel({ tasks, isLoading }: Props) {
  const navigate = useNavigate();

  if (isLoading) {
    return (
      <Box aria-busy="true" aria-label="Loading pending tasks">
        {[0, 1, 2].map(i => (
          <Box key={i} sx={{ py: 1.5 }}>
            <Skeleton variant="text" width="60%" />
            <Skeleton variant="text" width="80%" />
          </Box>
        ))}
      </Box>
    );
  }

  if (!tasks || tasks.length === 0) {
    return (
      <Typography variant="body2" color="text.secondary" sx={{ py: 2 }}>
        No pending tasks at this time.
      </Typography>
    );
  }

  return (
    <Box component="ul" sx={{ listStyle: 'none', p: 0, m: 0 }} role="list">
      {tasks.map((task, idx) => {
        const { label, action } = CATEGORY_META[task.category] ?? CATEGORY_META.DocumentReview;
        const route = resolveRoute(task.category, task.patientId);

        return (
          <Box
            key={task.id}
            component="li"
            role="listitem"
            aria-label={`${label}: ${task.patientName} — ${task.description}`}
          >
            <Box
              sx={{
                display:        'flex',
                alignItems:     'center',
                justifyContent: 'space-between',
                py:             1.5,
              }}
            >
              <Box>
                <Typography variant="subtitle2">{label}</Typography>
                <Typography variant="caption" color="text.secondary">
                  {task.patientName} — {task.description}
                </Typography>
              </Box>
              <Button
                size="small"
                variant="text"
                color="secondary"
                onClick={() => navigate(route)}
                aria-label={`${action} ${label} for ${task.patientName}`}
              >
                {action}
              </Button>
            </Box>
            {idx < tasks.length - 1 && <Divider />}
          </Box>
        );
      })}
    </Box>
  );
}
