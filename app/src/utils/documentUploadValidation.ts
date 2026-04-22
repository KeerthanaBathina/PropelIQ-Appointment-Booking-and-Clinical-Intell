/**
 * documentUploadValidation.ts
 *
 * Client-side validation helpers for the SCR-012 Document Upload screen (US_038).
 *
 * Rules per AC-5:
 *   - Allowed formats: PDF, DOCX, TXT, PNG, JPG/JPEG
 *   - Maximum file size: 10 MB
 *
 * EC-2: A file with a valid extension but corrupt content passes client-side
 * validation and is submitted; server-side parsing errors are surfaced later.
 */

// ─── Constants ────────────────────────────────────────────────────────────────

export const ALLOWED_MIME_TYPES: Readonly<Record<string, string>> = {
  'application/pdf':                                                       'PDF',
  'application/vnd.openxmlformats-officedocument.wordprocessingml.document': 'DOCX',
  'text/plain':                                                            'TXT',
  'image/png':                                                             'PNG',
  'image/jpeg':                                                            'JPG',
};

export const ALLOWED_EXTENSIONS = ['.pdf', '.docx', '.txt', '.png', '.jpg', '.jpeg'] as const;

export const MAX_FILE_SIZE_BYTES = 10 * 1024 * 1024; // 10 MB

export const MAX_FILE_SIZE_LABEL = '10 MB';

export const ALLOWED_FORMATS_LABEL = Object.values(ALLOWED_MIME_TYPES).join(', ');

// ─── Validation result ────────────────────────────────────────────────────────

export interface FileValidationResult {
  valid: boolean;
  /** Human-readable error message when valid is false. */
  error?: string;
}

// ─── Helpers ─────────────────────────────────────────────────────────────────

/** Returns true when the file's MIME type or extension is in the allowed set. */
function isAllowedType(file: File): boolean {
  if (ALLOWED_MIME_TYPES[file.type]) return true;
  const lower = file.name.toLowerCase();
  return ALLOWED_EXTENSIONS.some(ext => lower.endsWith(ext));
}

/**
 * Validates a single File against the US_038 format and size rules (AC-5).
 *
 * Returns `{ valid: true }` when all checks pass, or `{ valid: false, error }` with a
 * descriptive message that lists supported formats and the size limit.
 */
export function validateUploadFile(file: File): FileValidationResult {
  if (!isAllowedType(file)) {
    return {
      valid: false,
      error:
        `"${file.name}" has an unsupported format. ` +
        `Supported formats: ${ALLOWED_FORMATS_LABEL}. ` +
        `Please select a PDF, DOCX, TXT, PNG, or JPG file.`,
    };
  }

  if (file.size > MAX_FILE_SIZE_BYTES) {
    const sizeMb = (file.size / (1024 * 1024)).toFixed(1);
    return {
      valid: false,
      error:
        `"${file.name}" is ${sizeMb} MB and exceeds the ${MAX_FILE_SIZE_LABEL} limit. ` +
        `Please reduce the file size or select a different file.`,
    };
  }

  return { valid: true };
}

/**
 * Validates an array of files and returns separate arrays for valid and invalid entries.
 * The invalid array preserves the error message for each rejected file.
 */
export function validateUploadFiles(files: File[]): {
  valid: File[];
  invalid: Array<{ file: File; error: string }>;
} {
  const valid: File[] = [];
  const invalid: Array<{ file: File; error: string }> = [];

  for (const file of files) {
    const result = validateUploadFile(file);
    if (result.valid) {
      valid.push(file);
    } else {
      invalid.push({ file, error: result.error! });
    }
  }

  return { valid, invalid };
}
