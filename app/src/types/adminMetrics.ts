/**
 * TypeScript interfaces for the Admin Metrics Dashboard API (US_058, SCR-015).
 * Consumed by useAdminMetrics hook and admin dashboard sub-components.
 *
 * Endpoint: GET /api/admin/metrics
 * Endpoint: GET /api/admin/metrics/trends?period=7d|30d
 */

// ─── Snapshot ─────────────────────────────────────────────────────────────────

/** The five KPIs displayed on the admin dashboard (US_058 AC-1). */
export interface AdminMetricsSnapshot {
  /** Number of users with an active session in the last 24 h. */
  activeUsers: number;
  /** Total appointments booked today. */
  dailyAppointments: number;
  /** No-show rate as a percentage (0–100). */
  noShowRate: number;
  /** AI coding agreement rate as a percentage (0–100). */
  aiAgreementRate: number;
  /** System uptime percentage over the last 30 days (0–100). */
  uptimePercent: number;
}

export interface AdminMetricsResponse {
  metrics: AdminMetricsSnapshot;
  /** ISO 8601 UTC timestamp when these metrics were last computed. */
  generatedAt: string;
  /** True when the values are from the Redis cache and the backing query failed. */
  isStale: boolean;
}

// ─── Trends ──────────────────────────────────────────────────────────────────

/** A single data point in a rolling trend series (US_058 AC-2). */
export interface MetricTrendPoint {
  /** ISO 8601 date string, e.g. "2026-04-17". */
  date: string;
  activeUsers: number;
  dailyAppointments: number;
  noShowRate: number;
  aiAgreementRate: number;
  uptimePercent: number;
}

export type TrendPeriod = '7d' | '30d';

export interface AdminMetricsTrendsResponse {
  period: TrendPeriod;
  dataPoints: MetricTrendPoint[];
  /** ISO 8601 UTC timestamp when this trend series was computed. */
  generatedAt: string;
}
