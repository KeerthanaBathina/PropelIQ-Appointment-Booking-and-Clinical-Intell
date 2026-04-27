/**
 * NotificationTemplatesPanel — Template editor for SCR-015 Config tab (US_060 AC-1, AC-2).
 *
 * Wireframe: table of templates → click Edit → inline editor replaces table row
 * (or a drawer-style edit panel below the table).
 *
 * Features:
 *  - Template selector (list of all templates, click Edit to open form)
 *  - Channel toggle (Email / SMS / Email+SMS / In-App) via MUI ToggleButtonGroup
 *  - Subject line field (Email only)
 *  - Body text multiline field with VariablePlaceholderToolbar above it
 *  - Live TemplatePreview panel (reads body + replaces sample values)
 *  - Inline validation within 200ms (UXR-501):
 *      - Subject required for Email/Email+SMS
 *      - Body required
 *      - {{...}} placeholders must be in allowed list; unknown vars shown in error
 *  - Confirmation dialog before save (UXR-102)
 *  - Auto-save draft to localStorage every 2 s of inactivity (UXR-004)
 *  - Loading skeleton (UXR-502)
 *  - Empty and Error states
 *
 * Authorization: Admin only (consumed via AdminDashboard / ConfigTabs).
 */

import Alert from '@mui/material/Alert';
import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Chip from '@mui/material/Chip';
import CircularProgress from '@mui/material/CircularProgress';
import Dialog from '@mui/material/Dialog';
import DialogActions from '@mui/material/DialogActions';
import DialogContent from '@mui/material/DialogContent';
import DialogTitle from '@mui/material/DialogTitle';
import FormHelperText from '@mui/material/FormHelperText';
import Paper from '@mui/material/Paper';
import Skeleton from '@mui/material/Skeleton';
import Table from '@mui/material/Table';
import TableBody from '@mui/material/TableBody';
import TableCell from '@mui/material/TableCell';
import TableContainer from '@mui/material/TableContainer';
import TableHead from '@mui/material/TableHead';
import TableRow from '@mui/material/TableRow';
import TextField from '@mui/material/TextField';
import ToggleButton from '@mui/material/ToggleButton';
import ToggleButtonGroup from '@mui/material/ToggleButtonGroup';
import Tooltip from '@mui/material/Tooltip';
import Typography from '@mui/material/Typography';
import { useCallback, useEffect, useRef, useState } from 'react';
import {
  useNotificationTemplates,
  useUpdateNotificationTemplateById,
} from '@/hooks/useAdminConfig';
import { useToast } from '@/components/common/ToastProvider';
import type {
  NotificationChannel,
  NotificationStatus,
  NotificationTemplate,
  UpdateNotificationTemplateRequest,
} from '@/types/adminConfig';
import {
  ALLOWED_PLACEHOLDER_NAMES,
  TEMPLATE_VARIABLES,
} from '@/types/adminConfig';

// ─── Constants ────────────────────────────────────────────────────────────────

const PLACEHOLDER_PATTERN = /\{\{(\w+)\}\}/g;

const CHANNELS: NotificationChannel[] = ['Email', 'SMS', 'Email + SMS', 'In-App'];
const STATUSES: NotificationStatus[]  = ['Active', 'Draft', 'Disabled'];

function statusColor(s: NotificationStatus): 'success' | 'default' | 'error' {
  if (s === 'Active')   return 'success';
  if (s === 'Disabled') return 'error';
  return 'default';
}

const FALLBACK_TEMPLATES: NotificationTemplate[] = [
  { id: '1', name: 'Appointment Reminder (24h)', channel: 'Email + SMS', trigger: '24h before appt',    status: 'Active', subject: '24-hour appointment reminder', bodyTemplate: 'Hi {{patient_name}}, your appointment is on {{date}} at {{time}} with {{provider}}.' },
  { id: '2', name: 'Appointment Confirmation',   channel: 'Email',       trigger: 'On booking',         status: 'Active', subject: 'Your appointment is confirmed',  bodyTemplate: 'Hello {{patient_name}}, your appointment on {{date}} at {{time}} with {{provider}} has been confirmed.' },
  { id: '3', name: 'Intake Completion',           channel: 'In-App',     trigger: 'Intake submitted',   status: 'Draft',  bodyTemplate: 'Thank you {{patient_name}} for completing your intake form.' },
  { id: '4', name: 'No-Show Follow-up',           channel: 'SMS',        trigger: '15 min past appt',   status: 'Active', bodyTemplate: 'Hi {{patient_name}}, we missed you for your {{date}} appointment. Please reschedule at your earliest convenience.' },
];

// ─── Helpers ─────────────────────────────────────────────────────────────────

