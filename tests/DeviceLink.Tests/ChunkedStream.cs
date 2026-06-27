namespace DeviceLink.Tests;

/// <summary>
/// 테스트용 읽기 전용 스트림. ReadAsync 한 번에 미리 정한 청크 하나씩만 돌려줘
/// "TCP가 바이트를 쪼개 준다"는 상황을 결정론적으로 재현한다. 청크 소진 후엔 0(EOF).
/// </summary>
public sealed class ChunkedStream : Stream
{
    private readonly Queue<byte[]> _chunks;

    public ChunkedStream(params byte[][] chunks) => _chunks = new Queue<byte[]>(chunks);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        if (_chunks.Count == 0) return ValueTask.FromResult(0);
        var chunk = _chunks.Dequeue();
        int n = Math.Min(chunk.Length, buffer.Length);
        chunk.AsSpan(0, n).CopyTo(buffer.Span);
        return ValueTask.FromResult(n);
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
