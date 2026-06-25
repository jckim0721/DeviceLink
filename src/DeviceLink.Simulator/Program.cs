// DeviceLink.Simulator — 가짜 생체신호를 TCP로 송출하는 콘솔 앱 (M1 Day2)
// 심박(HR) 1종을 N초마다 파이프구분 메시지로 송출. 줄바꿈(\n)으로 메시지 구분.
// Gateway(TCP 리스너)에 client로 붙는다. 수신측 없으면 2초마다 재연결 시도.
//   테스트: 다른 탭에서  nc -lk 5000  띄우고 이 앱 실행.

using System.Net.Sockets;
using System.Text;
using DeviceLink.Core;

// --- 설정 (인자 없으면 기본값): [host] [port] [intervalSeconds] ---
string host = args.Length > 0 ? args[0] : "127.0.0.1";
int port = args.Length > 1 && int.TryParse(args[1], out var p) ? p : 5000;
double intervalSec = args.Length > 2 && double.TryParse(args[2], out var s) ? s : 1.0;
const string deviceId = "DEV-001";

Console.WriteLine($"DeviceLink.Simulator → {host}:{port}, 주기 {intervalSec}s, 장치 {deviceId}");
Console.WriteLine("심박(HR)을 파이프구분 메시지로 송출. Ctrl+C 로 종료.");

// Ctrl+C 깔끔 종료
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var rng = new Random();
double hr = 72;   // 심박 시작값, 랜덤워크로 흔든다

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
            // 심박 랜덤워크 (58~95 /min 클램프)
            hr = Math.Clamp(hr + rng.Next(-2, 3), 58, 95);
            var reading = new Reading("HR", DateTimeOffset.UtcNow, deviceId, hr, "/min");

            var bytes = Encoding.ASCII.GetBytes(reading.ToWire() + "\n");
            await stream.WriteAsync(bytes, cts.Token);
            await stream.FlushAsync(cts.Token);
            Console.WriteLine($"[송출] {reading.ToWire()}");

            await Task.Delay(TimeSpan.FromSeconds(intervalSec), cts.Token);
        }
    }
    catch (OperationCanceledException)
    {
        break;   // Ctrl+C
    }
    catch (Exception ex) when (ex is SocketException or IOException)
    {
        Console.WriteLine($"[연결 끊김/실패] {ex.Message} — 2초 후 재연결 (수신측 확인: nc -lk {port})");
        try { await Task.Delay(TimeSpan.FromSeconds(2), cts.Token); }
        catch (OperationCanceledException) { break; }
    }
}

Console.WriteLine("Simulator 종료.");
