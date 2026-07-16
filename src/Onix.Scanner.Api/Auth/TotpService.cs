using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Onix.Scanner.Api.Auth;

public class TotpService
{
    private const int StepSeconds = 30;
    private const int Digits = 6;
    private const int MaxAttempts = 3;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<long, OtpChallenge> _challenges = new();

    public string GenerateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(20);
        return Base32Encode(bytes);
    }

    public string GenerateQrUri(string secret, string username) =>
        $"otpauth://totp/ONIX:{username}?secret={secret}&issuer=ONIX&digits={Digits}&period={StepSeconds}";

    public bool ValidateCode(string secret, string code)
    {
        if (code.Length != Digits || !code.All(char.IsDigit))
            return false;

        var expected = GenerateCode(secret, DateTime.UtcNow);
        return code == expected;
    }

    public string GenerateCode(string secret, DateTime timestamp)
    {
        var secretBytes = Base32Decode(secret);
        var counter = (long)(timestamp - DateTime.UnixEpoch).TotalSeconds / StepSeconds;
        var counterBytes = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(counterBytes);

        using var hmac = new HMACSHA1(secretBytes);
        var hash = hmac.ComputeHash(counterBytes);

        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24)
                   | (hash[offset + 1] << 16)
                   | (hash[offset + 2] << 8)
                   | hash[offset + 3];

        var code = binary % (int)Math.Pow(10, Digits);
        return code.ToString($"D{Digits}");
    }

    public (string backupCode, string hash) GenerateBackupCode()
    {
        var plain = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(plain)));
        return (plain, hash);
    }

    public List<(string plain, string hash)> GenerateBackupCodes(int count = 8)
    {
        var codes = new List<(string, string)>();
        for (int i = 0; i < count; i++)
            codes.Add(GenerateBackupCode());
        return codes;
    }

    public (bool valid, string? matchedHash) ValidateBackupCode(string code, string storedHashes)
    {
        var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(code)));
        var hashes = storedHashes.Split(',', StringSplitOptions.RemoveEmptyEntries);
        var match = hashes.FirstOrDefault(h => h == hash);
        return (match is not null, match);
    }

    public string RemoveUsedBackupCode(string storedHashes, string usedHash)
    {
        var hashes = storedHashes.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Where(h => h != usedHash);
        return string.Join(",", hashes);
    }

    // --- OTP challenge state ---

    public OtpChallengeResult StartChallenge(long chatId, Guid userId, string purpose)
    {
        if (_challenges.TryGetValue(chatId, out var existing) && existing.ExpiresAt > DateTime.UtcNow)
        {
            if (existing.AttemptCount >= MaxAttempts)
                return new OtpChallengeResult { Blocked = true, BlockedUntil = existing.ExpiresAt };
        }

        _challenges[chatId] = new OtpChallenge
        {
            UserId = userId,
            Purpose = purpose,
            AttemptCount = 0,
            ExpiresAt = DateTime.UtcNow.Add(LockoutDuration),
        };
        return new OtpChallengeResult { Success = true };
    }

    public OtpChallengeResult TryValidateOtp(long chatId, string code, string? secret = null, string? backupCodes = null)
    {
        if (!_challenges.TryGetValue(chatId, out var challenge))
            return new OtpChallengeResult { Expired = true };

        if (challenge.ExpiresAt <= DateTime.UtcNow)
        {
            _challenges.TryRemove(chatId, out _);
            return new OtpChallengeResult { Expired = true };
        }

        if (challenge.AttemptCount >= MaxAttempts)
            return new OtpChallengeResult { Blocked = true, BlockedUntil = challenge.ExpiresAt };

        challenge.AttemptCount++;

        var valid = false;
        var usedBackup = false;
        string? matchedHash = null;
        if (!string.IsNullOrEmpty(secret))
            valid = ValidateCode(secret, code);

        if (!valid && !string.IsNullOrEmpty(backupCodes))
        {
            (valid, matchedHash) = ValidateBackupCode(code, backupCodes);
            usedBackup = valid;
        }

        if (!valid)
        {
            var remaining = MaxAttempts - challenge.AttemptCount;
            return new OtpChallengeResult { Success = false, RemainingAttempts = remaining };
        }

        _challenges.TryRemove(chatId, out _);
        return new OtpChallengeResult
        {
            Success = true,
            Validated = true,
            UserId = challenge.UserId,
            Purpose = challenge.Purpose,
            UsedBackup = usedBackup,
            MatchedHash = matchedHash,
        };
    }

    public void ClearChallenge(long chatId) => _challenges.TryRemove(chatId, out _);

    private static string Base32Encode(byte[] data)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        int bitCount = 0, index = 0;
        var result = new System.Text.StringBuilder();
        for (int i = 0; i < data.Length; i++)
        {
            index = (index << 8) | data[i];
            bitCount += 8;
            while (bitCount >= 5)
            {
                result.Append(alphabet[(index >> (bitCount - 5)) & 0x1f]);
                bitCount -= 5;
            }
        }
        if (bitCount > 0)
            result.Append(alphabet[(index << (5 - bitCount)) & 0x1f]);
        return result.ToString();
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var cleaned = input.ToUpperInvariant().Replace(" ", "").Replace("-", "");
        var bytes = new List<byte>();
        int buffer = 0, bitsLeft = 0;
        foreach (var c in cleaned)
        {
            var val = alphabet.IndexOf(c);
            if (val < 0) continue;
            buffer = (buffer << 5) | val;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                bytes.Add((byte)(buffer >> (bitsLeft - 8)));
                bitsLeft -= 8;
            }
        }
        return bytes.ToArray();
    }
}

public class OtpChallenge
{
    public Guid UserId { get; set; }
    public string Purpose { get; set; } = "";
    public int AttemptCount { get; set; }
    public DateTime ExpiresAt { get; set; }
}

public class OtpChallengeResult
{
    public bool Success { get; set; }
    public bool Expired { get; set; }
    public bool Blocked { get; set; }
    public DateTime? BlockedUntil { get; set; }
    public bool Validated { get; set; }
    public Guid UserId { get; set; }
    public string Purpose { get; set; } = "";
    public int RemainingAttempts { get; set; }
    public bool UsedBackup { get; set; }
    public string? MatchedHash { get; set; }
}
