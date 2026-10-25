using System.ComponentModel;
using System.Runtime.CompilerServices;

using Grpc.Core;

namespace KetPsi.Endpoints.Grpc;

/// <summary>Stream adapters used by generated streaming handlers.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class GrpcStreamAdapters
{
    /// <summary>Exposes a gRPC request stream as <see cref="IAsyncEnumerable{T}"/>.</summary>
    public static async IAsyncEnumerable<T> ReadAll<T>(IAsyncStreamReader<T> reader, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        while (await reader.MoveNext(cancellationToken).ConfigureAwait(false))
            yield return reader.Current;
    }
}
