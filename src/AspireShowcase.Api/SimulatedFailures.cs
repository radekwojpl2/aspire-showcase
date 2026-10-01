using System.Diagnostics;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Failures that can be switched on while the app runs, to see how they look in traces, logs
/// and metrics. They affect the to-do endpoints only. The switches are the commands on the
/// "web" resource in the Aspire dashboard, which call the endpoints below; those are mapped
/// in Development only, so in Azure nothing can turn a failure on.
/// </summary>
sealed class SimulatedFailures
{
    volatile bool _errors;
    volatile bool _slowQueries;

    public bool Errors { get => _errors; set => _errors = value; }

    public bool SlowQueries { get => _slowQueries; set => _slowQueries = value; }

    /// <summary>Runs before a to-do endpoint and applies whichever failures are on.</summary>
    public async Task ApplyAsync(HttpContext context)
    {
        if (SlowQueries)
        {
            // A query that really takes two seconds in PostgreSQL, so the time shows up
            // in the database span rather than in the API.
            var db = context.RequestServices.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlRawAsync("SELECT pg_sleep(2)", context.RequestAborted);
        }

        if (Errors)
        {
            var exception = new InvalidOperationException("Simulated failure: the to-do endpoints are set to fail.");
            // Attaches the exception to the request's span; the exception handler then
            // logs it and answers 500 with problem details.
            Activity.Current?.AddException(exception);
            throw exception;
        }
    }
}

static class SimulatedFailureEndpoints
{
    public static void MapSimulatedFailures(this IEndpointRouteBuilder api)
    {
        var failures = api.MapGroup("/failures");

        failures.MapGet("/", (SimulatedFailures state) => new FailureState(state.Errors, state.SlowQueries))
            .WithName("GetSimulatedFailures");

        failures.MapPost("/errors", (SimulatedFailures state) =>
        {
            state.Errors = true;
            return new FailureState(state.Errors, state.SlowQueries);
        })
        .WithName("FailTodoRequests");

        failures.MapPost("/slow-queries", (SimulatedFailures state) =>
        {
            state.SlowQueries = true;
            return new FailureState(state.Errors, state.SlowQueries);
        })
        .WithName("SlowDownTodoQueries");

        failures.MapPost("/reset", (SimulatedFailures state) =>
        {
            state.Errors = false;
            state.SlowQueries = false;
            return new FailureState(state.Errors, state.SlowQueries);
        })
        .WithName("StopSimulatedFailures");
    }

    record FailureState(bool Errors, bool SlowQueries);
}
