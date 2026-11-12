using System.Buffers;
using System.Collections;
using System.IO.Pipelines;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;

namespace KetPsi.Endpoints.Http.Benchmarks.Harness;

/// <summary>
/// A reusable, allocation-free stand-in for the per-connection server object Kestrel hands to ASP.NET Core
/// (Kestrel's <c>HttpProtocol</c> also implements the request/response/body/lifetime/routing features itself and
/// resets them per request). Everything that Kestrel does <b>not</b> implement (query, items, request services, …)
/// is created lazily by <see cref="DefaultHttpContext"/> exactly as in a real server and lands in the "extra" slots,
/// which are cleared per request, so those allocations are attributed to the endpoint just like in production.
/// </summary>
public sealed class InMemoryServerFeatures :
    IFeatureCollection,
    IHttpRequestFeature,
    IHttpResponseFeature,
    IHttpResponseBodyFeature,
    IRequestBodyPipeFeature,
    IHttpRequestBodyDetectionFeature,
    IHttpRequestLifetimeFeature,
    IHttpRequestIdentifierFeature,
    IRouteValuesFeature,
    IEndpointFeature
{
    private static readonly Type[] KnownTypes =
    [
        typeof(IHttpRequestFeature),
        typeof(IHttpResponseFeature),
        typeof(IHttpResponseBodyFeature),
        typeof(IRequestBodyPipeFeature),
        typeof(IHttpRequestBodyDetectionFeature),
        typeof(IHttpRequestLifetimeFeature),
        typeof(IHttpRequestIdentifierFeature),
        typeof(IRouteValuesFeature),
        typeof(IEndpointFeature),
    ];

    private readonly object?[] _known = new object?[KnownTypes.Length];
    private KeyValuePair<Type, object>[] _extra = new KeyValuePair<Type, object>[16];
    private int _extraCount;

    private readonly List<KeyValuePair<Func<object, Task>, object>> _onStarting = new(4);
    private readonly List<KeyValuePair<Func<object, Task>, object>> _onCompleted = new(4);

    private readonly ResponseBodyWriter _responseWriter;
    private readonly ResponseBodyStream _responseStream;
    private readonly RequestBodyReader _requestReader = new();
    private readonly MemoryStream _emptyRequestBody = new(Array.Empty<byte>(), writable: false);
    private MemoryStream _requestBody;
    private byte[] _requestBodyBytes = Array.Empty<byte>();

    public InMemoryServerFeatures()
    {
        _responseWriter = new ResponseBodyWriter(this);
        _responseStream = new ResponseBodyStream(_responseWriter);
        _requestBody = _emptyRequestBody;
        ResetKnown();
    }

    // ───────────────────────── request template (set once per scenario) ─────────────────────────

    /// <summary>Configures the request once; <see cref="ResetForRequest"/> then only rewinds the body.</summary>
    public void SetRequest(string method, string path, string query, IHeaderDictionary headers, byte[]? body)
    {
        Method = method;
        Path = path;
        QueryString = query;
        RequestHeaders = headers;
        _requestBodyBytes = body ?? Array.Empty<byte>();
        _requestBody = body is null ? _emptyRequestBody : new MemoryStream(body, writable: false);
        _requestReader.SetData(_requestBodyBytes);
    }

    /// <summary>
    /// Cheap per-request reset (what Kestrel does between requests on a kept-alive connection). Allocation free.
    /// </summary>
    public void ResetForRequest(Endpoint? endpoint, RouteValueDictionary? routeValues)
    {
        ResetKnown();
        Array.Clear(_extra, 0, _extraCount);
        _extraCount = 0;
        Revision++;

        _requestBody.Position = 0;
        _requestReader.Reset();

        StatusCode = 200;
        ReasonPhrase = null;
        ResponseHeaders.Clear();
        HasStarted = false;
        _responseWriter.Reset();
        _onStarting.Clear();
        _onCompleted.Clear();

        Endpoint = endpoint;
        _routeValues = routeValues;
    }

    private void ResetKnown()
    {
        for (var i = 0; i < _known.Length; i++)
            _known[i] = this;
    }

    // ───────────────────────── response inspection (verification only) ─────────────────────────

    public ReadOnlySpan<byte> ResponseBody => _responseWriter.Written;

    public string? ResponseContentType => ResponseHeaders.ContentType;

    // ───────────────────────── lifecycle helpers used by the harness ─────────────────────────

    internal void EnsureStarted()
    {
        if (HasStarted)
            return;

        HasStarted = true;
        // Kestrel runs OnStarting callbacks in reverse registration order.
        for (var i = _onStarting.Count - 1; i >= 0; i--)
        {
            var (callback, state) = _onStarting[i];
            var task = callback(state);
            if (!task.IsCompletedSuccessfully)
                task.GetAwaiter().GetResult();
        }
    }

    /// <summary>Runs OnCompleted callbacks (e.g. disposal of the request service scope). Returns a pending task only if one is async.</summary>
    internal Task FireOnCompletedAsync()
    {
        for (var i = _onCompleted.Count - 1; i >= 0; i--)
        {
            var (callback, state) = _onCompleted[i];
            var task = callback(state);
            if (!task.IsCompletedSuccessfully)
                return AwaitRest(task, i);
        }

        return Task.CompletedTask;
    }

    private async Task AwaitRest(Task pending, int index)
    {
        await pending.ConfigureAwait(false);
        for (var i = index - 1; i >= 0; i--)
        {
            var (callback, state) = _onCompleted[i];
            await callback(state).ConfigureAwait(false);
        }
    }

    // ───────────────────────── IFeatureCollection ─────────────────────────

    public bool IsReadOnly => false;

    public int Revision { get; private set; }

    public object? this[Type key]
    {
        get
        {
            var known = KnownTypes;
            for (var i = 0; i < known.Length; i++)
            {
                if (ReferenceEquals(known[i], key))
                    return _known[i];
            }

            for (var i = 0; i < _extraCount; i++)
            {
                if (ReferenceEquals(_extra[i].Key, key))
                    return _extra[i].Value;
            }

            return null;
        }
        set
        {
            Revision++;
            var known = KnownTypes;
            for (var i = 0; i < known.Length; i++)
            {
                if (ReferenceEquals(known[i], key))
                {
                    _known[i] = value;
                    return;
                }
            }

            for (var i = 0; i < _extraCount; i++)
            {
                if (ReferenceEquals(_extra[i].Key, key))
                {
                    if (value is null)
                    {
                        _extra[i] = _extra[_extraCount - 1];
                        _extra[--_extraCount] = default;
                    }
                    else
                    {
                        _extra[i] = new(key, value);
                    }
                    return;
                }
            }

            if (value is null)
                return;

            if (_extraCount == _extra.Length)
                Array.Resize(ref _extra, _extra.Length * 2);
            _extra[_extraCount++] = new(key, value);
        }
    }

    public TFeature? Get<TFeature>() => (TFeature?)this[typeof(TFeature)];

    public void Set<TFeature>(TFeature? instance) => this[typeof(TFeature)] = instance;

    public IEnumerator<KeyValuePair<Type, object>> GetEnumerator()
    {
        for (var i = 0; i < KnownTypes.Length; i++)
        {
            if (_known[i] is { } value)
                yield return new(KnownTypes[i], value);
        }

        for (var i = 0; i < _extraCount; i++)
            yield return _extra[i];
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    // ───────────────────────── IHttpRequestFeature ─────────────────────────

    public string Protocol { get; set; } = "HTTP/1.1";
    public string Scheme { get; set; } = "http";
    public string Method { get; set; } = "GET";
    public string PathBase { get; set; } = string.Empty;
    public string Path { get; set; } = "/";
    public string QueryString { get; set; } = string.Empty;
    public string RawTarget { get; set; } = string.Empty;
    public IHeaderDictionary RequestHeaders { get; private set; } = new HeaderDictionary();

    IHeaderDictionary IHttpRequestFeature.Headers
    {
        get => RequestHeaders;
        set => RequestHeaders = value;
    }

    Stream IHttpRequestFeature.Body
    {
        get => _requestBody;
        set => throw new NotSupportedException("Replacing the request body is not supported by the benchmark harness.");
    }

    // ───────────────────────── IHttpResponseFeature ─────────────────────────

    public int StatusCode { get; set; } = 200;
    public string? ReasonPhrase { get; set; }
    public IHeaderDictionary ResponseHeaders { get; private set; } = new HeaderDictionary();
    public bool HasStarted { get; private set; }

    IHeaderDictionary IHttpResponseFeature.Headers
    {
        get => ResponseHeaders;
        set => ResponseHeaders = value;
    }

    Stream IHttpResponseFeature.Body
    {
        get => _responseStream;
        set => throw new NotSupportedException("Replacing the response body is not supported by the benchmark harness.");
    }

    public void OnStarting(Func<object, Task> callback, object state) => _onStarting.Add(new(callback, state));

    public void OnCompleted(Func<object, Task> callback, object state) => _onCompleted.Add(new(callback, state));

    // ───────────────────────── IHttpResponseBodyFeature ─────────────────────────

    Stream IHttpResponseBodyFeature.Stream => _responseStream;

    PipeWriter IHttpResponseBodyFeature.Writer => _responseWriter;

    void IHttpResponseBodyFeature.DisableBuffering()
    {
    }

    Task IHttpResponseBodyFeature.StartAsync(CancellationToken cancellationToken)
    {
        EnsureStarted();
        return Task.CompletedTask;
    }

    Task IHttpResponseBodyFeature.SendFileAsync(string path, long offset, long? count, CancellationToken cancellationToken)
        => throw new NotSupportedException();

    Task IHttpResponseBodyFeature.CompleteAsync()
    {
        EnsureStarted();
        return Task.CompletedTask;
    }

    // ───────────────────────── Request body / lifetime / identity ─────────────────────────

    PipeReader IRequestBodyPipeFeature.Reader => _requestReader;

    bool IHttpRequestBodyDetectionFeature.CanHaveBody => _requestBodyBytes.Length > 0;

    CancellationToken IHttpRequestLifetimeFeature.RequestAborted { get; set; }

    void IHttpRequestLifetimeFeature.Abort()
    {
    }

    string IHttpRequestIdentifierFeature.TraceIdentifier { get; set; } = "0HBENCHMARK:00000001";

    // ───────────────────────── Routing ─────────────────────────

    private RouteValueDictionary? _routeValues;

    RouteValueDictionary IRouteValuesFeature.RouteValues
    {
        // Kestrel creates the dictionary lazily as well.
        get => _routeValues ??= new RouteValueDictionary();
        set => _routeValues = value;
    }

    public Endpoint? Endpoint { get; set; }

    // ───────────────────────── Body implementations ─────────────────────────

    /// <summary>Growable, reusable response buffer exposed as a <see cref="PipeWriter"/>. No allocation after warm-up.</summary>
    private sealed class ResponseBodyWriter(InMemoryServerFeatures owner) : PipeWriter
    {
        private byte[] _buffer = new byte[16 * 1024];
        private int _written;

        public ReadOnlySpan<byte> Written => _buffer.AsSpan(0, _written);

        public void Reset() => _written = 0;

        public override void Advance(int bytes)
        {
            owner.EnsureStarted();
            _written += bytes;
        }

        public override Memory<byte> GetMemory(int sizeHint = 0)
        {
            Ensure(sizeHint);
            return _buffer.AsMemory(_written);
        }

        public override Span<byte> GetSpan(int sizeHint = 0)
        {
            Ensure(sizeHint);
            return _buffer.AsSpan(_written);
        }

        public void Write(ReadOnlySpan<byte> source)
        {
            owner.EnsureStarted();
            Ensure(source.Length);
            source.CopyTo(_buffer.AsSpan(_written));
            _written += source.Length;
        }

        private void Ensure(int sizeHint)
        {
            if (sizeHint <= 0)
                sizeHint = 256;
            if (_buffer.Length - _written < sizeHint)
                Array.Resize(ref _buffer, Math.Max(_buffer.Length * 2, _written + sizeHint));
        }

        public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default)
        {
            owner.EnsureStarted();
            return new ValueTask<FlushResult>(new FlushResult(isCanceled: false, isCompleted: false));
        }

        public override ValueTask<FlushResult> WriteAsync(ReadOnlyMemory<byte> source, CancellationToken cancellationToken = default)
        {
            Write(source.Span);
            return new ValueTask<FlushResult>(new FlushResult(isCanceled: false, isCompleted: false));
        }

        public override bool CanGetUnflushedBytes => true;

        public override long UnflushedBytes => 0;

        public override void CancelPendingFlush()
        {
        }

        public override void Complete(Exception? exception = null)
        {
        }

        public override ValueTask CompleteAsync(Exception? exception = null) => default;
    }

    private sealed class ResponseBodyStream(ResponseBodyWriter writer) : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => writer.Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer) => writer.Write(buffer);

        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            writer.Write(buffer.AsSpan(offset, count));
            return Task.CompletedTask;
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            writer.Write(buffer.Span);
            return default;
        }
    }

    /// <summary>Single-segment <see cref="PipeReader"/> over the fixed request body. Rewound per request; no allocation.</summary>
    private sealed class RequestBodyReader : PipeReader
    {
        private byte[] _data = Array.Empty<byte>();
        private int _consumed;

        public void SetData(byte[] data)
        {
            _data = data;
            _consumed = 0;
        }

        public void Reset() => _consumed = 0;

        private ReadResult Current()
            => new(new ReadOnlySequence<byte>(_data, _consumed, _data.Length - _consumed), isCanceled: false, isCompleted: true);

        public override bool TryRead(out ReadResult result)
        {
            result = Current();
            return true;
        }

        public override ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
            => new(Current());

        public override void AdvanceTo(SequencePosition consumed) => _consumed = consumed.GetInteger();

        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) => _consumed = consumed.GetInteger();

        public override void CancelPendingRead()
        {
        }

        public override void Complete(Exception? exception = null)
        {
        }

        public override ValueTask CompleteAsync(Exception? exception = null) => default;
    }
}
