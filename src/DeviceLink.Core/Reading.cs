namespace DeviceLink.Core;

/// <summary>
/// 장치에서 나온 생체신호 측정 한 건(송출측 도메인 모델).
/// Simulator가 랜덤워크로 만들어 OruBuilder가 ORU^R01의 OBX로 인코딩한다.
/// Metric은 MetricCatalog 키(예: "HR", "SpO2", "TEMP", "NIBPs"/"NIBPd"/"NIBPm").
/// </summary>
public record Reading(
    string Metric,
    DateTimeOffset Timestamp,
    string DeviceId,
    double Value,
    string Unit);              // UCUM 단위 문자열 (예: "/min", "%", "Cel", "mm[Hg]")
