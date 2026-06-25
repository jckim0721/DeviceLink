# CLAUDE.md — DeviceLink

의료기기 → FHIR 게이트웨이. 장치 생체신호를 TCP로 수신 → 도메인 모델 파싱 → FHIR Observation 변환 → FHIR 서버(HAPI)에 POST.

## 단일 진실원 (스펙·트래커)

코드 스펙과 진척은 **Obsidian 볼트 노트가 기준**이다 (이 레포 밖, 사용자 볼트 `1_Projects/Programming/DeviceLink/`):

- `통합프로젝트_DeviceLink` — 상세 스펙: 아키텍처·스택·2주 일정·Definition of Done
- `DeviceLink_작업계획서` — 실행 트래커: 마일스톤 체크박스·작업 로그

작업 시작 시 위 두 노트를 컨텍스트로 읽고 시작한다. 끝나면 트래커에 체크 + 로그 한 줄.

## 구조

```
DeviceLink.sln
└─ src/
   ├─ DeviceLink.Core/       # 공유 도메인 모델(Reading)·표준 코드 매핑. 현재 비어 있음 (M1에서 채움)
   ├─ DeviceLink.Simulator/  # 생체신호 TCP 송출 콘솔
   └─ DeviceLink.Gateway/    # TCP 수신 → 파싱 → FHIR 변환 → POST
```

Simulator·Gateway 모두 `DeviceLink.Core`를 참조.

## 빌드/실행 (macOS, .NET 8 keg-only)

.NET 8이 Homebrew keg-only라 PATH를 잡아야 한다:

```bash
export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec"
export PATH="/opt/homebrew/opt/dotnet@8/bin:$PATH"

dotnet build
dotnet run --project src/DeviceLink.Simulator
dotnet run --project src/DeviceLink.Gateway
```

## 스택

.NET 8 / C# · Firely SDK (`Hl7.Fhir.R4`, FHIR R4) · HAPI 테스트 서버(`https://hapi.fhir.org/baseR4`).
스트레치: NHapi(HL7 v2 ORU^R01), Open Integration Engine(MLLP).
생체신호는 LOINC 코드 + UCUM 단위로 매핑 (정확한 코드는 loinc.org 확인).

## 작업 위임 원칙

- **위임 OK**: 보일러플레이트(소켓 서버, DTO, 직렬화), Firely API 사용법 탐색, 에러 핸들링 패턴, README 초안.
- **사람이 쥔다**: 아키텍처 결정, 프로토콜/파싱 설계, "왜 이렇게" 판단 — 면접에서 증명하는 부분이라 위임하지 않는다.
- 태스크는 작게 던진다. v0 정신: 못생겨도 *돌아가면 통과*, 다듬기는 나중.
