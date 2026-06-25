// DeviceLink.Gateway — TCP 수신 → Reading 파싱 → FHIR Observation 변환 → FHIR 서버 POST
// (M1 Day5) 전체 파이프라인 1종 동작 = v0: \n framing 파싱 → Observation 매핑 → HAPI에 POST → 부여 id 회수.
//   잘못된 메시지는 버리고 한 줄 경고(견고화는 M2). POST 실패는 그 건만 격리.
//   테스트: 이 앱 먼저 실행 → 다른 탭에서 Simulator 실행(127.0.0.1:5000).

using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using DeviceLink.Core;
using DeviceLink.Gateway;
using Hl7.Fhir.Rest;
using Hl7.Fhir.Serialization;

// FHIR JSON 직렬화 옵션 한 번만 구성(pretty). 불변·재사용 안전 — 핸들러들이 공유.
// (Hl7.Fhir.Model을 using하면 Task가 System.Threading.Tasks.Task와 충돌해 정규명 사용)
var fhirJson = new JsonSerializerOptions()
    .ForFhir(Hl7.Fhir.Model.ModelInfo.ModelInspector)
    .Pretty();

// --- 설정 (인자 없으면 기본값): [bindHost] [port] [fhirBaseUrl] ---
//   bindHost 0.0.0.0 = 모든 인터페이스. 로컬만이면 127.0.0.1.
//   fhirBaseUrl "-" = POST 스킵(오프라인일 때 JSON만 출력).
string bindHost = args.Length > 0 ? args[0] : "0.0.0.0";
int port = args.Length > 1 && int.TryParse(args[1], out var p) ? p : 5000;
string fhirBaseUrl = args.Length > 2 ? args[2] : "https://hapi.fhir.org/baseR4";
var bindAddr = IPAddress.Parse(bindHost);

// FhirClient 하나를 핸들러들이 공유(스레드 안전). "-"면 POST 안 하고 JSON만 찍는다.
FhirClient? fhir = fhirBaseUrl == "-"
    ? null
    : new FhirClient(fhirBaseUrl, new FhirClientSettings { PreferredFormat = ResourceFormat.Json });

Console.WriteLine($"DeviceLink.Gateway ← {bindHost}:{port} 수신 대기. " +
    (fhir is null ? "(POST 비활성 — JSON만)" : $"FHIR → {fhirBaseUrl}") + " Ctrl+C 로 종료.");

// Ctrl+C 깔끔 종료
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var listener = new TcpListener(bindAddr, port);
listener.Start();

try
{
    // accept loop — 클라이언트(장치)마다 핸들러 태스크 하나
    while (!cts.IsCancellationRequested)
    {
        var client = await listener.AcceptTcpClientAsync(cts.Token);
        _ = HandleClientAsync(client, fhirJson, fhir, cts.Token);
    }
}
catch (OperationCanceledException)
{
    // Ctrl+C — 정상 종료 경로
}
finally
{
    listener.Stop();
    fhir?.Dispose();
    Console.WriteLine("Gateway 종료.");
}

// 한 클라이언트 연결을 끝까지: \n 단위 라인 → Reading 파싱 → Observation 매핑 → POST(또는 JSON)
static async Task HandleClientAsync(TcpClient client, JsonSerializerOptions fhirJson, FhirClient? fhir, CancellationToken ct)
{
    var remote = client.Client.RemoteEndPoint?.ToString() ?? "?";
    Console.WriteLine($"[연결됨] {remote}");
    int ok = 0, bad = 0;
    try
    {
        using (client)
        await using (var stream = client.GetStream())
        using (var reader = new StreamReader(stream, System.Text.Encoding.ASCII))
        {
            // ReadLineAsync 가 \n(및 \r\n) framing·버퍼링을 처리한다.
            while (!ct.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line is null) break;            // 상대가 연결 종료
                if (line.Length == 0) continue;     // 빈 줄 무시

                if (Reading.TryParse(line, out var reading) && reading is not null)
                {
                    ok++;
                    Console.WriteLine(
                        $"[수신] {reading.Metric} {reading.Value}{reading.Unit} " +
                        $"@ {reading.Timestamp:O} (장치 {reading.DeviceId})");

                    // reading → FHIR Observation 변환
                    Hl7.Fhir.Model.Observation obs;
                    try
                    {
                        obs = ObservationMapper.ToObservation(reading);
                    }
                    catch (NotSupportedException ex)
                    {
                        Console.WriteLine($"[변환 생략] {ex.Message}");
                        continue;
                    }

                    if (fhir is null)
                    {
                        // POST 비활성 — Observation JSON만 출력(Day4 모드)
                        Console.WriteLine(JsonSerializer.Serialize(obs, fhirJson));
                        continue;
                    }

                    // HAPI에 POST → 서버가 부여한 id/version 회수. 실패는 이 건만 버린다.
                    try
                    {
                        var created = await fhir.CreateAsync(obs);
                        Console.WriteLine(
                            $"[POST] Observation/{created?.Id} (version {created?.Meta?.VersionId}) ← {reading.Metric} {reading.Value}{reading.Unit}");
                    }
                    catch (Exception ex) when (ex is FhirOperationException or HttpRequestException)
                    {
                        Console.WriteLine($"[POST 실패] {reading.Metric} {reading.Value}{reading.Unit}: {ex.Message}");
                    }
                }
                else
                {
                    bad++;
                    Console.WriteLine($"[무시] 파싱 실패: \"{line}\"");
                }
            }
        }
    }
    catch (OperationCanceledException)
    {
        // 종료 중 — 무시
    }
    catch (Exception ex) when (ex is SocketException or IOException)
    {
        Console.WriteLine($"[연결 오류] {remote}: {ex.Message}");
    }
    finally
    {
        Console.WriteLine($"[연결 종료] {remote} (수신 {ok}, 무시 {bad})");
    }
}
