# DeviceLink

> 의료기기 → FHIR 게이트웨이. 장치에서 나온 생체신호를 TCP로 수신해 HL7 **FHIR** 표준으로 변환하고 임상시스템(FHIR 서버)에 전송한다.

장치 통신(TCP/UART/CAN) 경험을 의료 상호운용 표준(HL7/FHIR)과 연결하는 2주 타임박스 포트폴리오 프로젝트.

## 아키텍처

```
[Device Simulator] --TCP--> [Gateway] --FHIR REST--> [FHIR Server]
   생체신호 송출            수신·파싱·변환            HAPI 테스트 서버
```

| 구성요소 | 프로젝트 | 역할 |
|---|---|---|
| Device Simulator | `src/DeviceLink.Simulator` | 가짜 생체신호를 주기적으로 TCP 송출 (현재 심박 1종) |
| Gateway | `src/DeviceLink.Gateway` | TCP 수신 → 도메인 모델 파싱 → FHIR Observation 매핑 → 서버 POST |
| 공유 모델 | `src/DeviceLink.Core` | `Reading` 도메인 모델 · 와이어 포맷 · 표준 코드 매핑 (Simulator/Gateway 공용, FHIR 비의존) |

FHIR 서버는 직접 만들지 않고 공개 테스트 서버(HAPI, `https://hapi.fhir.org/baseR4`)를 사용한다.

### 데이터 흐름

장치 메시지는 **파이프 구분 한 줄 = 한 측정**, 줄바꿈(`\n`)으로 프레이밍한다:

```
metric|timestamp(ISO8601 UTC)|deviceId|value|unit
예) HR|2026-06-25T16:52:44Z|DEV-001|66|/min
```

Gateway가 이를 `Reading`으로 파싱 → LOINC/UCUM 코드를 붙여 FHIR `Observation`으로 매핑 → HAPI에 POST하고 서버가 부여한 id를 회수한다:

```
[수신] HR 66/min @ 2026-06-25T16:52:44Z (장치 DEV-001)
[POST] Observation/137032271 (version 1) ← HR 66/min
```

### 생체신호 ↔ 표준 코드 매핑 (`Core/MetricCatalog`)

| metric | LOINC | 표시명 | UCUM 단위 |
|---|---|---|---|
| `HR` | 8867-4 | Heart rate | `/min` |
| `TEMP` | 8310-5 | Body temperature | `Cel` |
| `SpO2` | 59408-5 | Oxygen saturation by Pulse oximetry | `%` |

> 혈압(NIBP)은 systolic(8480-6)/diastolic(8462-4) component 구조라 별도 매핑이 필요 — M2 예정.

## 기술 스택

- **.NET 8 / C#**
- **Firely .NET SDK** (`Hl7.Fhir.R4` 6.2.0) — FHIR Observation POCO · System.Text.Json 직렬화 + `FhirClient` REST
- **FHIR 테스트 서버** — HAPI (`https://hapi.fhir.org/baseR4`)
- (스트레치) **NHapi** — HL7 v2 ORU^R01 / **Open Integration Engine** — MLLP 채널

## 실행

```bash
# macOS Homebrew에서 .NET 8은 keg-only — 최초 1회 PATH 설정
export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec"
export PATH="/opt/homebrew/opt/dotnet@8/bin:$PATH"

dotnet build
```

게이트웨이가 리스너이므로 **먼저 띄우고**, 시뮬레이터(클라이언트)를 나중에 붙인다. 터미널 2개:

```bash
# 터미널 1 — Gateway: 수신 → 파싱 → 변환 → HAPI POST
dotnet run --project src/DeviceLink.Gateway
#   인자: [bindHost] [port] [fhirBaseUrl]   기본 0.0.0.0 5000 https://hapi.fhir.org/baseR4
#   fhirBaseUrl 에 "-" 를 주면 POST를 건너뛰고 Observation JSON만 출력(오프라인 데모)

# 터미널 2 — Simulator: 심박을 N초마다 TCP 송출
dotnet run --project src/DeviceLink.Simulator
#   인자: [host] [port] [intervalSeconds]   기본 127.0.0.1 5000 1.0
```

POST된 리소스는 회수한 id로 되조회할 수 있다:

```bash
curl -H "Accept: application/fhir+json" https://hapi.fhir.org/baseR4/Observation/<id>
```

## 상태

✅ **v0 완성** — 장치 → 게이트웨이 → FHIR 서버로 생체신호 1종(심박)이 종단으로 흐른다.

- [x] 장치 → 게이트웨이 → FHIR 서버로 Observation 1종 이상 흐름
- [x] 서버에서 되조회 확인
- [x] README에 아키텍처·실행법·배운 점
- [x] 공개 레포 push

견고성: 파싱 실패 메시지와 POST 실패는 **건별로 격리**해 연결을 유지하고, 끊기면 시뮬레이터가 재접속한다.

## 배운 점

- **TCP는 메시지가 아니라 바이트 스트림** — 한 번의 read가 메시지 경계와 일치하지 않는다. `\n` 프레이밍을 정하고 `StreamReader.ReadLineAsync`로 라인 버퍼링을 위임해 부분 수신/CRLF를 흡수했다.
- **의존성 경계 설계** — 와이어 계약(`Reading`)과 표준 코드표(`MetricCatalog`)는 무거운 FHIR SDK에 묶이면 안 돼서 `Core`를 FHIR-비의존으로 두고, Firely 매핑은 Gateway에만 격리했다. 추후 HL7 v2 같은 다른 출력 포맷을 붙일 때 Core가 흔들리지 않는다.
- **Firely 6.x API 이행** — 구 `FhirJsonSerializer`/`SerializerSettings`가 폐기되고 System.Text.Json 기반 `JsonSerializerOptions().ForFhir(...).Pretty()`로 옮겨졌다. 또 `Hl7.Fhir.Model.Task`가 `System.Threading.Tasks.Task`와 충돌해 정규명으로 우회해야 했다.
- **FHIR 코드 표현** — LOINC 코드는 `coding`에, 사람이 읽는 표시명은 `coding.display`와 `text` 양쪽에 실어야 코드가 표시명과 함께 전달된다.

## 다음 계획

- **M2 — 단단하게**: 생체신호 4종 전부(SpO2·체온·혈압) + 혈압 component 매핑 + `subject`(Patient 참조) + 재접속/로깅 견고화
- **M3 — 상호운용(스트레치)**: NHapi로 HL7 v2 ORU^R01 출력 경로, Open Integration Engine MLLP 채널로 v2 수신·변환
- 자동화 테스트(파싱/매핑 단위 테스트), 구조적 로깅