/** Substitute sample values into body text for preview */
function renderPreview(body: string): string {
  return body.replace(PLACEHOLDER_PATTERN, (_, name) => {
    const v = TEMPLATE_VARIABLES.find(t => t.name === name);
    return v ? v.sampleValue : `{{${name}}}`;
  });
}

/** Returns list of unrecognised variable names used in body */
function findInvalidPlaceholders(body: string): string[] {
  const invalid: string[] = [];
  let m: RegExpExecArray | null;
  const re = /\{\{(\w+)\}\}/g;
  while ((m = re.exec(body)) !== null) {
    if (!ALLOWED_PLACEHOLDER_NAMES.has(m[1])) invalid.push(m[1]);
  }
  return [...new Set(invalid)];
}

const DRAFT_KEY = 'admin-notif-template-draft';

// ─── Variable Placeholder Toolbar ────────────────────────────────────────────

interface VarToolbarProps {
  onInsert: (placeholder: string) => void;
}

function VariablePlaceholderToolbar({ onInsert }: VarToolbarProps) {
  return (
    <Box sx={{ display: 'flex', gap: 0.75, flexWrap: 'wrap', mb: 0.75 }} aria-label="Variable placeholder toolbar">
      {TEMPLATE_VARIABLES.map(v => (
        <Tooltip key={v.name} title={`Sample: "${v.sampleValue}"`}>
          <Chip
            label={v.placeholder}
            size="small"
            variant="outlined"
            onClick={() => onInsert(v.placeholder)}
            clickable
            aria-label={`Insert ${v.placeholder}`}
            sx={{ fontFamily: 'monospace', fontSize: '0.75rem' }}
          />
        </Tooltip>
      ))}
    </Box>
  );
}

// ─── Template Preview ─────────────────────────────────────────────────────────

interface PreviewProps {
  subject: string;
  body:    string;
  channel: NotificationChannel;
}

function TemplatePreview({ subject, body, channel }: PreviewProps) {
  const showSubject = channel === 'Email' || channel === 'Email + SMS';
  return (
    <Paper
      variant="outlined"
      sx={{ p: 2, bgcolor: 'grey.50', borderRadius: 1, minHeight: 80 }}
      aria-label="Template preview"
    >
      <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mb: 0.5, fontWeight: 600 }}>
        Preview (sample values)
      </Typography>
      {showSubject && subject && (
        <Typography variant="body2" fontWeight={600} sx={{ mb: 0.5 }}>
          {renderPreview(subject)}
        </Typography>
      )}
      <Typography variant="body2" sx={{ whiteSpace: 'pre-wrap', color: 'text.secondary' }}>
        {body ? renderPreview(body) : <em>Body text will appear here…</em>}
      </Typography>
    </Paper>
  );
}

// ─── Edit Form ────────────────────────────────────────────────────────────────

interface EditFormProps {
  template: NotificationTemplate;
  onClose:  () => void;
}

