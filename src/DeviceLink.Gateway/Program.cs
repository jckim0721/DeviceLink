// DeviceLink.Gateway — HL7 v2 ORU^R01 수신 → NHapi 파싱 → FHIR 변환 → HAPI POST
// (M2) MLLP 리스너로 ORU^R01을 받아 OBX를 FHIR Observation으로 매핑(스칼라 + 혈압 panel),
//   PID 환자를 Patient로 보장해 subject 참조, HAPI에 POST하고 부여 id 회수.
//   잘못된 메시지/ POST 실패는 건별 격리. fhirBaseUrl "-" 면 POST 스킵(매핑만).
//   테스트: 이 앱 먼저 실행 → 다른 탭에서 Simulator 실행(127.0.0.1:5000).

using System.Net;
using System.Net.Sockets;
using DeviceLink.Gateway;
using Hl7.Fhir.Rest;

// --- 설정 (인자 없으면 기본값): [bindHost] [port] [fhirBaseUrl] ---
//   fhirBaseUrl "-" = POST 스킵(매핑만 로그).
string bindHost = args.Length > 0 ? args[0] : "0.0.0.0";
int port = args.Length > 1 && int.TryParse(args[1], out var p) ? p : 5000;
string fhirBaseUrl = args.Length > 2 ? args[2] : "https://hapi.fhir.org/baseR4";
var bindAddr = IPAddress.Parse(bindHost);

FhirClient? fhir = fhirBaseUrl == "-"
    ? null
    : new FhirClient(fhirBaseUrl, new FhirClientSettings { PreferredFormat = ResourceFormat.Json });
var patients = fhir is null ? null : new PatientRegistry(fhir);

Console.WriteLine($"DeviceLink.Gateway ← {bindHost}:{port} MLLP 수신 대기. " +
    (fhir is null ? "(POST 비활성 — 매핑만)" : $"FHIR → {fhirBaseUrl}") + " Ctrl+C 로 종료.");

// Ctrl+C 깔끔 종료
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

var listener = new TcpListener(bindAddr, port);
listener.Start();

try
{
    // accept loop — 장치(클라이언트)마다 핸들러 태스크 하나
    while (!cts.IsCancellationRequested)
    {
        var client = await listener.AcceptTcpClientAsync(cts.Token);
        _ = HandleClientAsync(client, fhir, patients, cts.Token);
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

// 한 장치 연결: MLLP 프레임 → ORU^R01 파싱 → FHIR 변환 → POST
static async Task HandleClientAsync(TcpClient client, FhirClient? fhir, PatientRegistry? patients, CancellationToken ct)
{
    var remote = client.Client.RemoteEndPoint?.ToString() ?? "?";
    Console.WriteLine($"[연결됨] {remote}");
    int ok = 0, bad = 0;
    try
    {
        using (client)
        await using (var stream = client.GetStream())
        {
            var reader = new MllpReader(stream);
            while (!ct.IsCancellationRequested)
            {
                var er7 = await reader.ReadMessageAsync(ct);
                if (er7 is null) break;   // 상대가 연결 종료

                ParsedOru parsed;
                try
                {
                    parsed = OruParser.Parse(er7);
                }
                catch (Exception ex)
                {
                    bad++;
                    Console.WriteLine($"[무시] ORU 파싱 실패: {ex.Message}");
                    continue;
                }

                ok++;
                Console.WriteLine($"[수신] ORU^R01 환자 {parsed.PatientId}, 측정 {parsed.Results.Count}건");

                // 환자 subject 보장(POST 모드에서만)
                var subject = patients is null ? null : await patients.EnsureAsync(parsed.PatientId);
                var observations = ObservationMapper.ToObservations(parsed, subject);

                foreach (var obs in observations)
                {
                    string label = $"{obs.Code.Coding[0].Code} {obs.Code.Coding[0].Display}";
                    if (fhir is null)
                    {
                        Console.WriteLine($"   [매핑] {label}" + (obs.Component.Count > 0 ? $" (+{obs.Component.Count} component)" : ""));
                        continue;
                    }
                    try
                    {
                        var created = await fhir.CreateAsync(obs);
                        Console.WriteLine($"   [POST] Observation/{created?.Id} ← {label}");
                    }
                    catch (Exception ex) when (ex is FhirOperationException or HttpRequestException)
                    {
                        Console.WriteLine($"   [POST 실패] {label}: {ex.Message}");
                    }
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
        Console.WriteLine($"[연결 종료] {remote} (메시지 {ok}, 무시 {bad})");
    }
}
