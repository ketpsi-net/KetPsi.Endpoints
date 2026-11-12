using System.Text.Json;
using System.Text.Json.Serialization;

namespace KetPsi.Endpoints.Results;

/// <summary>
/// Source-generated (reflection-free, Native AOT / trimming safe) JSON metadata for the payload-less result shapes
/// shipped by this package: <see cref="ApiResult{T}"/> of <see cref="object"/> (returned by
/// <see cref="ApiResult.Ok()"/>, <see cref="ApiResult.NoContent()"/>, the error factories and
/// <see cref="ApiResult.From(KetPsi.Results.Result)"/>), <see cref="ApiResultError"/> and its list.
/// </summary>
/// <remarks>
/// <para>
/// Add it to your resolver chain next to your own context, e.g.
/// <c>options.SerializerOptions.TypeInfoResolverChain.Add(ApiResultJsonContext.Default);</c>.
/// Typed results (<c>ApiResult&lt;OrderDto&gt;</c>) must be declared in your own context with
/// <c>[JsonSerializable(typeof(ApiResult&lt;OrderDto&gt;))]</c>.
/// </para>
/// <para>
/// Payload-less results always have a null <see cref="ApiResult{T}.Value"/>, so serializing them never requires
/// polymorphic <see cref="object"/> metadata. If you put a non-null value into an <c>ApiResult&lt;object?&gt;</c>
/// under Native AOT, its runtime type must also be registered in a resolver in the chain.
/// </para>
/// <para>Property names use the web defaults (camelCase), matching ASP.NET Core's default JSON options.</para>
/// </remarks>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(ApiResult<object>))]
[JsonSerializable(typeof(ApiResultError))]
[JsonSerializable(typeof(IReadOnlyList<ApiResultError>))]
public sealed partial class ApiResultJsonContext : JsonSerializerContext;
