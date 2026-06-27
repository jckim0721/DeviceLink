using DeviceLink.Gateway;

namespace DeviceLink.Tests;

/// <summary>견고화: 깨진/미지원/비정상 OBX 메시지를 어떻게 처리하는지.</summary>
public class OruParserTests
{
    [Fact]
    public void Garbage_message_throws()
    {
        Assert.ThrowsAny<Exception>(() => OruParser.Parse("THIS-IS-NOT-HL7|garbage"));
    }

    [Fact]
    public void Non_oru_message_throws_NotSupported()
    {
        // 유효한 MSH지만 ADT(입퇴원) — ORU가 아니라 미지원으로 거부.
        string adt =
            "MSH|^~\\&|X|Y|Z|W|20260626||ADT^A01^ADT_A01|1|P|2.5.1\r" +
            "EVN|A01|20260626\r" +
            "PID|1||P-1||Test^Patient";
        Assert.Throws<NotSupportedException>(() => OruParser.Parse(adt));
    }

    [Fact]
    public void Non_numeric_obx_value_is_skipped()
    {
        // OBX 두 개 중 하나는 값이 숫자가 아니다 → 그 OBX만 버리고 나머지는 살린다.
        string oru =
            "MSH|^~\\&|Sim|ICU|GW|HOSP|20260626034516||ORU^R01^ORU_R01|m1|P|2.5.1\r" +
            "PID|1||P-1||Test^Patient\r" +
            "OBR|1|||VITALS^Vital Signs|||20260626034516\r" +
            "OBX|1|NM|8867-4^Heart rate^LN||NOTANUMBER|/min^^UCUM|||||F\r" +
            "OBX|2|NM|59408-5^SpO2^LN||98|%^^UCUM|||||F";

        var parsed = OruParser.Parse(oru);

        Assert.Single(parsed.Results);
        Assert.Equal("59408-5", parsed.Results[0].Loinc);
        Assert.Equal(98, parsed.Results[0].Value, 3);
    }
}
