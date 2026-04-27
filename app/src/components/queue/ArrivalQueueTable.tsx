/**
 * ArrivalQueueTable — sortable, drag-and-drop MUI Table for the arrival queue (US_053/US_054, SCR-011).
 *
 * Columns:
 *   ⠿ (drag handle), #, Patient, Appt Time, Provider, Type, Wait Time, Priority, Status, Actions
 *
 * Features (US_053):
 *   - Sortable headers: Patient, Appt Time, Wait Time (aria-sort on th, UXR-206)
 *   - Patient name links to SCR-013 (/staff/patients/:patientId/profile)
 *   - Row highlight (error.surface #FFEBEE) for wait time > 30 minutes
 *   - 60% opacity for no-show rows
 *   - Action buttons per status (In Visit / Urgent / Arrived)
 *   - QueueStatusBadge + QueueWaitTimeTimer
 *   - Skeleton loading state (UXR-502)
 *
 * Features (US_054):
 *   - @dnd-kit drag-and-drop row reordering via DndContext + SortableContext
 *   - Drag handle column (DragIndicator icon)
 *   - QueueReorderControls (up/down arrow buttons) in Actions cell
 *   - PriorityOverrideDialog when non-urgent moved above urgent (UXR-102)
 *   - onReorder callback propagates (fromQueueId, toQueueId) to parent
 */

import { useState } from 'react';

import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Link from '@mui/material/Link';
import Paper from '@mui/material/Paper';
import Skeleton from '@mui/material/Skeleton';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import TableSortLabel from '@mui/material/TableSortLabel';
import Typography from '@mui/material/Typography';
import DragIndicatorIcon from '@mui/icons-material/DragIndicator';
import { Link as RouterLink } from 'react-router-dom';

import {
  DndContext,
  closestCenter,
  KeyboardSensor,
  PointerSensor,
  useSensor,
  useSensors,
  type DragEndEvent,
} from '@dnd-kit/core';
import {
  SortableContext,
  sortableKeyboardCoordinates,
  verticalListSortingStrategy,
  useSortable,
} from '@dnd-kit/sortable';
import { CSS } from '@dnd-kit/utilities';

import NoShowAutoBadge from '@/components/queue/NoShowAutoBadge';
import PriorityOverrideDialog from '@/components/queue/PriorityOverrideDialog';
import QueueReorderControls from '@/components/queue/QueueReorderControls';
import QueueStatusBadge from '@/components/queue/QueueStatusBadge';
import QueueWaitTimeTimer from '@/components/queue/QueueWaitTimeTimer';
import WaitTimeAlertBadge from '@/components/queue/WaitTimeAlertBadge';
import type { QueueEntry } from '@/hooks/useQueueData';

// ─── Types ────────────────────────────────────────────────────────────────────

export type SortColumn = 'patientName' | 'appointmentTime' | 'waitTimeMinutes';
export type SortDirection = 'asc' | 'desc';

interface Props {
  entries:          QueueEntry[];
  isLoading:        boolean;
  sortColumn:       SortColumn;
  sortDirection:    SortDirection;
  onSort:           (column: SortColumn) => void;
  onMarkInVisit:    (queueId: string) => void;
  onMarkArrived:    (appointmentId: string) => void;
  onMarkUrgent:     (queueId: string) => void;
  /** Called when a row is dragged or arrow-keyed to a new position. */
  onReorder:        (fromQueueId: string, toQueueId: string) => void;
  /** True when a reorder mutation is in-flight. */
  isReordering:     boolean;
  /** True when any mutation is pending (disables action buttons, aria-busy). */
  isMutating:       boolean;
  /** Configurable wait time alert threshold in minutes (US_055 AC-3). Defaults to 30. */
  thresholdMinutes: number;
}

// ─── Constants ────────────────────────────────────────────────────────────────

const ERROR_SURFACE_BG = '#FFEBEE';  // designsystem.md error.surface

// ─── Skeleton rows ────────────────────────────────────────────────────────────

function TableSkeleton() {
  return (
    <>
      {Array.from({ length: 5 }).map((_, i) => (
        <TableRow key={i}>
          {Array.from({ length: 10 }).map((_, j) => (
            <TableCell key={j}>
              <Skeleton variant="text" width={j === 0 ? 20 : '80%'} />
            </TableCell>
          ))}
        </TableRow>
      ))}
    </>
  );
}

// ─── Action buttons ───────────────────────────────────────────────────────────

interface ActionCellProps {
  entry:         QueueEntry;
  isFirst:       boolean;
  isLast:        boolean;
  isMutating:    boolean;
  isReordering:  boolean;
  onMarkInVisit: (queueId: string) => void;
  onMarkArrived: (appointmentId: string) => void;
  onMarkUrgent:  (queueId: string) => void;
  onMoveUp:      () => void;
  onMoveDown:    () => void;
}

