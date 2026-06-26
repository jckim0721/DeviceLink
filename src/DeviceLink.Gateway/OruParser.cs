using System.Globalization;
using NHapi.Base.Model;
using NHapi.Base.Parser;
using NHapi.Model.V251.Message;

namespace DeviceLink.Gateway;

/// <summary>OBX 한 줄(측정 결과 하나)을 HL7에서 그대로 뽑아온 값. 코드는 메시지가 실어온 LOINC 그대로 쓴다.</summary>
public record ObxResult(string Loinc, string Display, double Value, string Unit, DateTimeOffset Time, string DeviceId);

/// <summary>같은 환자·오더(OBR) 묶음으로 수신한 측정 set.</summary>
public record ParsedOru(string PatientId, IReadOnlyList<ObxResult> Results);

/// <summary>
/// 수신한 HL7 v2 ORU^R01(ER7)을 NHapi로 파싱해 도메인 값으로 꺼낸다.
/// "묶음(OBR) 단위로 OBX들을 모은다"는 HL7 구조가 그대로 ParsedOru로 옮겨진다.
/// FHIR 변환(스칼라 vs 혈압 panel)은 다음 단계의 매퍼 책임.
/// </summary>
public static class OruParser
{
    private static readonly PipeParser Parser = new();

    public static ParsedOru Parse(string er7)
    {
        var msg = Parser.Parse(er7);
        if (msg is not ORU_R01 oru)
            throw new NotSupportedException($"지원하지 않는 메시지 구조: {msg.GetStructureName()}");

        var patientResult = oru.GetPATIENT_RESULT(0);
        string patientId = patientResult.PATIENT.PID.GetPatientIdentifierList(0).IDNumber.Value ?? "";

        var order = patientResult.GetORDER_OBSERVATION(0);
        int count = order.OBSERVATIONRepetitionsUsed;

        var results = new List<ObxResult>(count);
        for (int i = 0; i < count; i++)
        {
            var obx = order.GetOBSERVATION(i).OBX;

            string loinc = obx.ObservationIdentifier.Identifier.Value ?? "";
            string display = obx.ObservationIdentifier.Text.Value ?? "";
            string unit = obx.Units.Identifier.Value ?? "";
            string deviceId = obx.GetEquipmentInstanceIdentifier(0).EntityIdentifier.Value ?? "";

            // OBX-5 값: Varies 안의 primitive 문자열을 꺼내 숫자로
            string raw = obx.GetObservationValue(0).Data is IPrimitive prim ? prim.Value ?? "" : "";
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                continue;   // 숫자 아닌 OBX는 건너뜀(견고화)

            // OBX-14 측정시각(yyyyMMddHHmmss, UTC 가정). 없으면 지금.
            string tsRaw = obx.DateTimeOfTheObservation.Time.Value ?? "";
            var time = DateTimeOffset.TryParseExact(
                tsRaw, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t)
                ? t : DateTimeOffset.UtcNow;

            results.Add(new ObxResult(loinc, display, value, unit, time, deviceId));
        }

        return new ParsedOru(patientId, results);
    }
}
