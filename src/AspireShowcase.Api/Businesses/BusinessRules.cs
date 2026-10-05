using System.Text.RegularExpressions;

/// <summary>What a business name and booking link have to look like.</summary>
static partial class BusinessRules
{
    const int MinNameLength = 2;
    const int MinSlugLength = 3;

    /// <summary>Why the name can't be used, or null when it can.</summary>
    public static string? NameProblem(string? name) =>
        name?.Trim() is not { Length: >= MinNameLength and <= AppDbContext.MaxNameLength }
            ? $"Use {MinNameLength} to {AppDbContext.MaxNameLength} characters."
            : null;

    /// <summary>Why the booking link can't be used, or null when it can. Doesn't check whether it's taken.</summary>
    public static string? SlugProblem(string? slug) => slug switch
    {
        null or { Length: < MinSlugLength or > AppDbContext.MaxSlugLength } =>
            $"Use {MinSlugLength} to {AppDbContext.MaxSlugLength} characters.",
        _ when !SlugPattern().IsMatch(slug) =>
            "Use lowercase letters, digits and single hyphens, starting and ending with a letter or digit.",
        _ => null,
    };

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex SlugPattern();
}
