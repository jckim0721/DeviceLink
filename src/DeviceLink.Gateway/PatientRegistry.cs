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
            // 식별자로 검색 → 있으면 그 Patient, 없으면 생성
            var bundle = await client.SearchAsync<Patient>(new[] { $"identifier={PatientIdSystem}|{patientId}" });
            string? id = bundle?.Entry?.FirstOrDefault()?.Resource?.Id;
            if (id is null)
            {
                var patient = new Patient
                {
                    Identifier = { new Identifier(PatientIdSystem, patientId) },
                    Active = true,
                };
                var created = await client.CreateAsync(patient);
                id = created?.Id;
                Console.WriteLine($"[환자 생성] Patient/{id} ({patientId})");
            }
            else
            {
                Console.WriteLine($"[환자 확인] Patient/{id} ({patientId})");
            }
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
