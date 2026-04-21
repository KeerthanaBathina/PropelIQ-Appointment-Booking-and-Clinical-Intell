using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BCrypt.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OtpNet;
using UPACIP.DataAccess;
using UPACIP.DataAccess.Entities;

namespace UPACIP.Service.Auth;

/// <summary>
/// TOTP-based MFA service implementing <see cref="IMfaService"/> (US_016 AC-1).
///
/// Security design:
///   - TOTP secrets are AES-256-CBC encrypted. The IV is randomly generated per encryption
///     and prepended to the ciphertext before Base64 encoding so each stored value is unique.
///   - Encryption key is derived via PBKDF2 (SHA-256, 100k iterations) from
///     <c>Mfa:TotpEncryptionKey</c> in configuration (never hardcoded).
///   - Backup codes: 8 × 8-char random alphanumeric codes, each BCrypt-hashed (work factor 10).
///     The JSON array of hashed codes is stored in <c>ApplicationUser.MfaRecoveryCodes</c>.
///     Each code entry is either the BCrypt hash (unused) or <c>null</c> (consumed).
///   - Code verification uses OtpNet with VerificationWindow(1, 1) for ±30-second tolerance.
/// </summary>
public sealed class MfaService : IMfaService
{
    private const string Issuer = "UPACIP";
    private const int BackupCodeCount = 8;
    private const int BackupCodeLength = 8;
    private const string BackupCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // unambiguous chars

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _db;
    private readonly byte[] _aesKey;
    private readonly ILogger<MfaService> _logger;

    public MfaService(
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext db,
        IConfiguration configuration,
        ILogger<MfaService> logger)
    {
        _userManager = userManager;
        _db          = db;
        _logger      = logger;

        var keyMaterial = configuration["Mfa:TotpEncryptionKey"]
            ?? throw new InvalidOperationException(
                "Mfa:TotpEncryptionKey is required. Set it via user secrets: " +
                "dotnet user-secrets set \"Mfa:TotpEncryptionKey\" \"<32+ char secret>\"");

        _aesKey = DeriveKey(keyMaterial);
    }

    /// <inheritdoc/>
    public async Task<MfaSetupData> GenerateSecretAsync(Guid userId, string userEmail, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString())
            ?? throw new InvalidOperationException($"User {userId} not found.");

        // Generate a 20-byte (160-bit) random TOTP secret (RFC 4226 minimum).
        var secretBytes = RandomNumberGenerator.GetBytes(20);
        var base32Secret = Base32Encoding.ToString(secretBytes);

        // Encrypt and persist (MFA not yet enabled — pending verify-setup step).
        user.TotpSecretEncrypted = Encrypt(secretBytes);
        var updateResult = await _userManager.UpdateAsync(user);

        if (!updateResult.Succeeded)
        {
            _logger.LogError("Failed to persist TOTP secret for user {UserId}: {Errors}",
                userId, string.Join(", ", updateResult.Errors.Select(e => e.Description)));
            throw new InvalidOperationException("Failed to persist MFA setup data.");
        }

        var otpAuthUrl = $"otpauth://totp/{Uri.EscapeDataString(Issuer)}:{Uri.EscapeDataString(userEmail)}" +
                         $"?secret={base32Secret}&issuer={Uri.EscapeDataString(Issuer)}&algorithm=SHA1&digits=6&period=30";

