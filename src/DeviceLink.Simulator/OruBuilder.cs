using System.Globalization;
using DeviceLink.Core;
using NHapi.Base.Parser;
using NHapi.Model.V251.Datatype;
using NHapi.Model.V251.Message;

namespace DeviceLink.Simulator;

/// <summary>
/// 측정 한 묶음(같은 장치·환자·시각)을 HL7 v2.5.1 ORU^R01 메시지로 만든다.
/// 실제 병상 모니터처럼 "값 1개 = OBX 1개". 혈압도 수축기/이완기/MAP를 각각 OBX로 보낸다
/// (FHIR panel+component로 묶는 건 수신측 게이트웨이 책임).
/// LOINC/단위는 Core.MetricCatalog에서 끌어온다.
/// </summary>
public static class OruBuilder
{
    private static readonly PipeParser Parser = new();

    /// <summary>readings 한 묶음 → ER7(파이프 구분 HL7) 문자열. 세그먼트 구분은 \r.</summary>
    public static string BuildEr7(string deviceId, string patientId, DateTimeOffset ts, IReadOnlyList<Reading> readings)
    {
        var oru = new ORU_R01();
        string hl7Ts = ts.ToUniversalTime().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);

        // --- MSH ---
        var msh = oru.MSH;
        msh.FieldSeparator.Value = "|";
        msh.EncodingCharacters.Value = @"^~\&";
        msh.SendingApplication.NamespaceID.Value = "DeviceLinkSim";
        msh.SendingFacility.NamespaceID.Value = "ICU";
        msh.ReceivingApplication.NamespaceID.Value = "DeviceLinkGW";
        msh.ReceivingFacility.NamespaceID.Value = "HOSP";
        msh.DateTimeOfMessage.Time.Value = hl7Ts;
        msh.MessageType.MessageCode.Value = "ORU";
        msh.MessageType.TriggerEvent.Value = "R01";
        msh.MessageType.MessageStructure.Value = "ORU_R01";
        msh.MessageControlID.Value = $"{deviceId}-{hl7Ts}";
        msh.ProcessingID.ProcessingID.Value = "P";
        msh.VersionID.VersionID.Value = "2.5.1";

        // --- PID (환자) ---
        var pid = oru.GetPATIENT_RESULT(0).PATIENT.PID;
        pid.SetIDPID.Value = "1";
        pid.GetPatientIdentifierList(0).IDNumber.Value = patientId;
        pid.GetPatientName(0).FamilyName.Surname.Value = "DeviceLink";
        pid.GetPatientName(0).GivenName.Value = "Test";

        // --- OBR (측정 오더/묶음 헤더) ---
        var order = oru.GetPATIENT_RESULT(0).GetORDER_OBSERVATION(0);
        var obr = order.OBR;
        obr.SetIDOBR.Value = "1";
        obr.UniversalServiceIdentifier.Identifier.Value = "VITALS";
        obr.UniversalServiceIdentifier.Text.Value = "Vital Signs";
        obr.ObservationDateTime.Time.Value = hl7Ts;

        // --- OBX (측정 결과, 값마다 하나) ---
        for (int i = 0; i < readings.Count; i++)
        {
            var r = readings[i];
            if (!MetricCatalog.TryGet(r.Metric, out var info) || info is null) continue;

            var obx = order.GetOBSERVATION(i).OBX;
            obx.SetIDOBX.Value = (i + 1).ToString(CultureInfo.InvariantCulture);
            obx.ValueType.Value = "NM";   // numeric
            obx.ObservationIdentifier.Identifier.Value = info.Loinc;
            obx.ObservationIdentifier.Text.Value = info.Display;
            obx.ObservationIdentifier.NameOfCodingSystem.Value = "LN";   // LOINC
            var nm = new NM(oru) { Value = r.Value.ToString(CultureInfo.InvariantCulture) };
            obx.GetObservationValue(0).Data = nm;
            obx.Units.Identifier.Value = info.UcumUnit;
            obx.Units.NameOfCodingSystem.Value = "UCUM";
            obx.ObservationResultStatus.Value = "F";   // Final
            obx.DateTimeOfTheObservation.Time.Value = hl7Ts;
            // OBX-18: 측정한 장치 식별자(HL7 표준 위치). 수신측이 Observation.device로 회수.
            obx.GetEquipmentInstanceIdentifier(0).EntityIdentifier.Value = deviceId;
        }

        return Parser.Encode(oru);
    }
}