function ActionCell({
  entry,
  isFirst,
  isLast,
  isMutating,
  isReordering,
  onMarkInVisit,
  onMarkArrived,
  onMarkUrgent,
  onMoveUp,
  onMoveDown,
}: ActionCellProps) {
  const isActive    = entry.status === 'waiting' || entry.status === 'arrived_late';
  const isScheduled = entry.status === 'scheduled';

  if (entry.status === 'no_show') {
    return <Typography variant="caption" color="text.secondary">Auto-marked</Typography>;
  }

  return (
    <Box sx={{ display: 'flex', gap: 0.5, alignItems: 'center', flexWrap: 'nowrap' }}>
      {isActive && (
        <Button
          variant="contained"
          size="small"
          disabled={isMutating}
          aria-busy={isMutating}
          aria-label={`Mark ${entry.patientName} In Visit`}
          onClick={() => onMarkInVisit(entry.queueId)}
          sx={{ whiteSpace: 'nowrap', minWidth: 0 }}
        >
          In Visit
        </Button>
      )}
      {isActive && entry.priority !== 'urgent' && (
        <Button
          variant="outlined"
          size="small"
          disabled={isMutating}
          aria-busy={isMutating}
          aria-label={`Mark ${entry.patientName} as Urgent`}
          onClick={() => onMarkUrgent(entry.queueId)}
          sx={{ whiteSpace: 'nowrap', minWidth: 0 }}
        >
          Urgent
        </Button>
      )}
      {isScheduled && (
        <Button
          variant="outlined"
          size="small"
          disabled={isMutating}
          aria-busy={isMutating}
          aria-label={`Mark ${entry.patientName} as Arrived`}
          onClick={() => onMarkArrived(entry.appointmentId)}
          sx={{ whiteSpace: 'nowrap', minWidth: 0 }}
        >
          Arrived
        </Button>
      )}
      {/* Keyboard reorder controls (accessible alternative to drag-and-drop, UXR-206) */}
      <QueueReorderControls
        queueId={entry.queueId}
        patientName={entry.patientName}
        isFirst={isFirst}
        isLast={isLast}
        isMutating={isReordering || isMutating}
        onMoveUp={onMoveUp}
        onMoveDown={onMoveDown}
      />
    </Box>
  );
}

// ─── Sortable row ─────────────────────────────────────────────────────────────

interface SortableQueueRowProps {
  entry:            QueueEntry;
  idx:              number;
  isFirst:          boolean;
  isLast:           boolean;
  isMutating:       boolean;
  isReordering:     boolean;
  thresholdMinutes: number;
  onMarkInVisit:    (queueId: string) => void;
  onMarkArrived:    (appointmentId: string) => void;
  onMarkUrgent:     (queueId: string) => void;
  onMoveUp:         () => void;
  onMoveDown:       () => void;
}

