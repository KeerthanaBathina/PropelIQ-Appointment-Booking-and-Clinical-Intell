using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Enums;
using UPACIP.Service.AI.ClinicalExtraction;
using UPACIP.Service.AI.ConversationalIntake;
using UPACIP.Service.Documents;

namespace UPACIP.Service.AI.DocumentParsing;

/// <summary>
/// Executes AI document parsing jobs for queued or synchronous fallback execution
/// (US_039 AC-2, AC-3, AC-4, AC-5, EC-1, EC-2; AIR-002, AIR-S01, AIR-O01, AIR-O07, AIR-O08).
///
/// Workflow per job:
///   1. Load the <c>ClinicalDocument</c> row; validate it is in <c>Queued</c> state (EC-2 idempotency).
///   2. Transition status to <c>Processing</c> (AC-2).
///   3. Decrypt and read document content via <see cref="IEncryptedFileStorageService"/>.
///   4. Sanitise content for PII and injection patterns (AIR-S01, AIR-S06).
///   5. Build prompt and invoke OpenAI GPT-4o-mini (primary) via tool calling (AIR-002).
///   6. Fall back to Anthropic Claude 3.5 Sonnet if the primary circuit opens or fails (AIR-002).
///   7. Validate and parse the model's structured response (AIR-O07).
///   8. Transition to <c>Completed</c> on success (AC-3), or throw to let the dispatcher's
///      Polly pipeline retry (AC-4).  After the final retry the dispatcher marks <c>Failed</c> (AC-5).
///
/// Transport-agnostic: same logic executes whether invoked from <see cref="DocumentParsingDispatcher"/>
/// (Redis queue) or the synchronous fallback path in <see cref="DocumentParsingQueueService"/> (EC-1).
///
/// Circuit breaker (AIR-O04):
///   Per-instance (scoped DI) — provider failure state is isolated per job execution,
///   consistent with <c>ConversationalIntakeService</c>.
/// </summary>
public sealed class DocumentParsingWorker : IDocumentParserWorker
{
    // ── Guardrail constants (match guardrails.json §DocumentParsing) ──────────

    private const double ConfidenceThreshold = DocumentParsingResultValidator.ConfidenceThreshold;
    private const int    MaxOutputTokens     = DocumentParsingPromptBuilder.MaxOutputTokens;

    // ── AI provider names ─────────────────────────────────────────────────────

    private const string ProviderOpenAi    = "openai";
    private const string ProviderAnthropic = "anthropic";

    // ── Supported text MIME types for direct text extraction ─────────────────