function EditForm({ template, onClose }: EditFormProps) {
  const { showToast } = useToast();
  const { mutateAsync: save, isLoading: isSaving } = useUpdateNotificationTemplateById();

  const [channel, setChannel] = useState<NotificationChannel>(template.channel);
  const [subject, setSubject] = useState(template.subject ?? '');
  const [body,    setBody]    = useState(template.bodyTemplate ?? '');
  const [status,  setStatus]  = useState<NotificationStatus>(template.status);

  const [subjectErr, setSubjectErr] = useState('');
  const [bodyErr,    setBodyErr]    = useState('');

  const bodyRef = useRef<HTMLTextAreaElement | null>(null);

  // UXR-004: auto-save draft to localStorage every 2 s of inactivity
  const draftTimer = useRef<ReturnType<typeof setTimeout> | null>(null);
  useEffect(() => {
    if (draftTimer.current) clearTimeout(draftTimer.current);
    draftTimer.current = setTimeout(() => {
      localStorage.setItem(
        `${DRAFT_KEY}:${template.id}`,
        JSON.stringify({ channel, subject, body, status }),
      );
    }, 2000);
    return () => { if (draftTimer.current) clearTimeout(draftTimer.current); };
  }, [channel, subject, body, status, template.id]);

  // Restore draft on mount
  useEffect(() => {
    try {
      const raw = localStorage.getItem(`${DRAFT_KEY}:${template.id}`);
      if (raw) {
        const d = JSON.parse(raw) as { channel: NotificationChannel; subject: string; body: string; status: NotificationStatus };
        setChannel(d.channel);
        setSubject(d.subject);
        setBody(d.body);
        setStatus(d.status);
      }
    } catch { /* ignore */ }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const showSubject = channel === 'Email' || channel === 'Email + SMS';

  // UXR-501: validate within 200ms (onChange is synchronous, so effectively immediate)
  const validateSubject = useCallback((v: string) => {
    if (showSubject && !v.trim()) {
      setSubjectErr('Subject is required for Email templates.');
    } else {
      setSubjectErr('');
    }
  }, [showSubject]);

  const validateBody = useCallback((v: string) => {
    if (!v.trim()) {
      setBodyErr('Body text is required.');
      return;
    }
    const invalid = findInvalidPlaceholders(v);
    if (invalid.length > 0) {
      setBodyErr(`Unknown variable(s): {{${invalid.join('}}, {{')}}}`);
    } else {
      setBodyErr('');
    }
  }, []);

  function insertPlaceholder(placeholder: string) {
    const el = bodyRef.current;
    if (!el) {
      setBody(prev => prev + placeholder);
      return;
    }
    const start = el.selectionStart ?? body.length;
    const end   = el.selectionEnd   ?? body.length;
    const next  = body.slice(0, start) + placeholder + body.slice(end);
    setBody(next);
    validateBody(next);
    // Restore cursor after the inserted text
    requestAnimationFrame(() => {
      el.selectionStart = start + placeholder.length;
      el.selectionEnd   = start + placeholder.length;
      el.focus();
    });
  }

  // Confirmation dialog
  const [confirmOpen, setConfirmOpen] = useState(false);

  function handleSaveClick() {
    validateSubject(subject);
    validateBody(body);
    if (subjectErr || bodyErr) return;
    if (showSubject && !subject.trim()) { setSubjectErr('Subject is required for Email templates.'); return; }
    if (!body.trim()) { setBodyErr('Body text is required.'); return; }
    if (findInvalidPlaceholders(body).length > 0) return;
    setConfirmOpen(true);
  }

  async function handleConfirmedSave() {
    const req: UpdateNotificationTemplateRequest = {
      channel,
      subject: showSubject ? subject : undefined,
      bodyTemplate: body,
      status,
    };
    try {
      await save({ id: template.id, body: req });
      localStorage.removeItem(`${DRAFT_KEY}:${template.id}`);
      showToast({ message: `Template "${template.name}" saved.`, severity: 'success' });
      setConfirmOpen(false);
      onClose();
    } catch {
      showToast({ message: 'Failed to save template. Please retry.', severity: 'error' });
      setConfirmOpen(false);
    }
  }

  return (
    <Box sx={{ mt: 2, pt: 2, borderTop: '1px solid', borderColor: 'divider' }}>
      <Typography variant="subtitle2" fontWeight={600} sx={{ mb: 2 }}>
        Editing: {template.name}
      </Typography>

      {/* Channel toggle */}
      <Box sx={{ mb: 2 }}>
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mb: 0.5 }}>
          Channel
        </Typography>
        <ToggleButtonGroup
          value={channel}
          exclusive
          onChange={(_, v: NotificationChannel | null) => { if (v) { setChannel(v); validateSubject(subject); } }}
          size="small"
          aria-label="Notification channel"
        >
          {CHANNELS.map(c => (
            <ToggleButton key={c} value={c} aria-label={c}>{c}</ToggleButton>
          ))}
        </ToggleButtonGroup>
      </Box>

      {/* Status toggle */}
      <Box sx={{ mb: 2 }}>
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mb: 0.5 }}>
          Status
        </Typography>
        <ToggleButtonGroup
          value={status}
          exclusive
          onChange={(_, v: NotificationStatus | null) => { if (v) setStatus(v); }}
          size="small"
          aria-label="Template status"
        >
          {STATUSES.map(s => (
            <ToggleButton key={s} value={s} aria-label={s}>{s}</ToggleButton>
          ))}
        </ToggleButtonGroup>
      </Box>

      {/* Subject */}
      {showSubject && (
        <TextField
          label="Subject Line"
          value={subject}
          onChange={e => { setSubject(e.target.value); validateSubject(e.target.value); }}
          onBlur={() => validateSubject(subject)}
          error={!!subjectErr}
          helperText={subjectErr || ' '}
          size="small"
          fullWidth
          sx={{ mb: 2 }}
          inputProps={{ 'aria-label': 'Email subject line', maxLength: 200 }}
        />
      )}

      {/* Variable toolbar + body */}
      <Box sx={{ mb: bodyErr ? 0 : 2 }}>
        <VariablePlaceholderToolbar onInsert={insertPlaceholder} />
        <TextField
          label="Body Text"
          value={body}
          onChange={e => { setBody(e.target.value); validateBody(e.target.value); }}
          onBlur={() => validateBody(body)}
          error={!!bodyErr}
          multiline
          rows={6}
          fullWidth
          size="small"
          inputProps={{
            'aria-label': 'Template body text',
            ref: (el: HTMLTextAreaElement | null) => { bodyRef.current = el; },
          }}
        />
        {bodyErr && (
          <FormHelperText error role="alert">{bodyErr}</FormHelperText>
        )}
      </Box>

      {/* Preview */}
      <Box sx={{ mb: 2 }}>
        <TemplatePreview subject={subject} body={body} channel={channel} />
      </Box>

      {/* Actions */}
      <Box sx={{ display: 'flex', gap: 1 }}>
        <Button
          variant="contained"
          size="small"
          onClick={handleSaveClick}
          disabled={isSaving || !!subjectErr || !!bodyErr}
          aria-label="Save template changes"
          startIcon={isSaving ? <CircularProgress size={14} color="inherit" /> : undefined}
        >
          {isSaving ? 'Saving…' : 'Save Changes'}
        </Button>
        <Button size="small" variant="outlined" onClick={onClose} aria-label="Cancel editing template">
          Cancel
        </Button>
      </Box>

      {/* Confirmation dialog (UXR-102) */}
      <Dialog
        open={confirmOpen}
        onClose={() => setConfirmOpen(false)}
        aria-labelledby="save-template-dialog-title"
        maxWidth="xs"
        fullWidth
      >
        <DialogTitle id="save-template-dialog-title">Save Template Changes?</DialogTitle>
        <DialogContent>
          <Typography variant="body2">
            Saving will update <strong>{template.name}</strong>. All future notifications will use the new template immediately. Already-sent messages are not affected.
          </Typography>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setConfirmOpen(false)} aria-label="Cancel save">Cancel</Button>
          <Button
            variant="contained"
            onClick={() => void handleConfirmedSave()}
            disabled={isSaving}
            aria-label="Confirm save template"
          >
            Confirm Save
          </Button>
        </DialogActions>
      </Dialog>
    </Box>
  );
}