function SortableQueueRow({
  entry,
  idx,
  isFirst,
  isLast,
  isMutating,
  isReordering,
  thresholdMinutes,
  onMarkInVisit,
  onMarkArrived,
  onMarkUrgent,
  onMoveUp,
  onMoveDown,
}: SortableQueueRowProps) {
  const {
    attributes,
    listeners,
    setNodeRef,
    transform,
    transition,
    isDragging,
  } = useSortable({ id: entry.queueId });

  const style = {
    transform: CSS.Transform.toString(transform),
    transition,
    opacity: isDragging ? 0.5 : entry.status === 'no_show' ? 0.6 : 1,
    backgroundColor: isDragging
      ? '#F5F5F5'
      : entry.waitTimeMinutes >= thresholdMinutes
      ? ERROR_SURFACE_BG
      : undefined,
    cursor: isDragging ? 'grabbing' : undefined,
  };

  return (
    <TableRow ref={setNodeRef} style={style}>
      {/* Drag handle */}
      <TableCell sx={{ width: 32, cursor: 'grab', color: 'text.disabled', px: 0.5 }} {...attributes} {...listeners}>
        <DragIndicatorIcon fontSize="small" aria-hidden="true" />
      </TableCell>

      {/* Row number */}
      <TableCell>
        <Typography variant="body2" color="text.secondary">
          {entry.queuePosition ?? idx + 1}
        </Typography>
      </TableCell>

      {/* Patient name — link to SCR-013 */}
      <TableCell>
        <Link
          component={RouterLink}
          to={`/staff/patients/${entry.appointmentId}/profile`}
          underline="hover"
          color="primary"
          aria-label={`View profile for ${entry.patientName}`}
        >
          {entry.patientName}
        </Link>
      </TableCell>

      {/* Appointment time */}
      <TableCell>
        <Typography variant="body2">
          {new Intl.DateTimeFormat(undefined, {
            hour:   'numeric',
            minute: '2-digit',
          }).format(new Date(entry.appointmentTime))}
        </Typography>
      </TableCell>

      {/* Provider */}
      <TableCell>
        <Typography variant="body2" color={entry.providerName ? 'text.primary' : 'text.secondary'}>
          {entry.providerName ?? '—'}
        </Typography>
      </TableCell>

      {/* Appointment type */}
      <TableCell>
        <Typography variant="body2" color={entry.appointmentType ? 'text.primary' : 'text.secondary'}>
          {entry.appointmentType ?? '—'}
        </Typography>
      </TableCell>

      {/* Wait time timer + alert badge (US_055 AC-2) */}
      <TableCell>
        <Box sx={{ display: 'flex', alignItems: 'center', flexWrap: 'wrap', gap: 0.5 }}>
          <QueueWaitTimeTimer arrivalTimestamp={entry.arrivalTimestamp} />
          <WaitTimeAlertBadge
            waitTimeMinutes={entry.waitTimeMinutes}
            thresholdMinutes={thresholdMinutes}
          />
        </Box>
      </TableCell>

      {/* Priority badge */}
      <TableCell>
        <QueueStatusBadge variant="priority" status={entry.priority} />
      </TableCell>

      {/* Status badge — NoShowAutoBadge for auto-detected no-shows (US_055 AC-2) */}
      <TableCell>
        {entry.isAutoNoShow ? (
          <NoShowAutoBadge
            isAutoDetected
            isDelayedDetection={entry.isDelayedDetection}
          />
        ) : (
          <QueueStatusBadge status={entry.status} />
        )}
      </TableCell>

      {/* Action buttons + reorder controls */}
      <TableCell>
        <ActionCell
          entry={entry}
          isFirst={isFirst}
          isLast={isLast}
          isMutating={isMutating}
          isReordering={isReordering}
          onMarkInVisit={onMarkInVisit}
          onMarkArrived={onMarkArrived}
          onMarkUrgent={onMarkUrgent}
          onMoveUp={onMoveUp}
          onMoveDown={onMoveDown}
        />
      </TableCell>
    </TableRow>
  );
}

// ─── Component ────────────────────────────────────────────────────────────────

function getSortAriaLabel(col: SortColumn, active: SortColumn, dir: SortDirection): 'ascending' | 'descending' | undefined {
  if (col !== active) return undefined;
  return dir === 'asc' ? 'ascending' : 'descending';
}

