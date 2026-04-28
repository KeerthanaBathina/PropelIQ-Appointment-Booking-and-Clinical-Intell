namespace UPACIP.Service.Backup.Models;

/// <summary>
/// Configuration for backup file encryption (US_089, AC-1, DR-025).
///
/// Bound from the <c>"BackupEncryption"</c> section in <c>appsettings.json</c>.
/// Hot-reloaded via <see cref="Microsoft.Extensions.Options.IOptionsMonitor{T}"/>.
///
/// Security note: <see cref="EncryptionKeyBase64"/> is intentionally left empty in the
/// committed <c>appsettings.json</c>.  In production, provide the key via the environment
/// variable <c>BackupEncryption__EncryptionKeyBase64</c> so the key is never stored alongside
/// backup files (edge case 2 — key separation, OWASP A02).
///
/// <b>IMPORTANT</b>: Do NOT override <c>ToString()</c> or any equality member on this class —
/// the key value must never appear in structured logs.
/// </summary>
public sealed class EncryptionOptions
{
    /// <summary>Configuration section key in appsettings.json.</summary>
    public const string SectionName = "BackupEncryption";

    /// <summary>
    /// Base64-encoded 256-bit (32-byte) AES-256-CBC encryption key.
    ///
    /// Required when <see cref="Enabled"/> is <c>true</c>.
    /// Must decode to exactly 32 bytes; the service throws
    /// <see cref="System.InvalidOperationException"/> at backup-cycle start if the
    /// decoded key is not the correct length.
    ///
    /// Generate a suitable key:
    ///   PowerShell: [Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Max 256 } | ForEach-Object { [byte]$_ }))
    ///   .NET:       Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
    ///
    /// Left empty in committed configuration — injected at runtime via environment variable.
    /// </summary>
    public string EncryptionKeyBase64 { get; set; } = string.Empty;

    /// <summary>
    /// When <c>false</c>, the encryption step is skipped and the plaintext <c>.dump</c>
    /// file is retained as-is. Intended for local development only.
    /// Default: <c>true</c>.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// I/O buffer size for streaming encryption and decryption.
    /// Larger buffers improve throughput at the cost of memory.
    /// Default: 81 920 bytes (80 KB) — balances throughput and memory for typical backup sizes.
    /// </summary>
    public int BufferSizeBytes { get; set; } = 81920;
}
