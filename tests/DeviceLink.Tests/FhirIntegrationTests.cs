using DeviceLink.Core;
using DeviceLink.Gateway;
using DeviceLink.Simulator;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Task = System.Threading.Tasks.Task;   // Hl7.Fhir.Model.Task와 충돌 회피

namespace DeviceLink.Tests;

/// <summary>
/// 실제 HAPI 테스트 서버에 POST하고 되조회해 검증하는 통합 테스트.
/// 단위 테스트(네트워크 비의존)와 분리(Category=Integration). 서버 미접속이면 건너뛴다.
///   전체 실행:        dotnet test
///   단위만(오프라인):  dotnet test --filter Category!=Integration
/// </summary>
[Trait("Category", "Integration")]
public class FhirIntegrationTests
{
    private const string BaseUrl = "https://hapi.fhir.org/baseR4";
    private static readonly DateTimeOffset Ts = new(2026, 6, 26, 3, 45, 16, TimeSpan.Zero);

    private static FhirClient NewClient() =>
        new(BaseUrl, new FhirClientSettings { PreferredFormat = ResourceFormat.Json, Timeout = 30000 });

    private static async Task<bool> ReachableAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            var resp = await http.GetAsync($"{BaseUrl}/metadata?_summary=true");
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    [SkippableFact]
    public async Task Full_pipeline_posts_scalar_and_reads_back()
    {
        Skip.IfNot(await ReachableAsync(), "HAPI 미접속 — 통합 테스트 건너뜀");
        using var client = NewClient();

        // HAPI 공개 서버는 내용이 동일한 리소스를 중복 거부 → 실행마다 고유 장치 id로 구분.
        string deviceId = "DEV-IT-" + DateTime.UtcNow.Ticks;

        // ORU 생성 → 파싱 → 매핑 → POST (종단 체인)
        var er7 = OruBuilder.BuildEr7(deviceId, "P-IT", Ts,
            new[] { new Reading("HR", Ts, deviceId, 72, "/min") });
        var parsed = OruParser.Parse(er7);
        var obs = ObservationMapper.ToObservations(parsed, null).Single();

        var created = await client.CreateAsync(obs);
        Assert.NotNull(created?.Id);   // 서버가 id를 부여했다

        // 되조회해 값이 보존됐는지
        var fetched = await client.ReadAsync<Observation>($"Observation/{created!.Id}");
        Assert.Equal("8867-4", fetched!.Code.Coding[0].Code);
        Assert.Equal(72m, ((Quantity)fetched.Value!).Value);
        Assert.Equal(deviceId, fetched.Device!.Identifier!.Value);
    }

    [SkippableFact]
    public async Task Bp_panel_posts_with_components_and_subject()
    {
        Skip.IfNot(await ReachableAsync(), "HAPI 미접속 — 통합 테스트 건너뜀");
        using var client = NewClient();

        // 매 실행 고유 환자 → 서버에 Patient 보장 후 subject로
        string patientId = "P-IT-" + DateTime.UtcNow.Ticks;
        var subject = await new PatientRegistry(client).EnsureAsync(patientId);

        var oru = new ParsedOru(patientId, new[]
        {
            new ObxResult("8480-6", "Systolic blood pressure", 120, "mm[Hg]", Ts, "DEV-IT"),
            new ObxResult("8462-4", "Diastolic blood pressure", 80, "mm[Hg]", Ts, "DEV-IT"),
            new ObxResult("8478-0", "Mean blood pressure", 93, "mm[Hg]", Ts, "DEV-IT"),
        });
        var panel = ObservationMapper.ToObservations(oru, subject).Single();

        var created = await client.CreateAsync(panel);
        Assert.NotNull(created?.Id);

        // 되조회: panel 코드 + component 3종 + subject + device
        var fetched = await client.ReadAsync<Observation>($"Observation/{created!.Id}");
        Assert.Equal("85354-9", fetched!.Code.Coding[0].Code);
        Assert.Equal(3, fetched.Component.Count);
        Assert.Contains(fetched.Component, c => c.Code.Coding[0].Code == "8480-6" && ((Quantity)c.Value!).Value == 120m);
        Assert.Contains(fetched.Component, c => c.Code.Coding[0].Code == "8462-4" && ((Quantity)c.Value!).Value == 80m);
        Assert.StartsWith("Patient/", fetched.Subject!.Reference);
        Assert.Equal("DEV-IT", fetched.Device!.Identifier!.Value);
    }

    [SkippableFact]
    public async Task PatientRegistry_creates_retrievable_patient()
    {
        Skip.IfNot(await ReachableAsync(), "HAPI 미접속 — 통합 테스트 건너뜀");
        using var client = NewClient();

        string patientId = "P-IT-" + DateTime.UtcNow.Ticks;
        var subject = await new PatientRegistry(client).EnsureAsync(patientId);

        // 진짜 Patient 참조가 나오고, 그 Patient를 서버에서 되조회할 수 있다.
        Assert.StartsWith("Patient/", subject.Reference);
        var id = subject.Reference!.Split('/')[1];
        var fetched = await client.ReadAsync<Patient>($"Patient/{id}");
        Assert.NotNull(fetched);
        Assert.Contains(fetched!.Identifier, i => i.Value == patientId);
    }
}
