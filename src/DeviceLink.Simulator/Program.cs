// DeviceLink.Simulator — 병상 모니터를 흉내내 HL7 v2 ORU^R01을 송출하는 콘솔 앱 (M2)
// 생체신호 랜덤워크 → ORU^R01(MSH+PID+OBR+OBX) 구성 → MLLP 프레이밍으로 TCP 송출.
// Gateway(MLLP 리스너)에 client로 붙는다. 수신측 없으면 2초마다 재연결 시도.
//   생체신호 4종: 심박·SpO2·체온·혈압(수축기/이완기/평균). 혈압은 값마다 OBX 하나.

using System.Globalization;
using System.Net.Sockets;
using System.Text;
using DeviceLink.Core;
using DeviceLink.Simulator;

// --- 설정 (인자 없으면 기본값): [host] [port] [intervalSeconds] ---
string host = args.Length > 0 ? args[0] : "127.0.0.1";
int port = args.Length > 1 && int.TryParse(args[1], out var p) ? p : 5000;
double intervalSec = args.Length > 2 && double.TryParse(args[2], out var s) ? s : 1.0;
const string deviceId = "DEV-001";
const string patientId = "P-001";

// MLLP 프레이밍 바이트: <VT> ...메시지... <FS><CR>
const byte VT = 0x0B, FS = 0x1C, CR = 0x0D;

Console.WriteLine($"DeviceLink.Simulator → {host}:{port}, 주기 {intervalSec}s, 장치 {deviceId}, 환자 {patientId}");
Console.WriteLine("HL7 v2 ORU^R01을 MLLP로 송출. Ctrl+C 로 종료.");

// Ctrl+C 깔끔 종료
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var rng = new Random();
// 각 생체신호 현재값(랜덤워크 시작점)
double hr = 72, spo2 = 98, temp = 36.7, sys = 120, dia = 78;

while (!cts.IsCancellationRequested)
{
    try
    {
        using var client = new TcpClient();
        await client.ConnectAsync(host, port, cts.Token);
        Console.WriteLine($"[연결됨] {host}:{port}");
        await using var stream = client.GetStream();

        while (!cts.IsCancellationRequested)
        {
            // 4종 랜덤워크(생리 범위 클램프). 혈압 평균(MAP) = 이완기 + (수축기-이완기)/3.
            hr   = Math.Clamp(hr   + rng.Next(-2, 3),       58, 95);
            spo2 = Math.Clamp(spo2 + rng.Next(-1, 2),       90, 100);
            temp = Math.Clamp(temp + (rng.NextDouble() - 0.5) * 0.2, 36.0, 38.5);
            sys  = Math.Clamp(sys  + rng.Next(-3, 4),       100, 145);
            dia  = Math.Clamp(dia  + rng.Next(-2, 3),       60, 95);
            double map = Math.Round(dia + (sys - dia) / 3.0);

            var now = DateTimeOffset.UtcNow;
            var readings = new[]
            {
                new Reading("HR",    now, deviceId, hr,             "/min"),
                new Reading("SpO2",  now, deviceId, spo2,           "%"),
                new Reading("TEMP",  now, deviceId, Math.Round(temp, 1), "Cel"),
                new Reading("NIBPs", now, deviceId, sys,            "mm[Hg]"),
                new Reading("NIBPd", now, deviceId, dia,            "mm[Hg]"),
                new Reading("NIBPm", now, deviceId, map,            "mm[Hg]"),
            };

            // ORU^R01 생성 → MLLP 프레임으로 감싸 송출
            string er7 = OruBuilder.BuildEr7(deviceId, patientId, now, readings);
            var body = Encoding.ASCII.GetBytes(er7);
            var frame = new byte[body.Length + 3];
            frame[0] = VT;
            body.CopyTo(frame, 1);
            frame[^2] = FS;
            frame[^1] = CR;

            await stream.WriteAsync(frame, cts.Token);
            await stream.FlushAsync(cts.Token);
            Console.WriteLine($"[송출] ORU^R01 {readings.Length} OBX " +
                $"(HR {hr}, SpO2 {spo2}, T {Math.Round(temp, 1)}, BP {sys}/{dia} MAP {map}) {body.Length}b");

            await Task.Delay(TimeSpan.FromSeconds(intervalSec), cts.Token);
        }
    }
    catch (OperationCanceledException)
    {
        break;   // Ctrl+C
    }
    catch (Exception ex) when (ex is SocketException or IOException)
    {
        Console.WriteLine($"[연결 끊김/실패] {ex.Message} — 2초 후 재연결");
        try { await Task.Delay(TimeSpan.FromSeconds(2), cts.Token); }
        catch (OperationCanceledException) { break; }
    }
}

Console.WriteLine("Simulator 종료.");
