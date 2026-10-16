using System.Linq.Expressions;

namespace KetPsi.Endpoints.Grpc;

/// <summary>
/// Endpoint-level gRPC configuration for a specific handler (interceptors + parameter binding).
/// Mirrors HTTP's <c>IHttpEndpointConfiguration&lt;TEndpoint&gt;</c>.
/// </summary>
public interface IGrpcEndpointConfiguration<in TEndpoint>
{
    void Configure(IGrpcEndpointBuilder<TEndpoint> builder);
}

/// <summary>
/// Non-generic marker / interceptor-only configuration (optional).
/// </summary>
public interface IGrpcEndpointConfiguration
{
    void Configure(IGrpcEndpointBuilder builder);
}

public interface IGrpcEndpointBuilder
{
    IGrpcEndpointBuilder AddInterceptor<TInterceptor>() where TInterceptor : global::Grpc.Core.Interceptors.Interceptor;
    IGrpcEndpointBuilder AddInterceptor(Type interceptorType);
}

public interface IGrpcEndpointBuilder<out TEndpoint> : IGrpcEndpointBuilder
{
    /// <summary>
    /// Configures binding sources for the request DTO (headers, path) — not the protobuf wire map.
    /// </summary>
    IGrpcEndpointBuilder<TEndpoint> Request<TRequest>(Action<IGrpcRequestBinder<TRequest>> configure);
}

/// <summary>
/// Binds individual request properties from gRPC request headers (metadata).
/// </summary>
public interface IGrpcRequestBinder<TRequest>
{
    IGrpcRequestBinder<TRequest> Property<TProp>(
        Expression<Func<TRequest, TProp>> propertySelector,
        Action<IGrpcParameterBuilder<TProp>> configure);
}

/// <summary>
/// Parameter binding sources — parallel to HTTP <c>FromHeader</c>.
/// </summary>
public interface IGrpcParameterBuilder<TParam>
{
    /// <summary>
    /// Bind from an HTTP/2 / gRPC request header (metadata entry).
    /// When <paramref name="name"/> is null, the property name is used as the header key.
    /// </summary>
    IGrpcParameterBuilder<TParam> FromHeader(string? name = null);
}

public class GrpcEndpointBuilder : IGrpcEndpointBuilder
{
    public List<Type> InterceptorTypes { get; } = new();
    public List<GrpcBindingRegistration> Bindings { get; } = new();

    public IGrpcEndpointBuilder AddInterceptor<TInterceptor>()
        where TInterceptor : global::Grpc.Core.Interceptors.Interceptor
    {
        InterceptorTypes.Add(typeof(TInterceptor));
        return this;
    }

    public IGrpcEndpointBuilder AddInterceptor(Type interceptorType)
    {
        if (interceptorType is null)
            throw new ArgumentNullException(nameof(interceptorType));
        if (!typeof(global::Grpc.Core.Interceptors.Interceptor).IsAssignableFrom(interceptorType))
            throw new ArgumentException(
                $"Type {interceptorType} must inherit Interceptor.", nameof(interceptorType));
        InterceptorTypes.Add(interceptorType);
        return this;
    }
}

public sealed class GrpcEndpointBuilder<TEndpoint> : GrpcEndpointBuilder, IGrpcEndpointBuilder<TEndpoint>
{
    public IGrpcEndpointBuilder<TEndpoint> Request<TRequest>(Action<IGrpcRequestBinder<TRequest>> configure)
    {
        var binder = new GrpcRequestBinder<TRequest>(Bindings);
        configure(binder);
        return this;
    }
}

public sealed class GrpcRequestBinder<TRequest> : IGrpcRequestBinder<TRequest>
{
    private readonly List<GrpcBindingRegistration> _bindings;

    public GrpcRequestBinder(List<GrpcBindingRegistration> bindings) => _bindings = bindings;

    public IGrpcRequestBinder<TRequest> Property<TProp>(
        Expression<Func<TRequest, TProp>> propertySelector,
        Action<IGrpcParameterBuilder<TProp>> configure)
    {
        var name = ReadMemberName(propertySelector);
        var param = new GrpcParameterBuilder<TProp>(name);
        configure(param);
        if (param.Source != GrpcBindingSource.None)
        {
            _bindings.Add(new GrpcBindingRegistration(
                typeof(TRequest),
                name,
                param.Source,
                param.Key,
                typeof(TProp)));
        }
        return this;
    }

    private static string ReadMemberName<TProp>(Expression<Func<TRequest, TProp>> expr)
    {
        return expr.Body switch
        {
            MemberExpression m => m.Member.Name,
            UnaryExpression { Operand: MemberExpression m } => m.Member.Name,
            _ => throw new ArgumentException("Expression must be a property access.", nameof(expr))
        };
    }
}

public sealed class GrpcParameterBuilder<TParam> : IGrpcParameterBuilder<TParam>
{
    public string PropertyName { get; }
    public GrpcBindingSource Source { get; private set; } = GrpcBindingSource.None;
    public string? Key { get; private set; }

    public GrpcParameterBuilder(string propertyName) => PropertyName = propertyName;

    public IGrpcParameterBuilder<TParam> FromHeader(string? name = null)
    {
        Source = GrpcBindingSource.Header;
        Key = name ?? PropertyName;
        return this;
    }
}

public enum GrpcBindingSource
{
    None = 0,
    Header = 1
}

public sealed record GrpcBindingRegistration(
    Type RequestType,
    string PropertyName,
    GrpcBindingSource Source,
    string? Key,
    Type PropertyType);