        return new MfaSetupData(otpAuthUrl, base32Secret);
    }

    /// <inheritdoc/>
    public async Task<bool> VerifyTotpCodeAsync(Guid userId, string code, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null || !user.MfaEnabled || user.TotpSecretEncrypted is null)
            return false;

        return VerifyCode(user.TotpSecretEncrypted, code);
    }

    /// <inheritdoc/>
    public async Task<string[]?> EnableMfaAsync(Guid userId, string code, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null || user.TotpSecretEncrypted is null)
            return null;

        if (!VerifyCode(user.TotpSecretEncrypted, code))
            return null;

        // Generate 8 single-use backup codes.
        var plainCodes = GeneratePlainBackupCodes();
        var hashedCodes = plainCodes.Select(c => BCrypt.Net.BCrypt.HashPassword(c, workFactor: 10)).ToArray();

        user.MfaEnabled       = true;
        user.MfaRecoveryCodes = JsonSerializer.Serialize(hashedCodes);

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            _logger.LogError("Failed to enable MFA for user {UserId}: {Errors}",
                userId, string.Join(", ", result.Errors.Select(e => e.Description)));
            return null;
        }

        _logger.LogInformation("MFA enabled for user {UserId}.", userId);
        return plainCodes;
    }

    /// <inheritdoc/>
    public async Task<bool> VerifyBackupCodeAsync(Guid userId, string code, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null || !user.MfaEnabled || user.MfaRecoveryCodes is null)
            return false;

        var hashedCodes = JsonSerializer.Deserialize<string?[]>(user.MfaRecoveryCodes);
        if (hashedCodes is null)
            return false;

        var normalised = code.Trim().ToUpperInvariant();

        for (int i = 0; i < hashedCodes.Length; i++)
        {
            if (hashedCodes[i] is null)
                continue; // already consumed

            if (BCrypt.Net.BCrypt.Verify(normalised, hashedCodes[i]))
            {
                // Consume the code (one-time use).
                hashedCodes[i] = null;
                user.MfaRecoveryCodes = JsonSerializer.Serialize(hashedCodes);
                await _userManager.UpdateAsync(user);
                _logger.LogInformation("Backup code consumed for user {UserId}.", userId);
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public async Task DisableMfaAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user is null)
            return;

        user.TotpSecretEncrypted = null;
        user.MfaRecoveryCodes    = null;
        user.MfaEnabled          = false;

        await _userManager.UpdateAsync(user);
        _logger.LogInformation("MFA disabled for user {UserId}.", userId);
    }

    /// <inheritdoc/>
    public async Task<bool> IsEnabledAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        return user?.MfaEnabled ?? false;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private bool VerifyCode(string encryptedSecret, string code)
    {
        try
        {
            var secretBytes = Decrypt(encryptedSecret);
            var totp        = new Totp(secretBytes);
            // ±1 step tolerance = ±30 seconds (RFC 6238 §5.2 recommendation).
            return totp.VerifyTotp(code, out _, new VerificationWindow(previous: 1, future: 1));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TOTP verification threw unexpectedly.");
            return false;
        }
    }

    private string Encrypt(byte[] plaintext)
    {
        using var aes = Aes.Create();
        aes.Key = _aesKey;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var cipher = encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);

        // Store as "{base64(IV)}:{base64(ciphertext)}"
        return $"{Convert.ToBase64String(aes.IV)}:{Convert.ToBase64String(cipher)}";
    }

    private byte[] Decrypt(string encoded)
    {
        var parts  = encoded.Split(':', 2);
        var iv     = Convert.FromBase64String(parts[0]);
        var cipher = Convert.FromBase64String(parts[1]);

        using var aes = Aes.Create();
        aes.Key = _aesKey;
        aes.IV  = iv;

        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(cipher, 0, cipher.Length);
    }

    private static byte[] DeriveKey(string keyMaterial)
    {
        // PBKDF2 with SHA-256: deterministic per key material, constant salt (app-specific).
        using var pbkdf2 = new Rfc2898DeriveBytes(
            Encoding.UTF8.GetBytes(keyMaterial),
            salt: Encoding.UTF8.GetBytes("UPACIP_MFA_TOTP_SALT_v1"),
            iterations: 100_000,
            HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(32); // 256-bit AES key
    }

    private static string[] GeneratePlainBackupCodes()
    {
        var codes = new string[BackupCodeCount];
        for (int i = 0; i < BackupCodeCount; i++)
        {
            var bytes = RandomNumberGenerator.GetBytes(BackupCodeLength);
            var sb = new StringBuilder(BackupCodeLength);
            foreach (var b in bytes)
                sb.Append(BackupCodeAlphabet[b % BackupCodeAlphabet.Length]);
            codes[i] = sb.ToString();
        }
        return codes;
    }
}
