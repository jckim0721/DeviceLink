using System.Collections.Concurrent;
using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;

namespace DeviceLink.Gateway;

/// <summary>
/// HL7 PID의 환자 식별자로 FHIR 서버에 Patient를 보장(없으면 생성)하고 subject 참조를 돌려준다.
/// 환자당 한 번만 서버에 묻도록 캐시한다. 서버 오류 시엔 식별자 기반 논리 참조로 폴백(연결을 막지 않음).
/// </summary>
public sealed class PatientRegistry(FhirClient client)
{
    private const string PatientIdSystem = "urn:devicelink:patient";
    private readonly ConcurrentDictionary<string, Task<ResourceReference>> _cache = new();

    public Task<ResourceReference> EnsureAsync(string patientId) =>
        _cache.GetOrAdd(patientId, EnsureCore);

    private async Task<ResourceReference> EnsureCore(string patientId)
    {
        try
        {
            var patient = new Patient
            {
                Identifier = { new Identifier(PatientIdSystem, patientId) },
                Active = true,
            };
            // 조건부 생성(If-None-Exist): 같은 식별자가 이미 있으면 그걸 돌려주고, 없을 때만 만든다.
            // 서버가 원자적으로 처리 → "검색 후 생성" 사이의 race·중복 생성을 피한다.
            var saved = await client.ConditionalCreateAsync(
                patient, new SearchParams().Where($"identifier={PatientIdSystem}|{patientId}"));
            string? id = saved?.Id;
            Console.WriteLine($"[환자] Patient/{id} ({patientId})");
            return new ResourceReference($"Patient/{id}") { Display = $"Patient {patientId}" };
        }
        catch (Exception ex)
        {
            // 서버가 안 되면 식별자 기반 논리 참조로라도 연결해 둔다.
            Console.WriteLine($"[환자 보장 실패] {patientId}: {ex.Message} — 식별자 참조로 폴백");
            return new ResourceReference
            {
                Identifier = new Identifier(PatientIdSystem, patientId),
                Display = $"Patient {patientId}",
            };
        }
    }
}
