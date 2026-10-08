using AspireShowcase.BusinessSetup.Web;

namespace AspireShowcase.BusinessSetup;

/// <summary>Business Setup's endpoints, for the API host to map; AddBusinessSetup registers what they use.</summary>
public static class BusinessSetupWeb
{
    /// <summary>Maps the module's endpoints, under /businesses.</summary>
    public static void MapBusinessSetup(this IEndpointRouteBuilder api) => BusinessSetupEndpoints.MapEndpoints(api);
}
