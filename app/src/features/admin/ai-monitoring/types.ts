/**
 * TypeScript interfaces for the AI Monitoring Dashboard (US_072).
 * Match the AiMetricsSummaryDto, AiMetricsTimeSeriesDto, and AiMetricAlertDto
 * returned by GET /api/admin/ai-metrics/*.
 */

// ─── Latency sub-object ───────────────────────────────────────────────────────

export interface OperationLatency {
  operationType: string;
  p50Milliseconds: number;
  p95Milliseconds: number;
  targetP95Milliseconds: number;
  sampleSize: number;
  meetsTarget: boolean;
}

// ─── Summary DTO ──────────────────────────────────────────────────────────────

export interface AiMetricsSummary {
  codingAgreementRate: number;
  codingAgreementTarget: number;
  codingSampleSize: number;
  extractionPrecision: number;
  extractionRecall: number;
  extractionTarget: number;
  extractionSampleSize: number;
  /** Minimum sample count for a metric to be statistically valid. Default: 30. */
  minSampleSize: number;
  codingAgreementTrend: TrendDirection;
  extractionPrecisionTrend: TrendDirection;
  extractionRecallTrend: TrendDirection;
  latencies: OperationLatency[];
  activeAlertCount: number;
  lastCalculatedAt: string | null;
}

// ─── Time-series DTO ─────────────────────────────────────────────────────────

export interface TimeSeriesDataPoint {
  date: string;
  value: number;
  sampleSize: number;
}

export interface AiMetricsTimeSeries {
  metricType: string;
  granularity: string;
  targetValue: number;
  dataPoints: TimeSeriesDataPoint[];
}

// ─── Alert DTO ───────────────────────────────────────────────────────────────

export interface AiMetricAlert {
  alertId: string;
  generatedAt: string;
  metricName: string;
  currentValue: number;
  targetValue: number;
  trendDirection: TrendDirection;
  isAcknowledged: boolean;
  acknowledgedByUserId: string | null;
}

// ─── Enums / literals ────────────────────────────────────────────────────────

export type TrendDirection = 'Up' | 'Down' | 'Stable';
export type Granularity = 'daily' | 'weekly' | 'monthly';
export type AccuracyMetricType = 'CodingAgreement' | 'ExtractionPrecision' | 'ExtractionRecall';
