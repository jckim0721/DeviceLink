using DeviceLink.Core;
using DeviceLink.Gateway;
using DeviceLink.Simulator;

namespace DeviceLink.Tests;

/// <summary>
/// Simulator가 만든 ORU^R01을 Gateway가 파싱하면 값이 보존되는지(왕복 무손실).
/// HL7 v2 생성↔파싱 계약을 한 번에 검증한다.
/// </summary>
public class OruRoundTripTests
{
    private static readonly DateTimeOffset Ts =
        new(2026, 6, 26, 3, 45, 16, TimeSpan.Zero);

    [Fact]
    public void Build_then_parse_preserves_all_obx_values()
    {
        var readings = new[]
        {
            new Reading("HR",    Ts, "DEV-001", 72,   "/min"),
            new Reading("SpO2",  Ts, "DEV-001", 98,   "%"),
            new Reading("TEMP",  Ts, "DEV-001", 36.7, "Cel"),
            new Reading("NIBPs", Ts, "DEV-001", 120,  "mm[Hg]"),
            new Reading("NIBPd", Ts, "DEV-001", 80,   "mm[Hg]"),
            new Reading("NIBPm", Ts, "DEV-001", 93,   "mm[Hg]"),
        };

        var er7 = OruBuilder.BuildEr7("DEV-001", "P-001", Ts, readings);
        var parsed = OruParser.Parse(er7);

        Assert.Equal("P-001", parsed.PatientId);
        Assert.Equal(6, parsed.Results.Count);

        // LOINC 기준으로 각 측정값/단위/장치가 그대로 돌아왔는지
        AssertObx(parsed, "8867-4", 72, "/min");
        AssertObx(parsed, "59408-5", 98, "%");
        AssertObx(parsed, "8310-5", 36.7, "Cel");
        AssertObx(parsed, "8480-6", 120, "mm[Hg]");
        AssertObx(parsed, "8462-4", 80, "mm[Hg]");
        AssertObx(parsed, "8478-0", 93, "mm[Hg]");

        Assert.All(parsed.Results, r => Assert.Equal("DEV-001", r.DeviceId));
        Assert.All(parsed.Results, r => Assert.Equal(Ts, r.Time));
    }

    [Fact]
    public void Build_carries_display_name_from_catalog()
    {
        var er7 = OruBuilder.BuildEr7("DEV-9", "P-9", Ts,
            new[] { new Reading("HR", Ts, "DEV-9", 60, "/min") });
        var parsed = OruParser.Parse(er7);

        Assert.Equal("Heart rate", parsed.Results.Single().Display);
    }

    [Fact]
    public void Unknown_metric_is_skipped_when_building()
    {
        // 카탈로그에 없는 metric은 OBX로 나가지 않는다 → 파싱 결과에서 빠진다.
        var er7 = OruBuilder.BuildEr7("DEV-1", "P-1", Ts, new[]
        {
            new Reading("HR", Ts, "DEV-1", 70, "/min"),
            new Reading("BOGUS", Ts, "DEV-1", 1, "x"),
        });
        var parsed = OruParser.Parse(er7);

        Assert.Single(parsed.Results);
        Assert.Equal("8867-4", parsed.Results[0].Loinc);
    }

    private static void AssertObx(ParsedOru parsed, string loinc, double value, string unit)
    {
        var obx = parsed.Results.Single(r => r.Loinc == loinc);
        Assert.Equal(value, obx.Value, 3);
        Assert.Equal(unit, obx.Unit);
    }
}
