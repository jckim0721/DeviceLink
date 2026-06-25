using System.Globalization;

namespace DeviceLink.Core;

/// <summary>
/// 장치에서 나온 생체신호 측정 한 건. Simulator가 만들고 Gateway가 받는다.
/// 와이어 포맷(파이프구분)은 Simulator·Gateway 공유 계약이라 여기 둔다.
/// </summary>
public record Reading(
    string Metric,            // 측정 종류 코드 (예: "HR", "SpO2", "TEMP", "NIBP")
    DateTimeOffset Timestamp,
    string DeviceId,
    double Value,
    string Unit)              // UCUM 단위 문자열 (예: "/min", "%", "Cel")
{
    /// <summary>
    /// 와이어 포맷으로 직렬화: metric|timestamp(ISO8601 UTC)|deviceId|value|unit
    /// 한 줄 = 한 Reading. 줄바꿈(\n)으로 메시지를 구분한다(framing은 송출측 책임).
    /// 예) HR|2026-06-25T14:05:01Z|DEV-001|72|/min
    /// </summary>
    public string ToWire() => string.Join('|',
        Metric,
        Timestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
        DeviceId,
        Value.ToString(CultureInfo.InvariantCulture),
        Unit);

    /// <summary>
    /// 와이어 포맷 한 줄을 Reading으로 역파싱. ToWire()의 역방향이라 여기 둔다.
    /// 필드 수(5)·timestamp·value가 모두 유효해야 true. 하나라도 어긋나면 false
    /// (잘못된 메시지는 수신측이 조용히 버리고 로깅 — M2 견고화 지점).
    /// 입력의 앞뒤 공백과 끝의 \r(CRLF 송출 대비)은 무시한다.
    /// </summary>
    public static bool TryParse(string? line, out Reading? reading)
    {
        reading = null;
        if (string.IsNullOrWhiteSpace(line)) return false;

        var parts = line.Trim().Split('|');
        if (parts.Length != 5) return false;

        var metric = parts[0];
        var deviceId = parts[2];
        if (metric.Length == 0 || deviceId.Length == 0) return false;

        if (!DateTimeOffset.TryParse(
                parts[1], CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var timestamp))
            return false;

        if (!double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return false;

        var unit = parts[4];
        if (unit.Length == 0) return false;

        reading = new Reading(metric, timestamp, deviceId, value, unit);
        return true;
    }
}
