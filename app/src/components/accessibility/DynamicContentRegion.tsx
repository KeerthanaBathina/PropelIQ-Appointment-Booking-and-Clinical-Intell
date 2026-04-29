import type { ReactNode } from 'react';

interface DynamicContentRegionProps {
  children: ReactNode;
  /**
   * Descriptive label for the live region — announced by screen readers when the
   * region's role is resolved.  Examples: `"AI assistant response"`, `"Queue updates"`.
   */
  ariaLabel: string;
  /**
   * Announcement urgency:
   * - `"polite"` (default) — screen reader waits for the user to finish before announcing.
   *   Use for AI responses, queue position changes, upload progress.
   * - `"assertive"` — immediately interrupts the screen reader.
   *   Use for toast notifications and critical error banners.
   */
  priority?: 'polite' | 'assertive';
  /**
   * When `true` the entire region content is re-announced on every change.
   * When `false` (default) only the changed portion is announced.
   *
   * Set `true` for toast notifications where the complete message must be read.
   * Leave `false` for chat/queue lists where only new items should be announced.
   */
  atomic?: boolean;
  /**
   * Controls which changes trigger an announcement:
   * - `"additions"` — only new nodes (chat messages appended to a list).
   * - `"additions text"` (default) — new nodes AND text changes (queue position number).
   * - `"all"` — additions, removals, and text changes.
   * - `"removals"` — removed nodes only (rarely useful for AT users).
   * - `"text"` — text changes only.
   */
  relevant?: 'additions' | 'removals' | 'text' | 'all' | 'additions text';
}

/**
 * Wrapper that marks a DOM subtree as an ARIA live region (US_100, edge case 2, AC-5).
 *
 * Use this component to ensure screen readers receive real-time updates for dynamic
 * content — the same information sighted users see visually:
 *
 * ```tsx
 * // SCR-008: AI conversational intake — only new messages announced
 * <DynamicContentRegion ariaLabel="AI assistant response" relevant="additions">
 *   {messages.map(m => <ChatBubble key={m.id} {...m} />)}
 * </DynamicContentRegion>
 *
 * // SCR-011: Arrival queue — position number and text changes announced
 * <DynamicContentRegion ariaLabel="Patient queue updates" relevant="additions text">
 *   <Typography>Your position: #{queuePosition}</Typography>
 *   <Typography>Estimated wait: {estimatedWait} minutes</Typography>
 * </DynamicContentRegion>
 *
 * // Toast notification — full message announced immediately
 * <DynamicContentRegion ariaLabel="Notification" priority="assertive" atomic>
 *   {activeToast && <Alert severity={activeToast.severity}>{activeToast.message}</Alert>}
 * </DynamicContentRegion>
 * ```
 *
 * WCAG references: 4.1.3 Status Messages, 1.3.1 Info and Relationships.
 */
export function DynamicContentRegion({
  children,
  ariaLabel,
  priority = 'polite',
  atomic = false,
  relevant = 'additions text',
}: DynamicContentRegionProps) {
  return (
    <div
      aria-live={priority}
      aria-atomic={atomic}
      aria-relevant={relevant}
      aria-label={ariaLabel}
      role={priority === 'assertive' ? 'alert' : 'status'}
    >
      {children}
    </div>
  );
}
