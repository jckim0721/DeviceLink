# DeviceLink

> 의료기기 → FHIR 게이트웨이. 장치에서 나온 생체신호를 TCP로 수신해 HL7 **FHIR** 표준으로 변환하고 임상시스템(FHIR 서버)에 전송한다.

장치 통신(TCP/UART/CAN) 경험을 의료 상호운용 표준(HL7/FHIR)과 연결하는 2주 타임박스 포트폴리오 프로젝트.

## 아키텍처

```
[Device Simulator] --HL7 v2/MLLP--> [Gateway] --FHIR REST--> [FHIR Server]
   ORU^R01 송출                  수신·파싱·변환          HAPI 테스트 서버
```

| 구성요소 | 프로젝트 | 역할 |
|---|---|---|
| Device Simulator | `src/DeviceLink.Simulator` | 생체신호 4종을 HL7 v2 ORU^R01로 만들어 MLLP로 송출 |
| Gateway | `src/DeviceLink.Gateway` | MLLP 수신 → NHapi로 ORU^R01 파싱 → FHIR Observation 매핑 → 서버 POST |
| 공유 모델 | `src/DeviceLink.Core` | `Reading` 도메인 모델 · 표준 코드 매핑(`MetricCatalog`) (HL7/FHIR SDK 비의존) |

FHIR 서버는 직접 만들지 않고 공개 테스트 서버(HAPI, `https://hapi.fhir.org/baseR4`)를 사용한다.

### 데이터 흐름

시뮬레이터는 실제 병상 모니터처럼 **HL7 v2.5.1 ORU^R01** 메시지를 만들어 **MLLP**(`<VT>…<FS><CR>`) 프레이밍으로 TCP 송출한다. 측정값 하나 = OBX 세그먼트 하나이며, 같은 측정 묶음은 OBR 아래 모인다:

```
MSH|^~\&|DeviceLinkSim|ICU|DeviceLinkGW|HOSP|20260626034206||ORU^R01^ORU_R01|...|P|2.5.1
PID|1||P-001||DeviceLink^Test
OBR|1|||VITALS^Vital Signs|||20260626034206
OBX|1|NM|8867-4^Heart rate^LN||72|/min^^UCUM|||||F|...|DEV-001
OBX|3|NM|8480-6^Systolic blood pressure^LN||120|mm[Hg]^^UCUM|||||F|...
OBX|4|NM|8462-4^Diastolic blood pressure^LN||80|mm[Hg]^^UCUM|||||F|...
```

Gateway는 MLLP 프레임을 NHapi로 파싱 → OBX들을 묶음 단위로 모아 FHIR로 매핑한다. 스칼라(HR·SpO2·체온)는 각각 Observation, **혈압 OBX 3종(수축기/이완기/평균)은 FHIR Blood pressure panel(85354-9) 하나에 component로 합친다.** PID의 환자는 FHIR Patient로 보장해 `subject`로 참조하고, OBX-18 장치 식별자는 `Observation.device`로 옮긴다. 그 뒤 HAPI에 POST하고 부여 id를 회수한다:

```
[수신] ORU^R01 환자 P-001, 측정 6건
[환자 생성] Patient/137032796 (P-001)
   [POST] Observation/137032797 ← 8867-4 Heart rate
   [POST] Observation/137032812 ← 85354-9 Blood pressure panel
```

### 생체신호 ↔ 표준 코드 매핑 (`Core/MetricCatalog`)

| metric | LOINC | 표시명 | UCUM 단위 |
|---|---|---|---|
| `HR` | 8867-4 | Heart rate | `/min` |
| `TEMP` | 8310-5 | Body temperature | `Cel` |
| `SpO2` | 59408-5 | Oxygen saturation by Pulse oximetry | `%` |
| `NIBPs` | 8480-6 | Systolic blood pressure | `mm[Hg]` |
| `NIBPd` | 8462-4 | Diastolic blood pressure | `mm[Hg]` |
| `NIBPm` | 8478-0 | Mean blood pressure | `mm[Hg]` |

> 혈압 3종은 HL7에선 각각 OBX로 흐르고, FHIR에선 Blood pressure panel(85354-9) 하나의 component로 합쳐진다.

## 기술 스택

- **.NET 8 / C#**
- **NHapi** (`nhapi` 3.2.4) — HL7 v2.5.1 ORU^R01 생성/파싱 + MLLP 프레이밍
- **Firely .NET SDK** (`Hl7.Fhir.R4` 6.2.0) — FHIR Observation/Patient POCO · System.Text.Json 직렬화 + `FhirClient` REST
- **FHIR 테스트 서버** — HAPI (`https://hapi.fhir.org/baseR4`)
- (스트레치) **Open Integration Engine** — MLLP 인터페이스 엔진 채널