export default function ArrivalQueueTable({
  entries,
  isLoading,
  sortColumn,
  sortDirection,
  onSort,
  onMarkInVisit,
  onMarkArrived,
  onMarkUrgent,
  onReorder,
  isReordering,
  isMutating,
  thresholdMinutes,
}: Props) {
  // Pending move waiting for PriorityOverrideDialog confirmation (UXR-102)
  const [pendingMove, setPendingMove] = useState<{ fromId: string; toId: string } | null>(null);

  const sensors = useSensors(
    useSensor(PointerSensor),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  );

  function handleDragEnd(event: DragEndEvent) {
    const { active, over } = event;
    if (!over || active.id === over.id) return;

    const fromId = String(active.id);
    const toId   = String(over.id);

    const fromEntry = entries.find((e) => e.queueId === fromId);
    const toEntry   = entries.find((e) => e.queueId === toId);

    if (!fromEntry || !toEntry) return;

    // Check if a non-urgent entry is being moved above an urgent entry (UXR-102)
    const fromIdx = entries.findIndex((e) => e.queueId === fromId);
    const toIdx   = entries.findIndex((e) => e.queueId === toId);
    const isMovingUp = toIdx < fromIdx;

    if (
      isMovingUp &&
      fromEntry.priority !== 'urgent' &&
      toEntry.priority === 'urgent'
    ) {
      // Warn before allowing override
      setPendingMove({ fromId, toId });
      return;
    }

    onReorder(fromId, toId);
  }

  function handleMoveUp(queueId: string) {
    const idx = entries.findIndex((e) => e.queueId === queueId);
    if (idx <= 0) return;

    const targetEntry = entries[idx - 1];
    const movingEntry = entries[idx];

    if (!targetEntry || !movingEntry) return;

    // Check priority override required
    if (movingEntry.priority !== 'urgent' && targetEntry.priority === 'urgent') {
      setPendingMove({ fromId: queueId, toId: targetEntry.queueId });
      return;
    }

    onReorder(queueId, targetEntry.queueId);
  }

  function handleMoveDown(queueId: string) {
    const idx = entries.findIndex((e) => e.queueId === queueId);
    if (idx < 0 || idx >= entries.length - 1) return;

    const targetEntry = entries[idx + 1];
    if (!targetEntry) return;

    onReorder(queueId, targetEntry.queueId);
  }

  function handleOverrideConfirm() {
    if (pendingMove) {
      onReorder(pendingMove.fromId, pendingMove.toId);
    }
    setPendingMove(null);
  }

  const pendingPatient = pendingMove
    ? (entries.find((e) => e.queueId === pendingMove.fromId)?.patientName ?? '')
    : '';

  return (
    <>
      <Paper variant="outlined">
        <TableContainer>
          <DndContext
            sensors={sensors}
            collisionDetection={closestCenter}
            onDragEnd={handleDragEnd}
          >
            <Table aria-label="Arrival queue" size="small">
              <TableHead>
                <TableRow>
                  {/* Drag handle column header */}
                  <TableCell sx={{ width: 32 }} aria-hidden="true" />

                  <TableCell sx={{ width: 40, fontWeight: 700 }}>#</TableCell>

                  {/* Sortable: Patient */}
                  <TableCell
                    aria-sort={getSortAriaLabel('patientName', sortColumn, sortDirection)}
                    sx={{ fontWeight: 700 }}
                  >
                    <TableSortLabel
                      active={sortColumn === 'patientName'}
                      direction={sortColumn === 'patientName' ? sortDirection : 'asc'}
                      onClick={() => onSort('patientName')}
                    >
                      Patient
                    </TableSortLabel>
                  </TableCell>

                  {/* Sortable: Appt Time */}
                  <TableCell
                    aria-sort={getSortAriaLabel('appointmentTime', sortColumn, sortDirection)}
                    sx={{ fontWeight: 700 }}
                  >
                    <TableSortLabel
                      active={sortColumn === 'appointmentTime'}
                      direction={sortColumn === 'appointmentTime' ? sortDirection : 'asc'}
                      onClick={() => onSort('appointmentTime')}
                    >
                      Appt Time
                    </TableSortLabel>
                  </TableCell>

                  <TableCell sx={{ fontWeight: 700 }}>Provider</TableCell>
                  <TableCell sx={{ fontWeight: 700 }}>Type</TableCell>

                  {/* Sortable: Wait Time */}
                  <TableCell
                    aria-sort={getSortAriaLabel('waitTimeMinutes', sortColumn, sortDirection)}
                    sx={{ fontWeight: 700 }}
                  >
                    <TableSortLabel
                      active={sortColumn === 'waitTimeMinutes'}
                      direction={sortColumn === 'waitTimeMinutes' ? sortDirection : 'asc'}
                      onClick={() => onSort('waitTimeMinutes')}
                    >
                      Wait Time
                    </TableSortLabel>
                  </TableCell>

                  <TableCell sx={{ fontWeight: 700 }}>Priority</TableCell>
                  <TableCell sx={{ fontWeight: 700 }}>Status</TableCell>
                  <TableCell sx={{ fontWeight: 700 }}>Actions</TableCell>
                </TableRow>
              </TableHead>

              <TableBody>
                {isLoading ? (
                  <TableSkeleton />
                ) : entries.length === 0 ? (
                  <TableRow>
                    <TableCell colSpan={10} align="center" sx={{ py: 4 }}>
                      <Typography variant="body2" color="text.secondary">
                        No patients in queue.
                      </Typography>
                    </TableCell>
                  </TableRow>
                ) : (
                  <SortableContext
                    items={entries.map((e) => e.queueId)}
                    strategy={verticalListSortingStrategy}
                  >
                    {entries.map((entry, idx) => (
                      <SortableQueueRow
                        key={entry.queueId}
                        entry={entry}
                        idx={idx}
                        isFirst={idx === 0}
                        isLast={idx === entries.length - 1}
                        isMutating={isMutating}
                        isReordering={isReordering}
                        thresholdMinutes={thresholdMinutes}
                        onMarkInVisit={onMarkInVisit}
                        onMarkArrived={onMarkArrived}
                        onMarkUrgent={onMarkUrgent}
                        onMoveUp={() => handleMoveUp(entry.queueId)}
                        onMoveDown={() => handleMoveDown(entry.queueId)}
                      />
                    ))}
                  </SortableContext>
                )}
              </TableBody>
            </Table>
          </DndContext>
        </TableContainer>
      </Paper>

      {/* Priority override confirmation dialog (UXR-102) */}
      <PriorityOverrideDialog
        open={pendingMove !== null}
        patientName={pendingPatient}
        onConfirm={handleOverrideConfirm}
        onCancel={() => setPendingMove(null)}
      />
    </>
  );
}
