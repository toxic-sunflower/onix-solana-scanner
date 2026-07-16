using Onix.Scanner.Infrastructure.Services;

namespace Onix.Scanner.Tests;

public class EncryptionServiceTests
{
    private static readonly byte[] Key = "0123456789abcdef"u8.ToArray();

    [Fact]
    public void Encrypt_Decrypt_Roundtrip()
    {
        var svc = new AesEncryptionService(Key);
        var original = "MySecretPassword123!";
        var encrypted = svc.Encrypt(original);
        var decrypted = svc.Decrypt(encrypted);
        Assert.Equal(original, decrypted);
    }

    [Fact]
    public void Encrypt_Decrypt_EmptyString()
    {
        var svc = new AesEncryptionService(Key);
        Assert.Equal("", svc.Encrypt(""));
        Assert.Equal("", svc.Decrypt(""));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("hello world")]
    [InlineData("  spaces  ")]
    [InlineData("unicode ✓")]
    [InlineData("very long password that exceeds typical lengths xyz1234567890!@#$%^&*()")]
    public void Encrypt_Decrypt_VariousInputs(string input)
    {
        var svc = new AesEncryptionService(Key);
        var encrypted = svc.Encrypt(input);
        var decrypted = svc.Decrypt(encrypted);
        Assert.Equal(input, decrypted);
    }

    [Fact]
    public void SamePlaintext_ProducesDifferentCiphertext_EachTime()
    {
        var svc = new AesEncryptionService(Key);
        var input = "same password";
        var e1 = svc.Encrypt(input);
        var e2 = svc.Encrypt(input);
        Assert.NotEqual(e1, e2);
    }

    [Fact]
    public void DifferentKeys_ProduceDifferentCiphertext()
    {
        var key1 = "0123456789abcdef"u8.ToArray();
        var key2 = "fedcba9876543210"u8.ToArray();
        var svc1 = new AesEncryptionService(key1);
        var svc2 = new AesEncryptionService(key2);
        var input = "test password";
        var e1 = svc1.Encrypt(input);
        var e2 = svc2.Encrypt(input);
        Assert.NotEqual(e1, e2);
    }

    [Fact]
    public void Constructor_Throws_OnInvalidKeyLength()
    {
        var invalidKey = new byte[] { 1, 2, 3 };
        Assert.Throws<ArgumentException>(() => new AesEncryptionService(invalidKey));
    }

    [Fact]
    public void Decrypt_InvalidBase64_Throws()
    {
        var svc = new AesEncryptionService(Key);
        Assert.Throws<FormatException>(() => svc.Decrypt("not-base64!!!"));
    }
}
