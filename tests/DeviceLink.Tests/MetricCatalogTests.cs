using DeviceLink.Core;

namespace DeviceLink.Tests;

public class MetricCatalogTests
{
    [Theory]
    [InlineData("HR", "8867-4", "/min")]
    [InlineData("SpO2", "59408-5", "%")]
    [InlineData("TEMP", "8310-5", "Cel")]
    [InlineData("NIBPs", "8480-6", "mm[Hg]")]
    [InlineData("NIBPd", "8462-4", "mm[Hg]")]
    [InlineData("NIBPm", "8478-0", "mm[Hg]")]
    public void TryGet_known_metric_returns_loinc_and_ucum(string metric, string loinc, string unit)
    {
        Assert.True(MetricCatalog.TryGet(metric, out var info));
        Assert.NotNull(info);
        Assert.Equal(loinc, info!.Loinc);
        Assert.Equal(unit, info.UcumUnit);
    }

    [Fact]
    public void TryGet_is_case_insensitive()
    {
        Assert.True(MetricCatalog.TryGet("hr", out var info));
        Assert.Equal("8867-4", info!.Loinc);
    }

    [Fact]
    public void TryGet_unknown_metric_returns_false()
    {
        Assert.False(MetricCatalog.TryGet("BOGUS", out var info));
        Assert.Null(info);
    }
}
