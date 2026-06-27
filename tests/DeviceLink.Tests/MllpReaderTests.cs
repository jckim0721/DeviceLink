using System.Text;
using DeviceLink.Gateway;

namespace DeviceLink.Tests;

/// <summary>
/// MLLP 프레이밍: 바이트 스트림에서 메시지 경계(&lt;VT&gt;..&lt;FS&gt;&lt;CR&gt;)를 정확히 잘라내는지.
/// TCP 현실(부분 수신·다중 메시지·앞선 쓰레기)을 ChunkedStream으로 재현한다.
/// </summary>
public class MllpReaderTests
{
    private const byte VT = 0x0B, FS = 0x1C, CR = 0x0D;

    private static byte[] Frame(string body)
    {
        var b = Encoding.ASCII.GetBytes(body);
        var f = new byte[b.Length + 3];
        f[0] = VT; b.CopyTo(f, 1); f[^2] = FS; f[^1] = CR;
        return f;
    }

    [Fact]
    public async Task Reads_single_frame()
    {
        var reader = new MllpReader(new ChunkedStream(Frame("MSH|hello")));
        Assert.Equal("MSH|hello", await reader.ReadMessageAsync(default));
        Assert.Null(await reader.ReadMessageAsync(default));   // EOF
    }

    [Fact]
    public async Task Reads_two_frames_from_one_buffer()
    {
        // 한 번의 수신에 두 메시지가 붙어 들어와도 각각 분리된다.
        var bytes = Frame("AAA").Concat(Frame("BBB")).ToArray();
        var reader = new MllpReader(new ChunkedStream(bytes));

        Assert.Equal("AAA", await reader.ReadMessageAsync(default));
        Assert.Equal("BBB", await reader.ReadMessageAsync(default));
        Assert.Null(await reader.ReadMessageAsync(default));
    }

    [Fact]
    public async Task Reassembles_frame_split_across_reads()
    {
        // 한 메시지가 여러 read에 쪼개져 와도 버퍼 누적으로 복원.
        var reader = new MllpReader(new ChunkedStream(
            new byte[] { VT, (byte)'H', (byte)'E' },
            new byte[] { (byte)'L', (byte)'L' },
            new byte[] { (byte)'O', FS, CR }));

        Assert.Equal("HELLO", await reader.ReadMessageAsync(default));
    }

    [Fact]
    public async Task Ignores_bytes_before_start_block()
    {
        // VT 앞의 잡음은 무시하고 프레임만 집는다.
        var bytes = Encoding.ASCII.GetBytes("junk").Concat(Frame("BODY")).ToArray();
        var reader = new MllpReader(new ChunkedStream(bytes));

        Assert.Equal("BODY", await reader.ReadMessageAsync(default));
    }
}