## 실행

```bash
# macOS Homebrew에서 .NET 8은 keg-only — 최초 1회 PATH 설정
export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec"
export PATH="/opt/homebrew/opt/dotnet@8/bin:$PATH"

dotnet build
```

게이트웨이가 리스너이므로 **먼저 띄우고**, 시뮬레이터(클라이언트)를 나중에 붙인다. 터미널 2개:

```bash
# 터미널 1 — Gateway: MLLP 수신 → NHapi 파싱 → FHIR 변환 → HAPI POST
dotnet run --project src/DeviceLink.Gateway
#   인자: [bindHost] [port] [fhirBaseUrl]   기본 0.0.0.0 5000 https://hapi.fhir.org/baseR4
#   fhirBaseUrl 에 "-" 를 주면 POST를 건너뛰고 매핑 결과만 로그(오프라인 데모)

# 터미널 2 — Simulator: ORU^R01(4종 생체신호)을 N초마다 MLLP로 송출
dotnet run --project src/DeviceLink.Simulator
#   인자: [host] [port] [intervalSeconds]   기본 127.0.0.1 5000 1.0
```

POST된 리소스는 회수한 id로 되조회할 수 있다:

```bash
curl -H "Accept: application/fhir+json" https://hapi.fhir.org/baseR4/Observation/<id>
```

## 상태

✅ **v0 완성** — 장치 → 게이트웨이 → FHIR로 종단 흐름.
✅ **M2 + HL7 v2 경로** — 실제 HL7 v2 ORU^R01(MLLP) 수신, 4종 생체신호 + 혈압 panel + Patient 참조.

- [x] 장치 → 게이트웨이 → FHIR 서버로 Observation 흐름 + 서버 되조회 확인
- [x] HL7 v2.5.1 ORU^R01 / MLLP 송수신 (NHapi)
- [x] 4종 생체신호(심박·SpO2·체온·혈압) + LOINC/UCUM + 혈압 panel(85354-9) component
- [x] PID → FHIR Patient `subject` 참조, OBX-18 → `Observation.device`
- [x] 견고화: 재접속 / 잘못된·미지원 메시지 건별 격리 / 단계별 로깅

견고성: 파싱 실패·미지원 메시지(비-ORU)·POST 실패는 **건별로 격리**해 연결을 유지하고, 끊기면 시뮬레이터가 재접속한다.

## 배운 점

- **장치는 OBX를 분리해 보내고 FHIR는 panel을 요구한다** — HL7 v2에서 혈압은 수축기/이완기/평균이 각각 OBX로 흐르지만, FHIR vital-signs 규약은 하나의 Blood pressure panel(85354-9)에 component로 묶도록 요구한다. 게이트웨이에서 OBR 묶음 단위로 OBX를 모아 합치는 게 인터페이스 엔진이 실제로 하는 일.
- **TCP는 메시지가 아니라 바이트 스트림** — MLLP(`<VT>…<FS><CR>`) 경계를 내부 버퍼에 누적하며 직접 찾아야 한 read에 섞여 온 여러/부분 프레임을 정확히 잘라낼 수 있다.
- **의존성 경계 설계** — 표준 코드표(`MetricCatalog`)와 도메인(`Reading`)을 둔 `Core`는 NHapi도 Firely도 참조하지 않는다. HL7 파싱은 Gateway, HL7 생성은 Simulator, FHIR 매핑은 Gateway로 격리.
- **Firely 6.x API 이행** — 직렬화가 System.Text.Json 기반 `JsonSerializerOptions().ForFhir(...)`로 옮겨졌고, `Hl7.Fhir.Model.Task`가 `System.Threading.Tasks.Task`와 충돌해 정규명으로 우회했다.

## 다음 계획

- **▶ 다음 업데이트 — 인터페이스 엔진(OIE)**: 지금은 게이트웨이를 손코딩했지만 실무는 Mirth/OIE 같은 엔진으로 한다. **Open Integration Engine에 MLLP 채널 1개**(MLLP 리스너 → 변환 → 목적지)를 구성해 "엔진도 만져봤다"를 추가한다. *(직접 짠 게이트웨이 = 원리 이해 / 엔진 채널 = 실무 도구 — 둘 다 보여주는 게 목표)*
- 처리량: 측정 set을 FHIR **transaction Bundle** 한 번으로 POST(현재는 OBX별 순차 POST가 서버 왕복 병목)
- 자동화 테스트(ORU 파싱 / FHIR 매핑 단위 테스트), 구조적 로깅

> 개발 과정·배운 점 상세는 [docs/개발노트.md](docs/개발노트.md).
