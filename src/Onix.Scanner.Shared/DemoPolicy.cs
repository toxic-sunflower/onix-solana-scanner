namespace Onix.Scanner.Shared;

/// <summary>Shared constants for the demo-access quota (5 hours of "online"
/// time — time with a live SSE connection open — per user, before payment
/// is required). Payment integration itself is not implemented yet;
/// User.HasPaidAccess is the placeholder switch for it.</summary>
public static class DemoPolicy
{
    public const int QuotaSeconds = 5 * 60 * 60;
}
