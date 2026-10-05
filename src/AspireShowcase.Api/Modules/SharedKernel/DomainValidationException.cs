namespace AspireShowcase.Api.SharedKernel;

/// <summary>
/// A change was refused because it breaks a rule of the domain. <see cref="Errors"/> says what's
/// wrong per field, in words meant for the user; the API passes them on as a validation problem.
/// </summary>
sealed class DomainValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("The change breaks a rule of the domain.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

/// <summary>Collects everything wrong with a change, so the user hears about all of it at once.</summary>
sealed class DomainErrors
{
    readonly Dictionary<string, List<string>> _errors = [];

    public void Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var messages))
        {
            _errors[field] = messages = [];
        }
        messages.Add(message);
    }

    public void ThrowIfAny()
    {
        if (_errors.Count > 0)
        {
            throw new DomainValidationException(_errors.ToDictionary(error => error.Key, error => error.Value.ToArray()));
        }
    }
}
