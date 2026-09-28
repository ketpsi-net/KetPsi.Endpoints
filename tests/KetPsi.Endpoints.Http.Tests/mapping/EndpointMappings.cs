using HttpEndpointGenerator.Tests.Handlers;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

using KetPsi.Endpoints.Http;
namespace HttpEndpointGenerator.Tests.Mapping;

/// <summary>
/// All Map* calls live here. The source generator scans these invocations.
/// </summary>
public static class EndpointMappings
{
    public static void MapAllEndpoints(this WebApplication app)
    {
        // -------------------------------------------------
        // Simple endpoints (no group)
        // -------------------------------------------------
        app.MapGet<GetUserByIdHandler>("/users/{id}")
           .WithName("GetUserById");

        app.MapGet<SearchOrdersHandler>("/orders")
           .WithName("SearchOrders");

        app.MapPost<CreateUserHandler>("/users")
           .WithName("CreateUser");

        app.MapPut<UpdateUserHandler>("/users/{id}")
           .WithName("UpdateUser");

        app.MapDelete<DeleteUserHandler>("/users/{id}")
           .WithName("DeleteUser");

        app.MapDelete<BulkDeleteUsersHandler>("/users")
           .WithName("BulkDeleteUsers");

        app.MapPost<UploadDocumentHandler>("/documents")
           .DisableAntiforgery()
           .WithName("UploadDocument");

        app.MapGet<GetUserByIdV1Handler>("/v1/users/{id}")
           .WithName("GetUserByIdV1");

        app.MapGet<InternalDebugHandler>("/internal/debug")
           .SkipEndpointGeneration()
           .WithName("InternalDebug");

        // -------------------------------------------------
        // AsParameters endpoints (external config applied by generator)
        // -------------------------------------------------
        app.MapGet<GetOrdersWithRecordFilterHandler>("/orders/filtered")
           .WithName("GetOrdersFiltered");

        app.MapGet<SearchProductsHandler>("/products/search")
           .WithName("SearchProducts");

        app.MapGet<ListCustomersHandler>("/customers")
           .WithName("ListCustomers");

        app.MapGet<GetSalesReportHandler>("/reports/sales")
           .WithName("GetSalesReport");

        app.MapGet<SearchCustomersComplexHandler>("/customers/search")
           .WithName("SearchCustomersComplex");

        app.MapGet<GetInventoryHandler>("/inventory")
           .WithName("GetInventory");

        // -------------------------------------------------
        // Edge-case endpoints
        // -------------------------------------------------
        app.MapGet<PingHandler>("/ping")
           .WithName("Ping");

        app.MapGet<HealthHandler>("/health")
           .WithName("Health");

        app.MapGet<SearchByTermHandler>("/search/term")
           .WithName("SearchByTerm");

        app.MapGet<SearchInitOnlyHandler>("/search/initonly")
           .WithName("SearchInitOnly");

        app.MapGet<LookupByCodeHandler>("/lookup")
           .WithName("LookupByCode");

        app.MapGet<EmptyishHandler>("/emptyish")
           .WithName("Emptyish");

        app.MapGet<CollisionHandler>("/collision")
           .WithName("Collision");

        app.MapGet<GetBySlugHandler>("/slugs/{slug}")
           .WithName("GetBySlug");

        app.MapGet<MultiQueryHandler>("/multi-query")
           .WithName("MultiQuery");

        app.MapGet<HeaderEchoHandler>("/headers/echo")
           .WithName("HeaderEcho");

        app.MapPost<OptionalBodyHandler>("/optional-body")
           .WithName("OptionalBody");

        app.MapGet<ValueTaskHandler>("/valuetask/{id}")
           .WithName("ValueTask");

        // -------------------------------------------------
        // Grouped endpoints
        // -------------------------------------------------
        var api = app.MapGroup("/api/v1")
                     .WithTags("V1");
        
        api.MapGet<GetUserByIdHandler>("/users/{id}")
           .WithName("GetUserById_Grouped");

        api.MapPost<CreateUserHandler>("/users")
           .WithName("CreateUser_Grouped");

        // Nested group
        var admin = api.MapGroup("/admin")
                       .WithTags("Admin");

        admin.MapDelete<DeleteUserHandler>("/users/{id}")
             .WithName("AdminDeleteUser");

        // -------------------------------------------------
        // Versioned group (if your generator supports MapApiVersionedGroup)
        // -------------------------------------------------
        // var v2 = app.MapApiVersionedGroup("/api/v2") ...
    }
}
