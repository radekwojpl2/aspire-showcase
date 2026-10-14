namespace AspireShowcase.Mcp.Tools;

/// <summary>A change the user was asked to confirm: done, with its result, or declined.</summary>
sealed record ChangeResult<T>(bool Done, string? Message, T? Result)
{
    public static ChangeResult<T> Declined(string message) => new(false, message, default);

    public static ChangeResult<T> Made(T result) => new(true, null, result);
}
