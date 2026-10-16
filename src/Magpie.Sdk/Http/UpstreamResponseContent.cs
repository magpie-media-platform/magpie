using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Magpie.Sdk.Http;

internal sealed class UpstreamResponseContent : HttpContent
{
    private readonly HttpContent _content;
    private readonly CancellationTokenSource _timeout;
    private readonly CancellationTokenSource _linked;
    private readonly TimeProvider _timeProvider;

    public UpstreamResponseContent(
        HttpContent content,
        CancellationTokenSource timeout,
        CancellationTokenSource linked,
        TimeProvider timeProvider)
    {
        _content = content;
        _timeout = timeout;
        _linked = linked;
        _timeProvider = timeProvider;
        foreach (var header in content.Headers)
        {
            Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    protected override bool TryComputeLength(out long length)
    {
        length = _content.Headers.ContentLength ?? 0;
        return _content.Headers.ContentLength.HasValue;
    }

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        using var source = await CreateContentReadStreamAsync().ConfigureAwait(false);
        await source.CopyToAsync(stream, 81920, _linked.Token).ConfigureAwait(false);
    }

#if !NETSTANDARD2_1
    protected override async Task SerializeToStreamAsync(
        Stream stream,
        TransportContext? context,
        CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_linked.Token, cancellationToken);
        using var source = await ReadStreamAsync(linked.Token).ConfigureAwait(false);
        await source.CopyToAsync(stream, linked.Token).ConfigureAwait(false);
    }

    protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken) =>
        ReadStreamAsync(cancellationToken);
#endif

    protected override Task<Stream> CreateContentReadStreamAsync() => ReadStreamAsync(_linked.Token);

    private async Task<Stream> ReadStreamAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_linked.Token, cancellationToken);
#if NETSTANDARD2_1
        var stream = await _content.ReadAsStreamAsync()
            .WaitAsync(Timeout.InfiniteTimeSpan, _timeProvider, linked.Token).ConfigureAwait(false);
#else
        var stream = await _content.ReadAsStreamAsync(linked.Token)
            .WaitAsync(Timeout.InfiniteTimeSpan, _timeProvider, linked.Token).ConfigureAwait(false);
#endif
        return new ResponseStream(stream, _linked.Token);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _content.Dispose();
            _linked.Dispose();
            _timeout.Dispose();
        }

        base.Dispose(disposing);
    }

    private sealed class ResponseStream(Stream inner, CancellationToken timeout) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }

        public override void Flush() => inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

        public override async Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout, cancellationToken);
            return await inner.ReadAsync(buffer, offset, count, linked.Token).ConfigureAwait(false);
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(timeout, cancellationToken);
            return await inner.ReadAsync(buffer, linked.Token).ConfigureAwait(false);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                inner.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
