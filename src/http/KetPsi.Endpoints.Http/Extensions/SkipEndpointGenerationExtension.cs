using Microsoft.AspNetCore.Builder;

namespace KetPsi.Endpoints.Http;

public static class SkipEndpointGenerationExtension
{
    public static RouteHandlerBuilder SkipEndpointGeneration(this RouteHandlerBuilder endpointRouteBuilder)
    {
        return endpointRouteBuilder;
    }
}
