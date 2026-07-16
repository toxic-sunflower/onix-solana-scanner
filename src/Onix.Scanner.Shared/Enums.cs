namespace Onix.Scanner.Shared;

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
