using AspireShowcase.BuildingBlocks.Application;
using Microsoft.AspNetCore.Http;

namespace AspireShowcase.BuildingBlocks.Web;

/// <summary>Turns what a use case came to into the HTTP answer the API gives for it.</summary>
public static class ResultHttp
{
    /// <param name="done">The answer when it's done; 204 No Content when left out.</param>
    public static IResult ToHttp(this Result result, Func<IResult>? done = null) => result.Outcome switch
    {
        Outcome.Done => done?.Invoke() ?? Results.NoContent(),
        Outcome.NotFound => Results.NotFound(),
        Outcome.Invalid => Results.ValidationProblem(Copy(result.Errors)),
        Outcome.Conflict when result.Errors is { } errors =>
            Results.ValidationProblem(Copy(errors), statusCode: StatusCodes.Status409Conflict),
        Outcome.Conflict => Results.Problem(title: result.Problem, statusCode: StatusCodes.Status409Conflict),
        Outcome.Unavailable => Results.Problem(title: result.Problem, statusCode: StatusCodes.Status503ServiceUnavailable),
        _ => throw new InvalidOperationException($"No HTTP answer for {result.Outcome}."),
    };

    /// <param name="done">The answer with the value when it's done, such as Results.Ok or Results.Created.</param>
    public static IResult ToHttp<T>(this Result<T> result, Func<T, IResult> done) =>
        result.IsDone ? done(result.Value) : ((Result)result).ToHttp();

    static Dictionary<string, string[]> Copy(IReadOnlyDictionary<string, string[]>? errors) =>
        errors?.ToDictionary() ?? [];
}
