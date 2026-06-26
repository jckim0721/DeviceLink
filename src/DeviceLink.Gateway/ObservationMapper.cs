using Hl7.Fhir.Model;

namespace DeviceLink.Gateway;

/// <summary>
/// 수신·파싱한 측정 set(ParsedOru) → FHIR R4 Observation들로 변환.
/// 스칼라(HR·SpO2·체온)는 각각 Observation 하나. 혈압 OBX 3종(수축기/이완기/평균)은
/// FHIR vital-signs 규약대로 Blood pressure panel(85354-9) 하나에 component로 묶는다.
/// LOINC/단위는 메시지가 실어온 값을 그대로 쓴다(코드 출처는 송신 장치).
/// </summary>
public static class ObservationMapper
{
    private const string LoincSystem = "http://loinc.org";
    private const string UcumSystem = "http://unitsofmeasure.org";
    private const string CategorySystem = "http://terminology.hl7.org/CodeSystem/observation-category";

    // 혈압 component LOINC + panel 코드
    private const string SystolicLoinc = "8480-6";
    private const string DiastolicLoinc = "8462-4";
    private const string MeanLoinc = "8478-0";
    private const string BpPanelLoinc = "85354-9";

    private static readonly HashSet<string> BpLoincs = new() { SystolicLoinc, DiastolicLoinc, MeanLoinc };

    /// <summary>측정 set을 FHIR Observation들로. subject는 환자 참조(없으면 null).</summary>
    public static IEnumerable<Observation> ToObservations(ParsedOru oru, ResourceReference? subject)
    {
        var bp = new List<ObxResult>();

        foreach (var r in oru.Results)
        {
            if (BpLoincs.Contains(r.Loinc)) { bp.Add(r); continue; }
            yield return Scalar(r, subject);
        }

        // 혈압: 수축기/이완기 중 하나라도 있으면 panel 하나로 묶는다.
        if (bp.Any(r => r.Loinc is SystolicLoinc or DiastolicLoinc))
            yield return BloodPressurePanel(bp, subject);
    }

    private static Observation Scalar(ObxResult r, ResourceReference? subject)
    {
        var obs = BaseObservation(r.Loinc, r.Display, r.Time, r.DeviceId, subject);
        obs.Value = Quantity(r.Value, r.Unit);
        return obs;
    }

    private static Observation BloodPressurePanel(IReadOnlyList<ObxResult> bp, ResourceReference? subject)
    {
        var first = bp[0];
        var obs = BaseObservation(BpPanelLoinc, "Blood pressure panel", first.Time, first.DeviceId, subject);
        foreach (var r in bp)
        {
            obs.Component.Add(new Observation.ComponentComponent
            {
                Code = Code(r.Loinc, r.Display),
                Value = Quantity(r.Value, r.Unit),
            });
        }
        return obs;
    }

    // status·category·code·effective·device·subject까지 공통으로 채운 Observation
    private static Observation BaseObservation(
        string loinc, string display, DateTimeOffset time, string deviceId, ResourceReference? subject)
    {
        var obs = new Observation
        {
            Status = ObservationStatus.Final,
            Category =
            {
                new CodeableConcept
                {
                    Coding = { new Coding(CategorySystem, "vital-signs") { Display = "Vital Signs" } },
                },
            },
            Code = Code(loinc, display),
            Effective = new FhirDateTime(time.ToUniversalTime()),
            Subject = subject,
        };
        if (!string.IsNullOrEmpty(deviceId))
            obs.Device = new ResourceReference { Identifier = new Identifier("urn:devicelink:device", deviceId) };
        return obs;
    }

    private static CodeableConcept Code(string loinc, string display) => new()
    {
        Coding = { new Coding(LoincSystem, loinc) { Display = display } },
        Text = display,
    };

    private static Quantity Quantity(double value, string unit) => new()
    {
        Value = (decimal)value,
        Unit = unit,
        System = UcumSystem,
        Code = unit,
    };
}
