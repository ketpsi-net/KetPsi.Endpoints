using System.Linq.Expressions;

namespace KetPsi.Endpoints.Grpc;

/// <summary>
/// Endpoint-level gRPC configuration for a specific handler (interceptors + parameter binding).
/// Mirrors HTTP's endpoint configuration pattern.
/// </summary>
/// <typeparam name="TEndpoint">The endpoint handler type.</typeparam>
public interface IGrpcEndpointConfiguration<in TEndpoint>
{
    /// <summary>Configures interceptors and request binding for <typeparamref name="TEndpoint"/>.</summary>
    void Configure(IGrpcEndpointBuilder<TEndpoint> builder);
}

/// <summary>Non-generic endpoint configuration (interceptors only).</summary>
public interface IGrpcEndpointConfiguration
{
    /// <summary>Configures interceptors for the endpoint.</summary>
    void Configure(IGrpcEndpointBuilder builder);
}

/// <summary>Fluent builder for endpoint-level gRPC options (interceptors).</summary>
public interface IGrpcEndpointBuilder
{
    /// <summary>Registers a gRPC interceptor type resolved from request services per call.</summary>
    IGrpcEndpointBuilder AddInterceptor<TInterceptor>() where TInterceptor : global::Grpc.Core.Interceptors.Interceptor;

    /// <summary>Registers a gRPC interceptor type resolved from request services per call.</summary>
    IGrpcEndpointBuilder AddInterceptor(Type interceptorType);
}

/// <summary>Fluent builder for a specific endpoint handler type.</summary>
/// <typeparam name="TEndpoint">The endpoint handler type.</typeparam>
public interface IGrpcEndpointBuilder<out TEndpoint> : IGrpcEndpointBuilder
{
    /// <summary>
    /// Configures binding sources for the request DTO (headers) — not the protobuf wire map.
    /// </summary>
    IGrpcEndpointBuilder<TEndpoint> Request<TRequest>(Action<IGrpcRequestBinder<TRequest>> configure);
}

/// <summary>Binds individual request properties from gRPC metadata headers.</summary>
/// <typeparam name="TRequest">The request DTO type.</typeparam>
public interface IGrpcRequestBinder<TRequest>
{
    /// <summary>Configures how a property is bound from headers.</summary>
    IGrpcRequestBinder<TRequest> Property<TProp>(
        Expression<Func<TRequest, TProp>> propertySelector,
        Action<IGrpcParameterBuilder<TProp>> configure);
}

/// <summary>Parameter binding sources (parallel to HTTP <c>FromMetadata</c>).</summary>
/// <typeparam name="TParam">The property type being bound.</typeparam>
public interface IGrpcParameterBuilder<TParam>
{
    /// <summary>
    /// Bind from an HTTP/2 / gRPC request header (metadata entry).
    /// When <paramref name="name"/> is null, the property name is used as the header key.
    /// </summary>
    IGrpcParameterBuilder<TParam> FromMetadata(string? name = null);
}

/// <summary>Runtime builder implementation used by <c>MapGrpc</c> configure callbacks and the generator.</summary>
internal class GrpcEndpointBuilder : IGrpcEndpointBuilder
{
    /// <summary>Interceptor types discovered at configure time.</summary>
    public List<Type> InterceptorTypes { get; } = new();

    /// <summary>Header binding registrations discovered at configure time.</summary>
    public List<GrpcBindingRegistration> Bindings { get; } = new();

    /// <inheritdoc />
    public IGrpcEndpointBuilder AddInterceptor<TInterceptor>()
        where TInterceptor : global::Grpc.Core.Interceptors.Interceptor
    {
        InterceptorTypes.Add(typeof(TInterceptor));
        return this;
    }

    /// <inheritdoc />
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

/// <summary>Typed endpoint builder used by <c>MapGrpc&lt;T&gt;</c> configure lambdas.</summary>
internal sealed class GrpcEndpointBuilder<TEndpoint> : GrpcEndpointBuilder, IGrpcEndpointBuilder<TEndpoint>
{
    /// <inheritdoc />
    public IGrpcEndpointBuilder<TEndpoint> Request<TRequest>(Action<IGrpcRequestBinder<TRequest>> configure)
    {
        var binder = new GrpcRequestBinder<TRequest>(Bindings);
        configure(binder);
        return this;
    }
}

/// <summary>Collects <see cref="FromMetadata"/> bindings for a request DTO.</summary>
internal sealed class GrpcRequestBinder<TRequest> : IGrpcRequestBinder<TRequest>
{
    private readonly List<GrpcBindingRegistration> _bindings;

    public GrpcRequestBinder(List<GrpcBindingRegistration> bindings) => _bindings = bindings;

    /// <inheritdoc />
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

/// <summary>Fluent parameter source builder for a single property.</summary>
internal sealed class GrpcParameterBuilder<TParam> : IGrpcParameterBuilder<TParam>
{
    public string PropertyName { get; }
    public GrpcBindingSource Source { get; private set; } = GrpcBindingSource.None;
    public string? Key { get; private set; }

    public GrpcParameterBuilder(string propertyName) => PropertyName = propertyName;

    /// <inheritdoc />
    public IGrpcParameterBuilder<TParam> FromMetadata(string? name = null)
    {
        Source = GrpcBindingSource.Metadata;
        Key = name ?? PropertyName;
        return this;
    }
}

/// <summary>Where a request property is bound from at call time.</summary>
public enum GrpcBindingSource
{
    /// <summary>Not bound from metadata.</summary>
    None = 0,
    /// <summary>Bound from gRPC request metadata (HTTP/2 headers).</summary>
    Metadata = 1,
}

/// <summary>One property binding discovered from endpoint configuration.</summary>
internal sealed class GrpcBindingRegistration
{
    public Type RequestType { get; }
    public string PropertyName { get; }
    public GrpcBindingSource Source { get; }
    public string? Key { get; }
    public Type PropertyType { get; }

    public GrpcBindingRegistration(
        Type requestType,
        string propertyName,
        GrpcBindingSource source,
        string? key,
        Type propertyType)
    {
        RequestType = requestType;
        PropertyName = propertyName;
        Source = source;
        Key = key;
        PropertyType = propertyType;
    }
}
