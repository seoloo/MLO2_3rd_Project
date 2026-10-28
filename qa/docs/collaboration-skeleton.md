# AI 사례 생성 · 팀 QA 공유 스켈레톤

`qa` 브랜치에서 준비하는 후속 개발용 구조입니다. 현재는 합성 예제와 로컬 공유 서버로 데이터 흐름을 확인합니다. Runpod 호출, AWS 자원 생성·배포, 실제 팀원 간 원격 동기화는 수행하지 않습니다.

## 목표 흐름

```text
Runpod 사례 생성 (후속 구현)
    │ 시험 문자 + 예상 정답 + 생성기/프롬프트 버전
    ▼
데스크톱 앱의 TeamQaWorkflow
    ├─ 문자만 → 서비스 판정 API → 실제 판정
    ├─ 예상 정답과 실제 판정 비교 → 기존 JSON·HTML 보고서
    └─ 시작/완료/오류 스냅샷 → 공용 QA 공유 API → 각 팀원의 데스크톱 화면
                              현재: localhost 예제 / 향후: EC2
```

AI 생성 사례는 판정 시험에만 사용합니다. 파이프라인 집계 정답은 기존 `ExpectedResultCalculator`가 계속 계산합니다. 서비스 API `/checks`와 QA 공유 API `/qa/runs`는 서로 다른 역할입니다.

## 폴더와 교체 경계

| 위치 | 역할 | 현재 상태 |
|---|---|---|
| `src/QaRunner.Contracts/` | 생성 사례·검수 상태·공유 실행·인터페이스 | 공통 DTO와 기본 형식 검증 |
| `src/QaRunner/Collaboration/DemoCaseGenerator.cs` | 합성 사례 제공 | 고정 예제 4건, 실제 AI 아님 |
| `src/QaRunner/Collaboration/RunpodCaseGenerator.cs` | Runpod 호출 경계 | 명시적으로 미지원 예외 발생, 네트워크 호출 없음 |
| `src/QaRunner/Collaboration/TeamQaWorkflow.cs` | 사례 → 기존 판정 채점 → 로컬 저장 → 공유 | 예제 흐름 연결 |
| `src/QaRunner/Collaboration/DemoTeamRunClient.cs` | 앱 내부 결과 보관 | 한 프로세스의 메모리만 사용 |
| `src/QaRunner/Collaboration/HttpTeamRunClient.cs` | 공유 API HTTP 계약 | 루프백 주소만 허용 |
| `src/QaRunner.Sharing.Api/` | 공용 QA 서버 뼈대 | localhost·메모리 저장 전용 |
| `src/QaRunner.Desktop/CollaborationView.xaml(.cs)` | 생성 사례·실행·팀 목록·상세·검수 근거 | 메인 탐색 메뉴에서 공통 콘텐츠 사용 |
| `src/QaRunner.Desktop/CollaborationWindow.xaml(.cs)` | 별도 창 호스트 | 같은 CollaborationView 재사용 |
| `tests/QaRunner.Tests/CollaborationTests.cs` | 라벨 검증·정보 분리·공유 장애·중복 전송 검사 | 기존 테스트와 함께 실행 |

기존 CLI와 WPF 메인 화면의 판정·파이프라인·부하·복구 시험은 그대로 사용합니다. 사례·공유 콘텐츠는 생성 사례의 판정 시험만 시연하며 대상은 `FakeVerdictService`로 고정합니다.

## 지금 실행해 보기

아래 명령은 `qa/` 폴더에서 실행합니다. .NET 10 SDK가 필요합니다.

```powershell
dotnet build --configuration Release
dotnet test --configuration Release --no-build
dotnet run --project src/QaRunner.Desktop
```

1. 메인 화면의 **공유 기록**에서 **실행한 사람과 공유 방법 바꾸기**를 펼칩니다.
2. 실행자·시험 환경·코드 버전을 입력합니다. 코드 버전은 현재 직접 입력하는 값입니다.
3. 기본 공유 방식 **이 앱 안에서만 확인**을 둔 채 **시험 문자 → AI 문자 만들기**에서 **예제 문자 4건 가져오기**를 누릅니다. 시험 문자 목록으로 이동합니다.
4. **예제 시험하고 결과 공유하기**를 누릅니다. 일부 불일치는 결함을 넣은 가짜 판정기의 예상 결과입니다.
5. 팀 실행 기록을 선택해 예상 정답과 실제 판정을 확인합니다.

같은 앱에서는 공유 모드와 실행자를 바꾸고 실행해 기록의 출처를 확인할 수 있습니다. 앱을 별도로 두 번 실행한 경우 앱 내부 메모리는 서로 공유되지 않습니다.

### 로컬 HTTP 계약 확인

별도 터미널에서 서버를 켭니다.

```powershell
dotnet run --project src/QaRunner.Sharing.Api
```

**공유 기록**의 연결 설정에서 공유 방식을 **같은 PC의 공유 서버**로 바꾸고 예제 시험을 실행합니다. 같은 PC에서 다른 앱 인스턴스를 켜 같은 방식을 선택하면 **공유 기록 새로고침**으로 기록을 읽을 수 있습니다.

```powershell
Invoke-RestMethod http://127.0.0.1:5085/health
Invoke-RestMethod http://127.0.0.1:5085/qa/runs
```

