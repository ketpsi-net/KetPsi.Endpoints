namespace KetPsi.Endpoints.Abstractions
{
    /// <summary>
    /// Marker for an endpoint handler: a plain class with a public <c>ExecuteAsync(..., CancellationToken)</c> method.
    /// The KetPsi source generators turn <c>MapGet&lt;THandler&gt;</c> / <c>MapGrpc&lt;THandler&gt;</c> call sites for
    /// implementing types into protocol endpoints at compile time.
    /// </summary>
    public interface IEndpoint;
}
