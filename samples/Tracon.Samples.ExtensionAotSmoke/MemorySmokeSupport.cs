namespace Tracon.Samples.ExtensionAotSmoke;

/// <summary>A run attribution context that always names one user.</summary>
internal sealed class FixedUser(string userId) : IRunAttributionContext
{
    public string? UserId => userId;

    public IReadOnlyDictionary<string, string>? Labels => null;
}
