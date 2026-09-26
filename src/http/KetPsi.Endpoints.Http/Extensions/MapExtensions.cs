using System.Diagnostics.CodeAnalysis;

using KetPsi.Endpoints.Abstractions;
using KetPsi.Endpoints.Http.Internals;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace KetPsi.Endpoints.Http;

public static class MapExtensions
{
    /// <summary>
    /// Maps a GET endpoint to a specified route using a service handler.
    /// </summary>
    /// <typeparam name="TEndpoint">This type parameter is used to specify the class that handles the service logic for the endpoint.</typeparam>
    /// <param name="endpointRouteBuilder">This parameter represents the application context where the endpoint is being defined.</param>
    /// <param name="route">This parameter specifies the URL pattern for the GET endpoint being mapped.</param>
    /// <returns>The method returns an object that allows further configuration of the mapped endpoint.</returns>
    public static RouteHandlerBuilder MapGet<TEndpoint>(this IEndpointRouteBuilder endpointRouteBuilder, [StringSyntax("Route")] string route)
       where TEndpoint : class
    {
        return new RouteHandlerBuilder([new EmptyEndpointConventionBuilder(endpointRouteBuilder.ServiceProvider)]);
    }


    /// <summary>
    /// Maps a POST endpoint to a specified route using a service handler.
    /// </summary>
    /// <typeparam name="TEndpoint">This type parameter is used to specify the class that handles the service logic for the endpoint.</typeparam>
    /// <param name="endpointRouteBuilder">This parameter represents the application where the endpoint will be mapped.</param>
    /// <param name="route">This parameter defines the specific route for the POST endpoint.</param>
    /// <returns>The method returns an endpoint convention builder for further configuration.</returns>
    public static RouteHandlerBuilder MapPost<TEndpoint>(this IEndpointRouteBuilder endpointRouteBuilder, [StringSyntax("Route")] string route)
       where TEndpoint : class, IEndpoint
    {
        return new RouteHandlerBuilder([new EmptyEndpointConventionBuilder(endpointRouteBuilder.ServiceProvider)]);
    }

    /// <summary>
    /// Maps a PUT request to a specified route in a web application using a service handler.
    /// </summary>
    /// <typeparam name="TEndpoint">This type parameter is used to specify the class that handles the service logic for the endpoint.</typeparam>
    /// <param name="endpointRouteBuilder">This parameter represents the application where the endpoint will be mapped.</param>
    /// <param name="route">This parameter defines the specific route for the PUT request.</param>
    /// <returns>Returns an endpoint convention builder for further configuration of the mapped endpoint.</returns>
    public static RouteHandlerBuilder MapPut<TEndpoint>(this IEndpointRouteBuilder endpointRouteBuilder, [StringSyntax("Route")] string route)
       where TEndpoint : class, IEndpoint
    {
        return new RouteHandlerBuilder([new EmptyEndpointConventionBuilder(endpointRouteBuilder.ServiceProvider)]);
    }

    /// <summary>
    /// Maps a DELETE HTTP request to a specified route using a service handler.
    /// </summary>
    /// <typeparam name="TEndpoint">This type parameter specifies the class that handles the service logic for the DELETE request.</typeparam>
    /// <param name="endpointRouteBuilder">This parameter is used to define the routing configuration for the endpoint.</param>
    /// <param name="route">This parameter specifies the URL pattern for the DELETE request.</param>
    /// <returns>The method returns an endpoint convention builder for further configuration.</returns>
    public static RouteHandlerBuilder MapDelete<TEndpoint>(this IEndpointRouteBuilder endpointRouteBuilder, [StringSyntax("Route")] string route)
        where TEndpoint : class, IEndpoint
    {
        return new RouteHandlerBuilder([new EmptyEndpointConventionBuilder(endpointRouteBuilder.ServiceProvider)]);
    }

}
