/**
 * useAdminConfig — React Query hooks for admin configuration CRUD (US_058 AC-4).
 *
 * GET/PUT hooks per config category:
 *   useSlotTemplates()         → GET /api/admin/config/slots
 *   useUpdateSlotTemplates()   → PUT /api/admin/config/slots
 *   useNotificationTemplates() → GET /api/admin/config/notifications
 *   useUpdateNotification()    → PUT /api/admin/config/notifications/:id
 *   useBusinessHours()         → GET /api/admin/config/hours
 *   useUpdateBusinessHours()   → PUT /api/admin/config/hours
 *   useRiskThresholds()        → GET /api/admin/config/risk-thresholds
 *   useUpdateRiskThresholds()  → PUT /api/admin/config/risk-thresholds
 */

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiGet, apiPut, apiPost } from '@/lib/apiClient';
import type {
  SlotTemplatesConfig,
  NotificationTemplatesConfig,
  NotificationTemplate,
  UpdateNotificationTemplateRequest,
  BusinessHoursConfig,
  RiskThresholdsConfig,
  // US_059
  SlotTemplateResponse,
  UpsertSlotTemplateRequest,
  AffectedAppointmentsResponse,
  BusinessHoursEntryDto,
  UpdateBusinessHoursRequest,
  HolidayResponse,
  CreateHolidayRequest,
  AddHolidayResponse,
} from '@/types/adminConfig';
import { apiDelete } from '@/lib/apiClient';

const TEN_MINUTES = 10 * 60 * 1_000;

// ─── Slot Templates ───────────────────────────────────────────────────────────

export function useSlotTemplates() {
  return useQuery<SlotTemplatesConfig>({
    queryKey: ['admin-config-slots'],
    queryFn:  () => apiGet('/api/admin/config/slots'),
    staleTime: TEN_MINUTES,
  });
}

export function useUpdateSlotTemplates() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (data: SlotTemplatesConfig) =>
      apiPut<SlotTemplatesConfig>('/api/admin/config/slots', data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin-config-slots'] }),
  });
}

// ─── Notification Templates ───────────────────────────────────────────────────

export function useNotificationTemplates() {
  return useQuery<NotificationTemplatesConfig>({
    queryKey: ['admin-config-notifications'],
    queryFn:  () => apiGet('/api/admin/config/notifications'),
    staleTime: TEN_MINUTES,
  });
}

export function useUpdateNotificationTemplate() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (tmpl: NotificationTemplate) =>
      apiPut<NotificationTemplate>(`/api/admin/config/notifications/${tmpl.id}`, tmpl),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin-config-notifications'] }),
  });
}

/** PUT /api/admin/config/notifications/:id — targeted update (US_060) */
export function useUpdateNotificationTemplateById() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({ id, body }: { id: string; body: UpdateNotificationTemplateRequest }) =>
      apiPut<NotificationTemplate>(`/api/admin/config/notifications/${id}`, body),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin-config-notifications'] }),
  });
}

export function useAddNotificationTemplate() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (tmpl: Omit<NotificationTemplate, 'id'>) =>
      apiPost<NotificationTemplate>('/api/admin/config/notifications', tmpl),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin-config-notifications'] }),
  });
}

// ─── Business Hours ───────────────────────────────────────────────────────────

export function useBusinessHours() {
  return useQuery<BusinessHoursConfig>({
    queryKey: ['admin-config-hours'],
    queryFn:  () => apiGet('/api/admin/config/hours'),
    staleTime: TEN_MINUTES,
  });
}

export function useUpdateBusinessHours() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (data: BusinessHoursConfig) =>
      apiPut<BusinessHoursConfig>('/api/admin/config/hours', data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin-config-hours'] }),
  });
}

// ─── Risk Thresholds ──────────────────────────────────────────────────────────

export function useRiskThresholds() {
  return useQuery<RiskThresholdsConfig>({
    queryKey: ['admin-config-risk-thresholds'],
    queryFn:  () => apiGet('/api/admin/config/risk-thresholds'),
    staleTime: TEN_MINUTES,
  });
}

export function useUpdateRiskThresholds() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (data: RiskThresholdsConfig) =>
      apiPut<RiskThresholdsConfig>('/api/admin/config/risk-thresholds', data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin-config-risk-thresholds'] }),
  });
}

// ─── US_059: Per-provider slot templates ─────────────────────────────────────

const FIVE_MINUTES = 5 * 60 * 1_000;

/** GET /api/admin/config/slots/{providerId} — all day templates for a provider */
export function useSlotTemplatesByProvider(providerId: string | null) {
  return useQuery<SlotTemplateResponse[]>({
    queryKey: ['admin-slot-templates', providerId],
    queryFn:  () => apiGet(`/api/admin/config/slots/${providerId}`),
    enabled:  !!providerId,
    staleTime: FIVE_MINUTES,
  });
}

/** PUT /api/admin/config/slots/{providerId}/{dayOfWeek} — upsert one day template */
export function useUpsertSlotTemplate() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: ({
      providerId,
      dayOfWeek,
      body,
    }: {
      providerId: string;
      dayOfWeek: number;
      body: UpsertSlotTemplateRequest;
    }) => apiPut<SlotTemplateResponse>(`/api/admin/config/slots/${providerId}/${dayOfWeek}`, body),
    onSuccess: (_data, { providerId }) => {
      qc.invalidateQueries({ queryKey: ['admin-slot-templates', providerId] });
    },
  });
}

/** GET /api/admin/config/slots/{providerId}/{dayOfWeek}/affected — preview impact */
export function useAffectedSlotAppointments(
  providerId: string | null,
  dayOfWeek: number | null,
  enabled: boolean,
) {
  return useQuery<AffectedAppointmentsResponse>({
    queryKey: ['admin-slot-affected', providerId, dayOfWeek],
    queryFn:  () =>
      apiGet(`/api/admin/config/slots/${providerId}/${dayOfWeek}/affected`),
    enabled: !!providerId && dayOfWeek !== null && enabled,
    staleTime: 0, // always fresh when explicitly requested
  });
}

// ─── US_059: Structured business hours ───────────────────────────────────────

/** GET /api/admin/config/business-hours — structured 7-day schedule */
export function useStructuredBusinessHours() {
  return useQuery<BusinessHoursEntryDto[]>({
    queryKey: ['admin-business-hours'],
    queryFn:  () => apiGet('/api/admin/config/business-hours'),
    staleTime: FIVE_MINUTES,
  });
}

/** PUT /api/admin/config/business-hours — bulk update */
export function useUpdateStructuredBusinessHours() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (data: UpdateBusinessHoursRequest) =>
      apiPut<BusinessHoursEntryDto[]>('/api/admin/config/business-hours', data),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin-business-hours'] }),
  });
}

// ─── US_059: Holidays ─────────────────────────────────────────────────────────

/** GET /api/admin/config/holidays */
export function useHolidaysList() {
  return useQuery<HolidayResponse[]>({
    queryKey: ['admin-holidays'],
    queryFn:  () => apiGet('/api/admin/config/holidays'),
    staleTime: FIVE_MINUTES,
  });
}

/** POST /api/admin/config/holidays */
export function useAddHoliday() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (req: CreateHolidayRequest) =>
      apiPost<AddHolidayResponse>('/api/admin/config/holidays', req),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin-holidays'] }),
  });
}

/** DELETE /api/admin/config/holidays/{id} — soft delete */
export function useRemoveHoliday() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: (holidayId: string) =>
      apiDelete<void>(`/api/admin/config/holidays/${holidayId}`),
    onSuccess: () => qc.invalidateQueries({ queryKey: ['admin-holidays'] }),
  });
}
