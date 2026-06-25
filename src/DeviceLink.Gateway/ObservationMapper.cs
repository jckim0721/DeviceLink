using DeviceLink.Core;
using Hl7.Fhir.Model;

namespace DeviceLink.Gateway;

/// <summary>
/// 도메인 Reading 한 건 → FHIR R4 Observation 변환. 표준 코드(LOINC/UCUM)는
/// Core.MetricCatalog에서 끌어온다. FHIR 모양에 대한 판단(상태·카테고리·effective·단위계)이
/// 여기 모인다 — Gateway가 쥐는 변환 정책.
/// </summary>
public static class ObservationMapper
{
    private const string LoincSystem = "http://loinc.org";
    private const string UcumSystem  = "http://unitsofmeasure.org";
    private const string CategorySystem = "http://terminology.hl7.org/CodeSystem/observation-category";

    /// <summary>
    /// Reading을 vital-signs Observation으로 매핑. 미등록 metric이면 NotSupportedException
    /// (호출측이 잡아 그 건만 버리고 로깅하도록).
    /// </summary>
    public static Observation ToObservation(Reading r)
    {
        if (!MetricCatalog.TryGet(r.Metric, out var info) || info is null)
            throw new NotSupportedException($"미등록 metric '{r.Metric}' — MetricCatalog에 없음");

        return new Observation
        {
            Status = ObservationStatus.Final,
            Category =
            {
                new CodeableConcept
                {
                    Coding = { new Coding(CategorySystem, "vital-signs") { Display = "Vital Signs" } },
                },
            },
            // LOINC 표시명은 coding.display에 실어 코드와 함께 전달(표준 문해력).
            Code = new CodeableConcept
            {
                Coding = { new Coding(LoincSystem, info.Loinc) { Display = info.Display } },
                Text = info.Display,
            },
            // 측정 시각. Reading.Timestamp는 UTC(Simulator가 UtcNow로 찍음).
            Effective = new FhirDateTime(r.Timestamp.ToUniversalTime()),
            Value = new Quantity
            {
                Value = (decimal)r.Value,
                Unit = r.Unit,        // 사람이 읽는 단위 문자열
                System = UcumSystem,  // UCUM 단위계
                Code = info.UcumUnit, // 정규 UCUM 코드(카탈로그 기준)
            },
            // 장치 식별은 일단 Identifier로. subject(Patient 참조)는 Day6에 붙인다.
            Identifier =
            {
                new Identifier("urn:devicelink:device", r.DeviceId),
            },
        };
    }
}
