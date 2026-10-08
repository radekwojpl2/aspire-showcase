using AspireShowcase.Scheduling.Application.Development;

namespace AspireShowcase.Scheduling.Web;

/// <summary>
/// Development only: /dev/sample-bookings fills the next 7 days of every business with made-up
/// bookings. Called from a command on the "web" resource in the Aspire dashboard.
/// </summary>
static class SampleBookings
{
    public static void Map(IEndpointRouteBuilder api) =>
        api.MapPost("/dev/sample-bookings", (MakeSampleBookings handler, CancellationToken cancellation) =>
            handler.HandleAsync(cancellation));
}
