import { useContext } from 'react';
import { LiveAnnouncerContext, type LiveAnnouncerContextType } from '@/components/accessibility/LiveAnnouncer';

/**
 * Returns the `announce` function from the nearest `LiveAnnouncerProvider` (US_100, AC-5).
 *
 * Usage:
 * ```tsx
 * const { announce } = useLiveAnnouncer();
 *
 * // Non-urgent — waits for user to finish current interaction
 * announce('Queue position updated: you are now #3');
 *
 * // Urgent — interrupts screen reader immediately
 * announce('Email address is required', 'assertive');
 * ```
 *
 * @throws When called outside a `LiveAnnouncerProvider`.
 */
export function useLiveAnnouncer(): LiveAnnouncerContextType {
  const context = useContext(LiveAnnouncerContext);
  if (!context) {
    throw new Error('useLiveAnnouncer must be used within a LiveAnnouncerProvider');
  }
  return context;
}
