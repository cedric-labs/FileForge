using System.Security.Cryptography;
using System.Text;

namespace FileForge.IO;

/// <summary>
/// Stores a secret or token locally in an encrypted file.
/// Uses DPAPI (<c>ProtectedData</c> with <c>CurrentUser</c> scope) on Windows, and AES-256 with a prefixed IV elsewhere.
/// </summary>
/// <param name="filePath">Full path of the encrypted file.</param>
/// <param name="entropy">Optional entropy binding the secret to the application or the device.</param>
public sealed class ProtectedFileStore(string filePath, byte[]? entropy = null)
{
    private static readonly byte[] s_defaultEntropy = Encoding.UTF8.GetBytes("FileForge-ProtectedFileStore-v1");
    private readonly string _filePath = !string.IsNullOrWhiteSpace(filePath)
        ? filePath
        : throw new ArgumentException("The protected file path cannot be empty.", nameof(filePath));
    private readonly byte[] _entropy = entropy is { Length: > 0 } ? [.. entropy] : s_defaultEntropy;

    /// <summary>
    /// Reads and decrypts the stored string, or returns <c>null</c> when the file is missing or unreadable.
    /// </summary>
    /// <returns>The decrypted text, or <c>null</c>.</returns>
    public string? ReadSecret()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return null;
            }

            var encryptedBytes = File.ReadAllBytes(_filePath);
            if (encryptedBytes.Length == 0)
            {
                return null;
            }

            var plainBytes = OperatingSystem.IsWindows()
                ? ProtectedData.Unprotect(encryptedBytes, _entropy, DataProtectionScope.CurrentUser)
                : DecryptAesWithEntropy(encryptedBytes, _entropy);

            return Encoding.UTF8.GetString(plainBytes);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            // A corrupted file, or one encrypted by another user or machine, is treated as no stored secret: the caller asks for it again.
            return null;
        }
    }

    /// <summary>
    /// Encrypts a secret string and writes it to disk.
    /// </summary>
    /// <param name="secret">Non-empty secret to store.</param>
    public void WriteSecret(string secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(secret);

        var plainBytes = Encoding.UTF8.GetBytes(secret);
        var encryptedBytes = OperatingSystem.IsWindows()
            ? ProtectedData.Protect(plainBytes, _entropy, DataProtectionScope.CurrentUser)
            : EncryptAesWithEntropy(plainBytes, _entropy);

        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllBytes(_filePath, encryptedBytes);
    }

    /// <summary>
    /// Deletes the protected file if it exists.
    /// </summary>
    public void ClearSecret()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Deletion is best effort: a file already gone or locked leaves the caller with nothing to do, and a locked file is read as before.
        }
    }

    /// <summary>
    /// Encrypts bytes with AES-256-CBC, using a key derived from the entropy by SHA-256 and a prefixed 16-byte IV.
    /// </summary>
    /// <param name="plainBytes">Plain data.</param>
    /// <param name="entropy">Entropy the AES-256 key is derived from.</param>
    /// <returns>The encrypted data, prefixed with the IV.</returns>
    public static byte[] EncryptAesWithEntropy(byte[] plainBytes, byte[] entropy)
    {
        ArgumentNullException.ThrowIfNull(plainBytes);
        ArgumentNullException.ThrowIfNull(entropy);

        using var aes = Aes.Create();
        aes.Key = SHA256.HashData(entropy);
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        var cipherText = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        var result = new byte[aes.IV.Length + cipherText.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(cipherText, 0, result, aes.IV.Length, cipherText.Length);
        return result;
    }

    /// <summary>
    /// Decrypts bytes encrypted by <see cref="EncryptAesWithEntropy"/>.
    /// </summary>
    /// <param name="cipherData">Encrypted data, prefixed with the 16-byte IV.</param>
    /// <param name="entropy">Entropy the AES-256 key is derived from.</param>
    /// <returns>The decrypted data.</returns>
    public static byte[] DecryptAesWithEntropy(byte[] cipherData, byte[] entropy)
    {
        ArgumentNullException.ThrowIfNull(cipherData);
        ArgumentNullException.ThrowIfNull(entropy);

        using var aes = Aes.Create();
        aes.Key = SHA256.HashData(entropy);

        var iv = new byte[16];
        if (cipherData.Length < iv.Length)
        {
            throw new CryptographicException("Invalid encrypted data.");
        }

        Buffer.BlockCopy(cipherData, 0, iv, 0, iv.Length);
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
        return decryptor.TransformFinalBlock(cipherData, iv.Length, cipherData.Length - iv.Length);
    }
}
