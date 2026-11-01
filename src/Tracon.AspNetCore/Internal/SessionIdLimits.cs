namespace Tracon;

/// <summary>The size limits of a session identifier a client chooses.</summary>
internal static class SessionIdLimits
{
    /// <summary>
    /// The longest session identifier accepted. The <c>sessions</c> table of SQL Server holds
    /// <c>nvarchar(200)</c> and refuses a longer value with a truncation error, while the other stores
    /// keep it, so the limit is the narrowest column; the AG-UI thread identity uses the same number.
    /// </summary>
    public const int MaxLength = 200;
}
