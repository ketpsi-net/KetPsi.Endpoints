using KetPsi.Endpoints.Abstractions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace HttpEndpointGenerator.Tests.Handlers;

// -------------------------------------------------------
// 1. Simple GET with path parameter
// -------------------------------------------------------
public class GetUserByIdHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync(int id, CancellationToken ct)
    {
        return Task.FromResult(Results.Ok(new { Id = id, Name = $"User-{id}" }));
    }
}

// -------------------------------------------------------
// 2. GET with multiple query parameters + header
// -------------------------------------------------------
public class SearchOrdersHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync(
        [FromQuery] string? status,
        [FromQuery] int? page = 1,
        [FromQuery] int? size = 20,
        [FromHeader(Name = "X-Tenant-ID")] string? tenantId = null,
        CancellationToken ct = default)
    {
        return Task.FromResult(Results.Ok(new
        {
            Status = status,
            Page = page,
            Size = size,
            TenantId = tenantId,
            Total = 42
        }));
    }
}

// -------------------------------------------------------
// 3. POST with JSON body
// -------------------------------------------------------
public record CreateUserRequest(string Email, string FirstName, string LastName, string[]? Roles = null);

public class CreateUserHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync([FromBody] CreateUserRequest request, CancellationToken ct)
    {
        return Task.FromResult(Results.Created($"/users/{Guid.NewGuid():N}", request));
    }
}

// -------------------------------------------------------
// 4. PUT with path + body + conditional header
// -------------------------------------------------------
public record UpdateUserRequest(string FirstName, string LastName, string? Email = null);

public class UpdateUserHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync(
        int id,
        [FromBody] UpdateUserRequest request,
        [FromHeader(Name = "If-Match")] string? etag,
        CancellationToken ct)
    {
        return Task.FromResult(Results.Ok(new
        {
            Id = id,
            request.FirstName,
            request.LastName,
            request.Email,
            ETag = etag
        }));
    }
}

// -------------------------------------------------------
// 5. DELETE simple
// -------------------------------------------------------
public class DeleteUserHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync(int id, CancellationToken ct)
    {
        return Task.FromResult(Results.NoContent());
    }
}

// -------------------------------------------------------
// 6. DELETE with body (bulk)
// -------------------------------------------------------
public record BulkDeleteRequest(int[] Ids, string? Reason = null);

public class BulkDeleteUsersHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync([FromBody] BulkDeleteRequest request, CancellationToken ct)
    {
        return Task.FromResult(Results.Ok(new { Deleted = request.Ids.Length, request.Reason }));
    }
}

// -------------------------------------------------------
// 7. FromServices + FromKeyedServices
// -------------------------------------------------------
public interface IDocumentService
{
    string GetName();
}

public class DocumentService : IDocumentService
{
    public string GetName() => "DocumentService";
}

public interface IBlobStorage
{
    string Provider { get; }
}

public class PrimaryBlobStorage : IBlobStorage
{
    public string Provider => "Primary";
}

public class SecondaryBlobStorage : IBlobStorage
{
    public string Provider => "Secondary";
}

public class UploadDocumentHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync(
        [FromForm] IFormFile? file,
        [FromForm] string title,
        [FromServices] IDocumentService docs,
        [FromKeyedServices("primary")] IBlobStorage storage,
        CancellationToken ct)
    {
        return Task.FromResult(Results.Ok(new
        {
            Title = title,
            FileName = file?.FileName,
            Service = docs.GetName(),
            Storage = storage.Provider
        }));
    }
}

// -------------------------------------------------------
// 8. Obsolete endpoint
// -------------------------------------------------------
[Obsolete("Use GetUserByIdV2 instead")]
public class GetUserByIdV1Handler : IEndpoint
{
    public Task<IResult> ExecuteAsync(int id, CancellationToken ct)
    {
        return Task.FromResult(Results.Ok(new { Id = id, Version = "v1" }));
    }
}

// -------------------------------------------------------
// 9. Handler that should be skipped
// -------------------------------------------------------
public class InternalDebugHandler : IEndpoint
{
    public Task<IResult> ExecuteAsync(CancellationToken ct)
    {
        return Task.FromResult(Results.Ok(new { Debug = true }));
    }
}
