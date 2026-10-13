using System.ServiceModel;

namespace KetPsi.Endpoints.Grpc.Tests;

/// <summary>
/// Client-side contracts for integration tests (no CallContext).
/// Service/operation names must match the generated server contracts.
/// </summary>
[ServiceContract(Name = "Api.Users")]
public interface IUsersClient
{
    [OperationContract(Name = "GetById")]
    Task<GetUserResponse> GetByIdAsync(GetUserRequest request);
}

[ServiceContract(Name = "Api.Orders")]
public interface IOrdersClient
{
    [OperationContract(Name = "Create")]
    Task<CreateOrderResponse> CreateAsync(CreateOrderRequest request);
}
