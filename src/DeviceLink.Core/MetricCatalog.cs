namespace DeviceLink.Core;

/// <summary>
/// 한 생체신호 종류의 표준 식별 정보: LOINC 코드 + 표시명 + 기대 UCUM 단위.
/// FHIR에 의존하지 않는 순수 데이터 — Firely 매핑은 Gateway 쪽 책임.
/// </summary>
public record MetricInfo(string Loinc, string Display, string UcumUnit);

/// <summary>
/// metric 코드(와이어의 첫 필드) → 표준 코드 매핑표. "표준 코드 매핑"은 Core가 쥔다(CLAUDE.md).
/// LOINC/UCUM 출처: loinc.org. 스칼라 1값짜리만 여기 둔다 —
/// NIBP(혈압)는 systolic(8480-6)/diastolic(8462-4) component 구조라 별도 매퍼 필요(M2).
/// </summary>
public static class MetricCatalog
{
    private static readonly IReadOnlyDictionary<string, MetricInfo> Map =
        new Dictionary<string, MetricInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["HR"]   = new("8867-4",  "Heart rate",                                      "/min"),
            ["TEMP"] = new("8310-5",  "Body temperature",                                "Cel"),
            ["SpO2"] = new("59408-5", "Oxygen saturation in Arterial blood by Pulse oximetry", "%"),
            // 혈압은 HL7 v2에서 값마다 OBX로 분리돼 흐른다(수축기/이완기/평균).
            // 수신측 게이트웨이가 이 셋을 FHIR Blood pressure panel(85354-9)의 component로 묶는다.
            ["NIBPs"] = new("8480-6", "Systolic blood pressure",  "mm[Hg]"),
            ["NIBPd"] = new("8462-4", "Diastolic blood pressure", "mm[Hg]"),
            ["NIBPm"] = new("8478-0", "Mean blood pressure",      "mm[Hg]"),
        };

    /// <summary>알려진 metric이면 표준 코드 정보를 돌려준다. 미등록이면 false.</summary>
    public static bool TryGet(string metric, out MetricInfo? info)
    {
        if (Map.TryGetValue(metric, out var found)) { info = found; return true; }
        info = null;
        return false;
    }
}
