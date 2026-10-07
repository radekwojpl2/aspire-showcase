using AspireShowcase.BuildingBlocks.Domain;

namespace AspireShowcase.BuildingBlocks.Application;

/// <summary>What came of a use case, other than its value.</summary>
public enum Outcome
{
    Done,

    /// <summary>What it was about doesn't exist, or isn't the caller's.</summary>
    NotFound,

    /// <summary>The request breaks a rule; <see cref="Result.Errors"/> says what's wrong per field.</summary>
    Invalid,

    /// <summary>It can't be done as things are, such as a time that was just taken.</summary>
    Conflict,

    /// <summary>Something it needs isn't available right now; trying again later may work.</summary>
    Unavailable,
}

/// <summary>
/// What a use case returns: done, or why not. The endpoints turn it into an HTTP answer; use
/// cases don't know about HTTP.
/// </summary>
public class Result
{
    static readonly Result DoneResult = new(Outcome.Done, null, null);

    protected Result(Outcome outcome, string? problem, IReadOnlyDictionary<string, string[]>? errors)
    {
        Outcome = outcome;
        Problem = problem;
        Errors = errors;
    }

    public Outcome Outcome { get; }

    /// <summary>For <see cref="Outcome.Conflict"/> and <see cref="Outcome.Unavailable"/>: what to tell the user.</summary>
    public string? Problem { get; }

    /// <summary>For <see cref="Outcome.Invalid"/>, or a <see cref="Outcome.Conflict"/> about fields: what's wrong per field.</summary>
    public IReadOnlyDictionary<string, string[]>? Errors { get; }

    public bool IsDone => Outcome == Outcome.Done;

    public static Result Done() => DoneResult;

    public static Result<T> Done<T>(T value) => new(value);

    public static Failure NotFound() => new(Outcome.NotFound, null, null);

    public static Failure Invalid(IReadOnlyDictionary<string, string[]> errors) => new(Outcome.Invalid, null, errors);

    public static Failure Invalid(string field, string message) => Invalid(new Dictionary<string, string[]> { [field] = [message] });

    /// <summary>A rule of the domain was broken.</summary>
    public static Failure Invalid(DomainValidationException exception) => Invalid(exception.Errors);

    public static Failure Conflict(string problem) => new(Outcome.Conflict, problem, null);

    /// <summary>A conflict about fields, such as a name that's taken.</summary>
    public static Failure Conflict(IReadOnlyDictionary<string, string[]> errors) => new(Outcome.Conflict, null, errors);

    public static Failure Unavailable(string problem) => new(Outcome.Unavailable, problem, null);
}

/// <summary>A result that isn't <see cref="Outcome.Done"/>; it converts to any <see cref="Result{T}"/>.</summary>
public sealed class Failure : Result
{
    internal Failure(Outcome outcome, string? problem, IReadOnlyDictionary<string, string[]>? errors)
        : base(outcome, problem, errors)
    {
    }
}

/// <summary>A result with a value when done.</summary>
public sealed class Result<T> : Result
{
    readonly T? _value;

    internal Result(T value)
        : base(Outcome.Done, null, null) => _value = value;

    Result(Failure failure)
        : base(failure.Outcome, failure.Problem, failure.Errors)
    {
    }

    /// <exception cref="InvalidOperationException">It isn't done.</exception>
    public T Value => IsDone ? _value! : throw new InvalidOperationException($"There's no value: the outcome is {Outcome}.");

    public static implicit operator Result<T>(Failure failure) => new(failure);

    public static implicit operator Result<T>(T value) => new(value);
}
