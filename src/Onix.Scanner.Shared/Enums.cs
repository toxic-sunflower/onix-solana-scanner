using System.Text.Json.Serialization;

namespace Onix.Scanner.Shared;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TokenHealthStatus
{
    Active,
    StaleBingx,
    StaleJupiter,
    ProxyError,
    NoQuote,
    MappingRequired,
    Disabled
}

public enum ProxyStatus
{
    Active,
    Failed,
    Slow,
    Disabled
}

/// <summary>TZ п.8.3: "По умолчанию worker не должен незаметно переходить на
/// общий IP, если токен настроен на обязательную индивидуальную прокси."
/// Strict is the default for exactly that reason.</summary>
public enum ProxyFallbackPolicy
{
    Strict,
    FallbackToSharedIp
}

public enum QualityStatus
{
    Valid,
    Stale,
    Invalid
}

public enum UserRole
{
    User,
    Admin
}

public enum SubscriptionTier
{
    Free,
    Premium
}
