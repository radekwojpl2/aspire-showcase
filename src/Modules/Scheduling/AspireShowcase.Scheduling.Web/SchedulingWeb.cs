using AspireShowcase.Scheduling.Web;

namespace AspireShowcase.Scheduling;

/// <summary>Scheduling's endpoints, for the API host to map; AddScheduling registers what they use.</summary>
public static class SchedulingWeb
{
    /// <param name="includeDevelopmentTools">Also maps /dev/sample-bookings; only ever in Development.</param>
    public static void MapScheduling(this IEndpointRouteBuilder api, bool includeDevelopmentTools)
    {
        CalendarEndpoints.Map(api);
        OwnerBookingEndpoints.Map(api);
        TimeOffEndpoints.Map(api);
        PublicBookingEndpoints.Map(api);
        ClientBookingEndpoints.Map(api);
        if (includeDevelopmentTools)
        {
            SampleBookings.Map(api);
        }
    }
}
