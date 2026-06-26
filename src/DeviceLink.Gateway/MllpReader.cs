using System.Text;

namespace DeviceLink.Gateway;

/// <summary>
/// MLLP(Minimal Lower Layer Protocol) 프레임 리더. 스트림에서 한 메시지씩 꺼낸다.
/// 프레임: &lt;VT=0x0B&gt; ...HL7 ER7... &lt;FS=0x1C&gt;&lt;CR=0x0D&gt;.
/// TCP는 바이트 스트림이라 한 read에 여러 프레임/부분 프레임이 섞일 수 있어
/// 내부 버퍼에 누적하며 경계를 직접 찾는다.
/// </summary>
public sealed class MllpReader(Stream stream)
{
    private const byte VT = 0x0B, FS = 0x1C, CR = 0x0D;
    private readonly byte[] _tmp = new byte[4096];
    private readonly List<byte> _pending = new();

    /// <summary>다음 완전한 메시지(ER7 문자열). 상대가 연결을 닫으면 null.</summary>
    public async Task<string?> ReadMessageAsync(CancellationToken ct)
    {
        while (true)
        {
            // 버퍼에서 완성된 프레임(VT..FS)을 먼저 찾는다.
            int vt = _pending.IndexOf(VT);
            int fs = vt >= 0 ? _pending.IndexOf(FS, vt + 1) : -1;
            if (vt >= 0 && fs > vt)
            {
                var body = _pending.GetRange(vt + 1, fs - vt - 1).ToArray();
                int removeTo = fs + 1;
                if (removeTo < _pending.Count && _pending[removeTo] == CR) removeTo++;  // 끝의 CR 흡수
                _pending.RemoveRange(0, removeTo);
                return Encoding.ASCII.GetString(body);
            }

            int n = await stream.ReadAsync(_tmp, ct);
            if (n == 0) return null;   // 연결 종료
            for (int i = 0; i < n; i++) _pending.Add(_tmp[i]);
        }
    }
}
