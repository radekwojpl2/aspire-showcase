namespace AspireShowcase.Api.BusinessSetup;

/// <summary>
/// The time zone a business's hours are in, as an IANA ID such as Europe/Warsaw. Opening hours are
/// local wall-clock times; this is what turns them into instants, including across DST changes.
/// </summary>
static class BusinessTimeZone
{
    public const int MaxLength = 64;

    /// <summary>For businesses started before they had a time zone.</summary>
    public const string Default = "UTC";

    /// <summary>
    /// Whether the ID is an IANA time zone this machine knows. Windows IDs are refused even on
    /// Windows, so stored values work the same in the Linux containers in Azure.
    /// </summary>
    public static bool IsValid(string? id) =>
        id is { Length: > 0 and <= MaxLength } &&
        TimeZoneInfo.TryFindSystemTimeZoneById(id, out _) &&
        TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out _);
}
