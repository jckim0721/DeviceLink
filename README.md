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
| Device Simulator | `src/DeviceLink.Simulator` | 가짜 생체신호(심박·SpO2·체온·혈압)를 주기적으로 TCP 송출 |
| Gateway | `src/DeviceLink.Gateway` | TCP 수신 → 도메인 모델 파싱 → FHIR Observation 매핑 → 서버 POST |
| 공유 모델 | `src/DeviceLink.Core` | Reading 등 도메인 모델 · 표준 코드 매핑 (Simulator/Gateway 공용) |

FHIR 서버는 직접 만들지 않고 공개 테스트 서버(HAPI, `https://hapi.fhir.org/baseR4`)를 사용한다.

## 기술 스택

- **.NET 8 / C#**
- **Firely .NET SDK** (`Hl7.Fhir.R4`) — FHIR Observation POCO·JSON 직렬화 + `FhirClient` REST
- **FHIR 테스트 서버** — HAPI (`https://hapi.fhir.org/baseR4`)
- (스트레치) **NHapi** — HL7 v2 ORU^R01 / **Open Integration Engine** — MLLP 채널

## 실행

```bash
# macOS Homebrew에서 .NET 8은 keg-only — 최초 1회 PATH 설정
export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec"
export PATH="/opt/homebrew/opt/dotnet@8/bin:$PATH"

dotnet build                                   # 전체 솔루션 빌드
dotnet run --project src/DeviceLink.Simulator  # 시뮬레이터
dotnet run --project src/DeviceLink.Gateway    # 게이트웨이
```

## 상태

🚧 **v0 진행중** — 솔루션 스켈레톤 단계. 전체 파이프라인 1종(장치→게이트웨이→FHIR)이 흐르면 v0.

- [ ] 장치 → 게이트웨이 → FHIR 서버로 Observation 1종 이상 흐름
- [ ] 서버에서 되조회 확인
- [ ] README에 아키텍처·실행법·배운 점
- [ ] 공개 레포 push

## 배운 점 / 다음 계획

_v0 완성 후 채움._