    private static readonly IReadOnlySet<string> TextContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "text/plain",
        "text/csv",
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive     = true,
        DefaultIgnoreCondition          = JsonIgnoreCondition.WhenWritingNull,
    };

    // ── Dependencies ──────────────────────────────────────────────────────────

    private readonly ApplicationDbContext                  _db;
    private readonly IEncryptedFileStorageService          _storage;
    private readonly DocumentParsingPromptBuilder          _promptBuilder;
    private readonly DocumentParsingResultValidator        _validator;
    private readonly ClinicalExtractionService             _extractionService;
    private readonly IExtractedDataPersistenceService      _persistenceService;
    private readonly IHttpClientFactory                    _httpClientFactory;
    private readonly AiGatewaySettings                     _aiSettings;
    private readonly ILogger<DocumentParsingWorker>        _logger;

    // Per-instance circuit breaker for OpenAI (scoped lifetime — per job isolation).
    private readonly AsyncCircuitBreakerPolicy _openAiCircuitBreaker;

    public DocumentParsingWorker(
        ApplicationDbContext                db,
        IEncryptedFileStorageService        storage,
        DocumentParsingPromptBuilder        promptBuilder,
        DocumentParsingResultValidator      validator,
        ClinicalExtractionService           extractionService,
        IExtractedDataPersistenceService    persistenceService,
        IHttpClientFactory                  httpClientFactory,
        IOptions<AiGatewaySettings>         aiSettings,
        ILogger<DocumentParsingWorker>      logger)
    {
        _db                 = db;
        _storage            = storage;
        _promptBuilder      = promptBuilder;
        _validator          = validator;
        _extractionService  = extractionService;
        _persistenceService = persistenceService;
        _httpClientFactory  = httpClientFactory;
        _aiSettings         = aiSettings.Value;
        _logger             = logger;

        // Circuit breaker: open after 3 consecutive failures; half-open after 30s (AIR-O04).
        _openAiCircuitBreaker = Policy
            .Handle<Exception>(ex => ex is not OperationCanceledException)
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: 3,
                durationOfBreak: TimeSpan.FromSeconds(30),
                onBreak: (ex, duration) =>
                    _logger.LogError(ex,
                        "DocumentParsingWorker: OpenAI circuit OPEN for {DurationSeconds}s.",
                        (int)duration.TotalSeconds),
                onReset: () =>
                    _logger.LogInformation("DocumentParsingWorker: OpenAI circuit CLOSED."),
                onHalfOpen: () =>
                    _logger.LogInformation("DocumentParsingWorker: OpenAI circuit HALF-OPEN."));
    }

    // ─── IDocumentParserWorker ────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task ParseAsync(Guid documentId, CancellationToken cancellationToken)
    {
        // ── Step 1: Load and validate document state (EC-2 idempotency) ──────────
        var document = await _db.ClinicalDocuments
            .FirstOrDefaultAsync(d => d.Id == documentId, cancellationToken);

        if (document is null)
        {
            _logger.LogWarning(
                "DocumentParsingWorker: document not found. DocumentId={DocumentId}", documentId);
            return;
        }

        // Idempotency: skip if not in Queued state (e.g. already Completed/Failed).
        if (document.ProcessingStatus != ProcessingStatus.Queued)
        {
            _logger.LogDebug(
                "DocumentParsingWorker: document is in {Status} state, skipping. DocumentId={DocumentId}",
                document.ProcessingStatus, documentId);
            return;
        }

        // ── Step 2: Transition to Processing (AC-2) ───────────────────────────────
        document.ProcessingStatus = ProcessingStatus.Processing;
        document.UpdatedAt        = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "DocumentParsingWorker: started. DocumentId={DocumentId} Category={Category}",
            documentId, document.DocumentCategory);

        // ── Step 3: Decrypt and read content ─────────────────────────────────────
        string documentText;
        try
        {
            var rawBytes = await _storage.ReadDecryptedAsync(document.FilePath, cancellationToken);
            documentText = ExtractTextFromBytes(rawBytes, document.ContentType ?? string.Empty, documentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "DocumentParsingWorker: failed to read/decrypt document. DocumentId={DocumentId}",
                documentId);
            // Re-throw so the dispatcher's Polly pipeline can retry (AC-4).
            throw;
        }

        // ── Step 4: Sanitise content (AIR-S01, AIR-S06) ──────────────────────────
        var sanitisedText = _validator.SanitiseDocumentContent(documentText, documentId);

        // ── Step 5: Build prompt ──────────────────────────────────────────────────
        var messages = _promptBuilder.BuildMessages(
            documentId,
            document.DocumentCategory,
            document.ContentType ?? "application/octet-stream",
            sanitisedText);

        // ── Step 6: Invoke AI model (GPT-4o-mini primary, Claude fallback) ────────
        DocumentParsingModelResult? modelResult = null;
        var provider = "fallback";

        try
        {
            modelResult = await _openAiCircuitBreaker.ExecuteAsync(
                () => CallOpenAiAsync(messages, documentId, cancellationToken));
            if (modelResult is not null) provider = ProviderOpenAi;
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning(
                "DocumentParsingWorker: OpenAI circuit open; falling back to Anthropic. DocumentId={DocumentId}",
                documentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "DocumentParsingWorker: OpenAI call failed; falling back to Anthropic. DocumentId={DocumentId}",
                documentId);
        }

        // Anthropic fallback (low confidence or primary failure).
        if (modelResult is null || modelResult.Confidence < ConfidenceThreshold)
        {
            var fallbackReason = modelResult is null
                ? "provider failure"
                : $"low-confidence={modelResult.Confidence:F2}";

            _logger.LogInformation(
                "DocumentParsingWorker: Anthropic fallback triggered; reason={Reason}. DocumentId={DocumentId}",
                fallbackReason, documentId);

            try
            {
                var anthropicResult = await CallAnthropicAsync(messages, documentId, cancellationToken);
                if (anthropicResult is not null)
                {
                    modelResult = anthropicResult;
                    provider    = ProviderAnthropic;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "DocumentParsingWorker: Anthropic fallback also failed. DocumentId={DocumentId}",
                    documentId);
            }
        }

        // ── Step 7: Handle no-model result — rethrow so dispatcher retries (AC-4) ─
        if (modelResult is null)
        {
            _logger.LogError(
                "DocumentParsingWorker: all providers failed. DocumentId={DocumentId}",
                documentId);
            // Reset to Queued so retry attempt starts cleanly.
            document.ProcessingStatus = ProcessingStatus.Queued;
            document.UpdatedAt        = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
            throw new InvalidOperationException(
                $"All AI providers failed for DocumentId={documentId}. Will retry.");
        }

        // ── Step 8: Transition to Completed (AC-3) ────────────────────────────────
        document.ProcessingStatus = ProcessingStatus.Completed;
        document.UpdatedAt        = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "DocumentParsingWorker: parsing complete. " +
            "DocumentId={DocumentId} Provider={Provider} Confidence={Confidence:F2} " +
            "ExtractionPossible={Possible} FieldCount={Fields}",
            documentId, provider, modelResult.Confidence,
            modelResult.ExtractionPossible, modelResult.ExtractedFields.Count);

        // ── Step 9: Clinical data extraction + persistence (US_040 AC-1–AC-5) ────
        // Extraction and persistence are best-effort: failures must NOT roll back the
        // parsing Completed status set in Step 8 (task_002 atomicity constraint).
        try
        {
            var extractionResult = await _extractionService.ExtractAsync(
                documentId,
                document.DocumentCategory,
                sanitisedText,
                cancellationToken);

            var outcome = await _persistenceService.PersistAsync(
                documentId,
                extractionResult,
                cancellationToken);

            _logger.LogInformation(
                "DocumentParsingWorker: extraction persistence complete. " +
                "DocumentId={DocumentId} Outcome={Outcome} Total={Total} ManualReview={Review} " +
                "LowConfidence={Low} ConfidenceUnavailable={Unavail}",
                documentId, outcome.Outcome, outcome.TotalPersisted, outcome.RequiresManualReview,
                outcome.LowConfidenceCount, outcome.ConfidenceUnavailableCount);
        }
        catch (Exception ex)
        {
            // Swallow extraction/persistence errors: document is already Completed for parsing.
            _logger.LogError(ex,
                "DocumentParsingWorker: clinical extraction or persistence failed (non-fatal). " +
                "DocumentId={DocumentId}",
                documentId);
        }
    }

    // ─── AI provider calls ────────────────────────────────────────────────────────

    private async Task<DocumentParsingModelResult?> CallOpenAiAsync(
        IReadOnlyList<(string Role, string Content)> messages,
        Guid              documentId,
        CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("openai");

        var requestBody = new
        {
            model       = _aiSettings.OpenAiModel,
            messages    = messages.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
            tools       = JsonDocument.Parse(DocumentParsingPromptBuilder.GetToolDefinitionJson()).RootElement,
            tool_choice = JsonDocument.Parse(DocumentParsingPromptBuilder.GetToolChoiceJson()).RootElement,
            max_tokens  = MaxOutputTokens,
        };

        using var response = await client.PostAsJsonAsync(
            "/v1/chat/completions", requestBody, JsonOptions, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError(
                "DocumentParsingWorker: OpenAI HTTP {Status}. DocumentId={DocumentId}",
                (int)response.StatusCode, documentId);
            throw new HttpRequestException(
                $"OpenAI returned {(int)response.StatusCode}.", null, response.StatusCode);
        }

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return ParseOpenAiToolCallResponse(json, documentId);
    }

    private async Task<DocumentParsingModelResult?> CallAnthropicAsync(
        IReadOnlyList<(string Role, string Content)> messages,
        Guid              documentId,
        CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("anthropic");

        // Build Anthropic messages API request — system prompt separated from messages array.
        var systemPrompt  = messages.FirstOrDefault(m => m.Role == "system").Content ?? string.Empty;
        var userMessages  = messages.Where(m => m.Role != "system")
                                    .Select(m => new { role = m.Role, content = m.Content })
                                    .ToArray();

        var toolSchema = JsonDocument.Parse(DocumentParsingPromptBuilder.GetToolDefinitionJson()).RootElement;

        var requestBody = new
        {
            model      = _aiSettings.AnthropicModel,
            max_tokens = MaxOutputTokens,
            system     = systemPrompt,
            messages   = userMessages,
            tools      = toolSchema.ValueKind == JsonValueKind.Array
                ? toolSchema.EnumerateArray().Select(t => t).ToArray()
                : Array.Empty<JsonElement>(),
            tool_choice = new { type = "auto" },
        };

        using var response = await client.PostAsJsonAsync(
            "/v1/messages", requestBody, JsonOptions, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError(
                "DocumentParsingWorker: Anthropic HTTP {Status}. DocumentId={DocumentId}",
                (int)response.StatusCode, documentId);
            throw new HttpRequestException(
                $"Anthropic returned {(int)response.StatusCode}.", null, response.StatusCode);
        }

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
        return ParseAnthropicToolCallResponse(json, documentId);
    }

    // ─── Response parsers ─────────────────────────────────────────────────────────

    private DocumentParsingModelResult? ParseOpenAiToolCallResponse(JsonElement json, Guid documentId)
    {
        try
        {
            var choices = json.GetProperty("choices");
            if (choices.GetArrayLength() == 0) return null;

            var message   = choices[0].GetProperty("message");
            var toolCalls = message.GetProperty("tool_calls");
            if (toolCalls.GetArrayLength() == 0) return null;

            var firstCall = toolCalls[0];
            var funcArgs  = firstCall.GetProperty("function").GetProperty("arguments").GetString()
                            ?? string.Empty;

            return _validator.ValidateToolCallArguments(funcArgs, documentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "DocumentParsingWorker: failed to parse OpenAI tool-call response. DocumentId={DocumentId}",
                documentId);
            return null;
        }
    }

    private DocumentParsingModelResult? ParseAnthropicToolCallResponse(JsonElement json, Guid documentId)
    {
        try
        {
            // Anthropic messages response: content[] with type="tool_use" blocks.
            var content = json.GetProperty("content");
            foreach (var block in content.EnumerateArray())
            {
                if (block.TryGetProperty("type", out var typeEl) &&
                    typeEl.GetString() == "tool_use" &&
                    block.TryGetProperty("input", out var inputEl))
                {
                    var inputJson = inputEl.GetRawText();
                    return _validator.ValidateToolCallArguments(inputJson, documentId);
                }
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "DocumentParsingWorker: failed to parse Anthropic tool-call response. DocumentId={DocumentId}",
                documentId);
            return null;
        }
    }

    // ─── Document content extraction ─────────────────────────────────────────────

    /// <summary>
    /// Converts raw decrypted bytes to a text string for prompt embedding.
    /// Binary formats (PDF, DOCX, images) produce a placeholder until an OCR layer is added.
    /// Plain-text formats are decoded directly.
    /// </summary>
    private string ExtractTextFromBytes(byte[] bytes, string contentType, Guid documentId)
    {
        if (bytes.Length == 0)
        {
            _logger.LogWarning(
                "DocumentParsingWorker: document bytes are empty. DocumentId={DocumentId}", documentId);
            return string.Empty;
        }

        if (TextContentTypes.Contains(contentType))
        {
            // Plain-text files: decode as UTF-8 (fallback to Latin-1 for legacy).
            try { return Encoding.UTF8.GetString(bytes); }
            catch { return Encoding.Latin1.GetString(bytes); }
        }

        // For binary formats (PDF, DOCX, PNG, JPEG) return a structured description so the
        // model can still produce metadata from the filename/category context, while flagging
        // that full OCR content is unavailable until an OCR service is wired.
        // This keeps the pipeline functional and produces low-confidence extractions rather
        // than hard failures, allowing downstream staff review.
        _logger.LogInformation(
            "DocumentParsingWorker: binary content type {ContentType}; returning file-type placeholder. " +
            "DocumentId={DocumentId}",
            contentType, documentId);

        return $"[Binary document of type '{contentType}'. OCR extraction is not yet available. " +
               $"File size: {bytes.Length} bytes. " +
               $"Please set extraction_possible=false and confidence=0.0 and request manual review.]";
    }
}
