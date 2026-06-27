using DeviceLink.Gateway;
using Hl7.Fhir.Model;

namespace DeviceLink.Tests;

/// <summary>
/// OBX → FHIR 매핑 규칙: 스칼라는 개별 Observation, 혈압 3종은 panel+component.
/// 네트워크 없이 ParsedOru를 직접 만들어 매퍼만 검증한다.
/// </summary>
public class ObservationMapperTests
{
    private static readonly DateTimeOffset Ts = new(2026, 6, 26, 3, 45, 16, TimeSpan.Zero);

    private static ObxResult Obx(string loinc, double v, string unit) =>
        new(loinc, loinc, v, unit, Ts, "DEV-1");

    private static ParsedOru Oru(params ObxResult[] r) => new("P-1", r);

    [Fact]
    public void Scalar_obx_maps_to_single_observation()
    {
        var subject = new ResourceReference("Patient/123");
        var obs = ObservationMapper.ToObservations(Oru(Obx("8867-4", 72, "/min")), subject).Single();

        Assert.Equal(ObservationStatus.Final, obs.Status);
        Assert.Equal("8867-4", obs.Code.Coding[0].Code);
        Assert.Equal("vital-signs", obs.Category[0].Coding[0].Code);
        var q = Assert.IsType<Quantity>(obs.Value);
        Assert.Equal(72m, q.Value);
        Assert.Equal("/min", q.Code);
        Assert.Equal("http://unitsofmeasure.org", q.System);
        Assert.Equal("Patient/123", obs.Subject!.Reference);
        Assert.Equal("DEV-1", obs.Device!.Identifier!.Value);
    }

    [Fact]
    public void Three_bp_obx_collapse_into_one_panel_with_components()
    {
        var observations = ObservationMapper.ToObservations(Oru(
            Obx("8480-6", 120, "mm[Hg]"),
            Obx("8462-4", 80, "mm[Hg]"),
            Obx("8478-0", 93, "mm[Hg]")), null).ToList();

        var panel = Assert.Single(observations);
        Assert.Equal("85354-9", panel.Code.Coding[0].Code);   // Blood pressure panel
        Assert.Null(panel.Value);                              // panel 자체엔 값 없음
        Assert.Equal(3, panel.Component.Count);

        AssertComponent(panel, "8480-6", 120);
        AssertComponent(panel, "8462-4", 80);
        AssertComponent(panel, "8478-0", 93);
    }

    [Fact]
    public void Scalars_and_bp_mix_yields_scalars_plus_one_panel()
    {
        var observations = ObservationMapper.ToObservations(Oru(
            Obx("8867-4", 72, "/min"),
            Obx("59408-5", 98, "%"),
            Obx("8480-6", 120, "mm[Hg]"),
            Obx("8462-4", 80, "mm[Hg]")), null).ToList();

        // HR + SpO2(스칼라 2) + 혈압 panel 1 = 3
        Assert.Equal(3, observations.Count);
        Assert.Contains(observations, o => o.Code.Coding[0].Code == "8867-4" && o.Component.Count == 0);
        Assert.Contains(observations, o => o.Code.Coding[0].Code == "59408-5" && o.Component.Count == 0);
        Assert.Contains(observations, o => o.Code.Coding[0].Code == "85354-9" && o.Component.Count == 2);
    }

    [Fact]
    public void Bp_panel_built_even_if_mean_missing()
    {
        var panel = ObservationMapper.ToObservations(Oru(
            Obx("8480-6", 118, "mm[Hg]"),
            Obx("8462-4", 76, "mm[Hg]")), null).Single();

        Assert.Equal("85354-9", panel.Code.Coding[0].Code);
        Assert.Equal(2, panel.Component.Count);
    }

    [Fact]
    public void Null_subject_is_allowed()
    {
        var obs = ObservationMapper.ToObservations(Oru(Obx("8310-5", 36.7, "Cel")), null).Single();
        Assert.Null(obs.Subject);
    }

    private static void AssertComponent(Observation panel, string loinc, decimal value)
    {
        var c = panel.Component.Single(x => x.Code.Coding[0].Code == loinc);
        Assert.Equal(value, ((Quantity)c.Value!).Value);
    }
}