현재 서버는 `127.0.0.1:5085`에만 바인딩하며, 서버를 종료하면 서버 메모리의 기록은 사라집니다. 자동 폴링과 실시간 푸시는 아직 없습니다. 목록은 버튼으로 갱신하며 상태는 시험 시작·종료·실행 오류 시점에만 전송합니다.

## 데이터 계약 초안

### 생성 사례

- `GeneratedCaseSet`: `datasetId`, `generator`, `promptVersion`, `cases`.
- 사례: `caseId`, `message`, `expectedVerdict`, `rationale`, `reviewState`.
- 예상 판정: `HIGH_RISK` / `CAUTION` / `NO_SIGNAL` / `UNDECIDED`.
- 검수 상태: `AiSuggested` / `HumanReviewed`. 생성기가 스스로 검수 완료로 승격하지 않습니다.
- 검수 전 라벨이 있으면 `isProvisional`이 true이고, 화면·HTML 보고서·대시보드에 탐색 시험임을 표시합니다.

AI 제안과 시스템 판정이 다르면 검토할 불일치 후보입니다. 팀이 승인한 라벨을 고정한 평가 세트와 AI 탐색 세트는 공식 지표를 만들 때 구분해야 합니다. 검수·승인 UI와 평가 세트 버전 관리는 후속 작업입니다.

### 공유 실행

- `runId`: 여러 PC가 충돌 없이 사용하도록 UUID.
- `actorName`, `environmentName`, `codeVersion`: 실행 출처. 현재 자기 입력값이며 인증된 신원은 아닙니다.
- `state`: `Running` / `Completed` / `Error`. 판정 불일치가 있어도 시험 절차가 끝났으면 `Completed`입니다.
- `startedAt`, `updatedAt`: UTC 시각.
- `targetName`, `caseSet`, `results`, `error`: 재현용 사례·결과와 실행 오류.
- 같은 UUID의 업로드는 upsert하고, 더 오래된 `updatedAt` 스냅샷은 무시합니다. 실제 환경의 시계 오차·상태 전이·동시 수정 정책은 후속 설계 대상입니다.

| 메서드·경로 | 역할 |
|---|---|
| `GET /health` | 로컬 프로토타입·메모리 저장임을 확인 |
| `PUT /qa/runs/{runId}` | 시작·완료·오류 스냅샷 등록·갱신, 성공 204 |
| `GET /qa/runs` | 현재 서버의 실행 목록 |
| `GET /qa/runs/{runId}` | 실행 상세, 없으면 404 |

형식이 잘못되거나 경로와 본문의 실행 ID가 다르면 400을 반환합니다. 공유 API 계약은 `docs/contract.md`의 서비스·파이프라인 계약과 별도로 확정합니다.

### 로컬 보존과 공유 실패

기존 `reports/<UUID>.json`, `.html`, `dashboard.html` 외에 `reports/team-runs/<UUID>.json`을 남깁니다. 이 스냅샷에는 생성 사례·예상 정답·검수 상태·실행자·결과가 들어 있어 재현과 추후 재전송에 사용할 수 있습니다. 파일은 기존 `qa/reports/` 제외 규칙을 따릅니다.

완료 업로드에 실패해도 로컬 보고서와 스냅샷을 보존하고, UI에 **공유 미완료**를 표시합니다. 자동 재전송·업로드 대기열·수동 재전송 버튼은 아직 구현하지 않았습니다. 예제에는 합성 문자만 사용하며, 실제 입력을 공유할 때의 마스킹·보존 정책은 먼저 확정합니다.

## 프로젝트 착수 후 진행 순서

1. **사례 기준 확정:** 두 상황의 판정 기준, 고정 평가 세트, 검수 승인 경로와 모델·프롬프트 버전 관리.
2. **Runpod 어댑터:** 환경 변수 등으로 키 주입, 작업 제출·상태 조회, 응답 파싱·검증, 중복 제거·요청/비용 한도. Runpod 준비 실패는 서비스 판정 실패율과 분리.
3. **실제 판정 연결:** 새 화면에 API 설정을 추가하고 `FakeVerdictService`를 `ApiVerdictService`로 교체. 예상 정답은 서비스로 전송하지 않음.
4. **서버 영구 저장·인증:** `ITeamRunStore` 구현 교체, 실행자 인증·권한, 페이지 조회·보존 정책, HTTPS, 합성/마스킹 데이터 정책.
5. **EC2 배포:** 서버 운영 설정·볼륨·헬스체크·제한된 접근을 준비한 뒤 루프백 전용 서버·클라이언트를 확장. 현재 코드를 그대로 원격에 공개하지 않음.
6. **팀 동기화:** 초기에는 주기적 목록 조회, 필요 시 푸시 방식으로 확장. 진행률·취소·앱 종료 처리·네트워크 단절 후 재전송 구현.
7. **실제 시험 연결:** API 반복·앱 E2E·파이프라인의 동시 실행 격리, 확정 정답과 탐색 결과 분리, 실제 대상 CI 실패 기준 적용.

현재 범위는 위 작업을 이어 붙일 수 있는 스켈레톤입니다. AWS·Runpod을 켜거나 유료 자원을 생성하는 코드는 포함하지 않습니다.
