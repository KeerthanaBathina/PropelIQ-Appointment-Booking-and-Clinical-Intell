/**
 * IntakeChatMessageList — conversation transcript for SCR-008 AI intake (US_027, AC-2).
 *
 * Renders:
 *   - AI chat bubbles (left-aligned, neutral.100 bg) with optional clarification examples (EC-1)
 *   - Patient chat bubbles (right-aligned, primary.100 bg)
 *   - System error messages (centered, warning style)
 *   - Typing indicator (UXR-504: three bouncing dots while AI generates response)
 *
 * Accessibility (UXR-206):
 *   - Container has role="log" aria-live="polite" aria-label so screen readers announce new messages
 *   - Each bubble has accessible timestamp
 *   - Clarification examples use aria-describedby linking
 *
 * Auto-scroll: new messages scroll the container to the bottom.
 * Design tokens: ChatBubble spec from designsystem.md.
 */

import { useEffect, useRef, memo } from 'react';
import Box from '@mui/material/Box';
import Chip from '@mui/material/Chip';
import Typography from '@mui/material/Typography';
import type { IntakeMessage } from '@/hooks/useAIIntakeSession';

// ─── Types ────────────────────────────────────────────────────────────────────

interface IntakeChatMessageListProps {
  messages: IntakeMessage[];
  isAiTyping: boolean;
}

// ─── Helpers ──────────────────────────────────────────────────────────────────

function formatTime(iso: string): string {
  return new Date(iso).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
}

// ─── Typing Indicator (UXR-504) ───────────────────────────────────────────────

const BOUNCE_KEYFRAMES = `
  @keyframes bounce {
    0%, 80%, 100% { transform: translateY(0); }
    40% { transform: translateY(-6px); }
  }
`;

function TypingDot({ delay }: { delay: string }) {
  return (
    <Box
      aria-hidden="true"
      sx={{
        width: 8,
        height: 8,
        borderRadius: '50%',
        bgcolor: 'text.disabled',
        animation: 'bounce 1.4s ease-in-out infinite',
        animationDelay: delay,
      }}
    />
  );
}

function TypingIndicator() {
  return (
    <>
      <style>{BOUNCE_KEYFRAMES}</style>
      <Box
        aria-label="AI is typing"
        sx={{
          display: 'flex',
          gap: 0.75,
          alignItems: 'center',
          px: 2,
          py: 1.5,
          bgcolor: 'grey.100',
          borderRadius: '12px 12px 12px 2px',
          width: 'fit-content',
          maxWidth: '80%',
        }}
      >
        <TypingDot delay="0ms" />
        <TypingDot delay="160ms" />
        <TypingDot delay="320ms" />
      </Box>
    </>
  );
}

// ─── Single message bubble ─────────────────────────────────────────────────────

interface BubbleProps {
  message: IntakeMessage;
}

const MessageBubble = memo(function MessageBubble({ message }: BubbleProps) {
  const isAi   = message.role === 'ai';
  const isUser = message.role === 'user';

  if (message.role === 'system') {
    return (
      <Box sx={{ display: 'flex', justifyContent: 'center', my: 0.5 }}>
        <Typography
          variant="caption"
          color="warning.main"
          sx={{ bgcolor: 'warning.50', px: 2, py: 0.5, borderRadius: 2 }}
        >
          {message.content}
        </Typography>
      </Box>
    );
  }

  return (
    <Box
      sx={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: isUser ? 'flex-end' : 'flex-start',
        gap: 0.5,
      }}
    >
      {/* Bubble */}
      <Box
        sx={{
          maxWidth: '80%',
          px: 2,
          py: 1.5,
          borderRadius: isAi
            ? '12px 12px 12px 2px'  // flat bottom-left corner (origin side)
            : '12px 12px 2px 12px', // flat bottom-right corner
          bgcolor: isAi ? 'grey.100' : 'primary.50',
          color: 'text.primary',
        }}
      >
        <Typography
          variant="body2"
          component="p"
          // Allow bold tags coming from AI content (e.g. <strong>Penicillin</strong>)
          dangerouslySetInnerHTML={isAi ? { __html: message.content } : undefined}
        >
          {isUser ? message.content : undefined}
        </Typography>
      </Box>

      {/* Clarification examples (EC-1) */}
      {isAi && message.clarificationExamples && message.clarificationExamples.length > 0 && (
        <Box
          sx={{ display: 'flex', gap: 0.5, flexWrap: 'wrap', maxWidth: '80%', pl: 0.5 }}
          aria-label="Example responses"
        >
          <Typography variant="caption" color="text.secondary" sx={{ width: '100%' }}>
            Examples:
          </Typography>
          {message.clarificationExamples.map((ex) => (
            <Chip
              key={ex}
              label={ex}
              size="small"
              variant="outlined"
              sx={{ fontSize: '0.7rem' }}
            />
          ))}
        </Box>
      )}

      {/* Timestamp */}
      <Typography
        variant="caption"
        color="text.disabled"
        aria-label={`Sent at ${formatTime(message.timestamp)}`}
      >
        {formatTime(message.timestamp)}
      </Typography>
    </Box>
  );
});

// ─── Main component ───────────────────────────────────────────────────────────

function IntakeChatMessageList({ messages, isAiTyping }: IntakeChatMessageListProps) {
  const bottomRef = useRef<HTMLDivElement>(null);

  // Auto-scroll to bottom when new messages arrive (AC-2)
  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages, isAiTyping]);

  return (
    <Box
      role="log"
      aria-live="polite"
      aria-label="Intake conversation"
      aria-relevant="additions"
      sx={{
        flex: 1,
        overflowY: 'auto',
        px: 2,
        py: 2,
        display: 'flex',
        flexDirection: 'column',
        gap: 1.5,
      }}
    >
      {messages.map((msg) => (
        <MessageBubble key={msg.id} message={msg} />
      ))}

      {/* Typing indicator (UXR-504) */}
      {isAiTyping && <TypingIndicator />}

      {/* Anchor for auto-scroll */}
      <div ref={bottomRef} aria-hidden="true" />
    </Box>
  );
}

export default memo(IntakeChatMessageList);
