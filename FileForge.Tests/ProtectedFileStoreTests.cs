using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using FileForge.IO;
using Xunit;

namespace FileForge.Tests;

public sealed class ProtectedFileStoreTests : IDisposable
{
    private readonly string _tempFilePath;

    public ProtectedFileStoreTests()
    {
        _tempFilePath = Path.Combine(Path.GetTempPath(), $"fileforge_secret_{Guid.NewGuid():N}.dat");
    }

    public void Dispose()
    {
        if (File.Exists(_tempFilePath))
        {
            try
            {
                File.Delete(_tempFilePath);
            }
            catch (IOException)
            {
                // A file briefly locked by the file system (indexer, antivirus) is left in the temp folder rather than failing the test.
            }
        }
    }

    [Fact]
    public void WriteSecret_WhenValidSecretProvided_RoundTripsAndClearsCorrectly()
    {
        var store = new ProtectedFileStore(_tempFilePath);
        store.ReadSecret().Should().BeNull();

        store.WriteSecret("TopSecretToken#2026");
        store.ReadSecret().Should().Be("TopSecretToken#2026");

        store.ClearSecret();
        store.ReadSecret().Should().BeNull();
    }

    [Fact]
    public void EncryptAesWithEntropy_WhenRoundTrippedWithDecryptAesWithEntropy_RestoresPlainBytes()
    {
        var entropy = Encoding.UTF8.GetBytes("TestEntropy");
        var plain = Encoding.UTF8.GetBytes("SensitivePayload123!");

        var cipher = ProtectedFileStore.EncryptAesWithEntropy(plain, entropy);
        var decrypted = ProtectedFileStore.DecryptAesWithEntropy(cipher, entropy);

        Encoding.UTF8.GetString(decrypted).Should().Be("SensitivePayload123!");
    }

    [Fact]
    public void DecryptAesWithEntropy_WhenCipherShorterThanIv_ThrowsCryptographicException()
    {
        var entropy = Encoding.UTF8.GetBytes("TestEntropy");
        Action act = () => ProtectedFileStore.DecryptAesWithEntropy([1, 2, 3], entropy);

        act.Should().Throw<CryptographicException>();
    }
}
