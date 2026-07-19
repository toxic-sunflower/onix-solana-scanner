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