// ─── Main Panel ───────────────────────────────────────────────────────────────

export default function NotificationTemplatesPanel() {
  const { data, isLoading, isError, refetch } = useNotificationTemplates();
  const templates = data?.templates ?? FALLBACK_TEMPLATES;

  const [editingId, setEditingId] = useState<string | null>(null);

  if (isLoading) {
    return (
      <Box>
        {[0, 1, 2, 3].map(i => (
          <Skeleton key={i} variant="text" height={40} sx={{ mb: 0.5 }} />
        ))}
      </Box>
    );
  }

  if (isError) {
    return (
      <Alert severity="error" action={
        <Button color="inherit" size="small" onClick={() => void refetch()}>Retry</Button>
      }>
        Failed to load notification templates.
      </Alert>
    );
  }

  if (templates.length === 0) {
    return (
      <Box sx={{ textAlign: 'center', py: 4 }}>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          No notification templates configured yet.
        </Typography>
        <Button variant="contained" size="small" aria-label="Add first notification template">
          + Add Template
        </Button>
      </Box>
    );
  }

  const editingTemplate = templates.find(t => t.id === editingId) ?? null;

  return (
    <Box>
      <Typography variant="subtitle1" fontWeight={600} sx={{ mb: 2 }}>
        Notification Templates
      </Typography>

      <TableContainer>
        <Table size="small" aria-label="Notification templates">
          <TableHead>
            <TableRow>
              <TableCell>Template</TableCell>
              <TableCell>Channel</TableCell>
              <TableCell>Trigger</TableCell>
              <TableCell>Status</TableCell>
              <TableCell align="right">Actions</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {templates.map(tmpl => (
              <TableRow key={tmpl.id} hover selected={tmpl.id === editingId}>
                <TableCell>{tmpl.name}</TableCell>
                <TableCell>{tmpl.channel}</TableCell>
                <TableCell>{tmpl.trigger}</TableCell>
                <TableCell>
                  <Chip
                    label={tmpl.status}
                    size="small"
                    color={statusColor(tmpl.status)}
                    variant="outlined"
                  />
                </TableCell>
                <TableCell align="right">
                  <Button
                    size="small"
                    variant={tmpl.id === editingId ? 'contained' : 'outlined'}
                    onClick={() => setEditingId(prev => (prev === tmpl.id ? null : tmpl.id))}
                    aria-label={tmpl.id === editingId ? `Close editor for ${tmpl.name}` : `Edit ${tmpl.name}`}
                    aria-expanded={tmpl.id === editingId}
                  >
                    {tmpl.id === editingId ? 'Close' : 'Edit'}
                  </Button>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>

      {/* Inline edit form — shown below the table when a template is selected */}
      {editingTemplate && (
        <EditForm
          key={editingTemplate.id}
          template={editingTemplate}
          onClose={() => setEditingId(null)}
        />
      )}
    </Box>
  );
}
