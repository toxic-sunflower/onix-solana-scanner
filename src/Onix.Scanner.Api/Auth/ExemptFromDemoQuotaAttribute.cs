namespace Onix.Scanner.Api.Auth;

/// <summary>Marks a controller as exempt from DemoQuotaFilter — e.g.
/// AuthController, so a user whose demo quota ran out can still check their
/// status, refresh their token, or log out.</summary>
[AttributeUsage(AttributeTargets.Class)]
public class ExemptFromDemoQuotaAttribute : Attribute
{
}
