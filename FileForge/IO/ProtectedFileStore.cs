using System.Security.Cryptography;
using System.Text;

namespace FileForge.IO;

/// <summary>
/// Magasin de persistance locale de secrets et jetons sur fichier chiffré.
/// Utilise DPAPI (<c>ProtectedData</c> en portée <c>CurrentUser</c>) sous Windows et AES-256 avec IV préfixé en repli.
/// </summary>
/// <param name="filePath">Chemin complet du fichier chiffré.</param>
/// <param name="entropy">Entropie optionnelle de liaison à l'application ou à l'appareil.</param>
public sealed class ProtectedFileStore(string filePath, byte[]? entropy = null)
{
    private static readonly byte[] s_defaultEntropy = Encoding.UTF8.GetBytes("FileForge-ProtectedFileStore-v1");
    private readonly string _filePath = !string.IsNullOrWhiteSpace(filePath)
        ? filePath
        : throw new ArgumentException("Le chemin du fichier protégé ne peut pas être vide.", nameof(filePath));
    private readonly byte[] _entropy = entropy is { Length: > 0 } ? [.. entropy] : s_defaultEntropy;

    /// <summary>
    /// Lit et déchiffre la chaîne stockée dans le fichier, ou retourne <c>null</c> si le fichier est absent ou illisible.
    /// </summary>
    /// <returns>Texte déchiffré ou <c>null</c>.</returns>
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
            // En cas de corruption ou d'incompatibilité machine, retourner null
            return null;
        }
    }

    /// <summary>
    /// Chiffre et écrit une chaîne secrète sur le disque.
    /// </summary>
    /// <param name="secret">Valeur secrète non vide à persister.</param>
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
    /// Supprime le fichier protégé s'il existe.
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
            // Tolérance idempotente si le fichier est déjà supprimé ou verrouillé
        }
    }

    /// <summary>
    /// Chiffre un tableau d'octets via AES-256-CBC avec clé dérivée par SHA-256 de l'entropie et IV de 16 octets préfixé.
    /// </summary>
    /// <param name="plainBytes">Données en clair.</param>
    /// <param name="entropy">Entropie servant à dériver la clé AES-256.</param>
    /// <returns>Données chiffrées préfixées par l'IV.</returns>
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
    /// Déchiffre un tableau d'octets chiffré par <see cref="EncryptAesWithEntropy"/>.
    /// </summary>
    /// <param name="cipherData">Données chiffrées préfixées par l'IV de 16 octets.</param>
    /// <param name="entropy">Entropie servant à dériver la clé AES-256.</param>
    /// <returns>Données déchiffrées.</returns>
    public static byte[] DecryptAesWithEntropy(byte[] cipherData, byte[] entropy)
    {
        ArgumentNullException.ThrowIfNull(cipherData);
        ArgumentNullException.ThrowIfNull(entropy);

        using var aes = Aes.Create();
        aes.Key = SHA256.HashData(entropy);

        var iv = new byte[16];
        if (cipherData.Length < iv.Length)
        {
            throw new CryptographicException("Données chiffrées invalides.");
        }

        Buffer.BlockCopy(cipherData, 0, iv, 0, iv.Length);
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
        return decryptor.TransformFinalBlock(cipherData, iv.Length, cipherData.Length - iv.Length);
    }
}
