using System.Text.RegularExpressions;

namespace AspireShowcase.Api.BusinessSetup;

/// <summary>The business's part of its booking link, /book/{slug}.</summary>
static partial class BookingSlug
{
    public const int MinLength = 3;
    public const int MaxLength = 40;

    /// <summary>Why the slug can't be used, or null when it can. Doesn't check whether it's taken.</summary>
    public static string? Problem(string? slug) => slug switch
    {
        null or { Length: < MinLength or > MaxLength } =>
            $"Use {MinLength} to {MaxLength} characters.",
        _ when !Pattern().IsMatch(slug) =>
            "Use lowercase letters, digits and single hyphens, starting and ending with a letter or digit.",
        _ => null,
    };

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex Pattern();
}
