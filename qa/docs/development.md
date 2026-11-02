# 개발자 안내

[← QA 안내로 돌아가기](../README.md)

## 폴더 구조

```
qa/
├─ QaRunner.slnx                     솔루션 (아래 프로젝트 5개)
├─ run-qa-desktop.cmd                팀원용: 최신 코드 받기 → 빌드 → 데스크탑 앱 켜기 (더블클릭)
├─ publish-qa-desktop.cmd            QA 담당용: Windows x64 배포 파일과 ZIP 만들기 (dist/, Git 제외)
├─ cases/
│   ├─ sessions.json                 판정 시험 세션 사례 (사람·봇 A~D) + 정답
│   └─ pipeline/*.json               파이프라인 시험 시나리오
├─ docs/install-and-use.md           팀원용 설치·사용 안내 (배포 ZIP에도 함께 들어감)
├─ docs/collaboration-skeleton.md    AI 사례·팀 공유 스켈레톤 안내
├─ src/QaRunner/                     콘솔 프로그램 (채점 엔진)
│   ├─ Program.cs                    콘솔 실행 순서 (옵션 → 준비 → 채점 → 출력 → 저장)
│   ├─ QaRunService.cs               콘솔·데스크탑 앱이 함께 쓰는 실행 절차
│   ├─ CommandLineOptions.cs         명령줄 옵션 읽기
│   ├─ SessionCaseLoader.cs          판정 시험 세션 사례 파일 읽기·형식 확인
│   ├─ VerdictChecker.cs             판정 시험 채점기 (묶음 비교)
│   ├─ DetectionSummary.cs           봇 탐지율·사람 오탐률 계산
│   ├─ Models/                       세션 사례·결과·실행 기록·시험 종류·시험 영역
│   ├─ Measurement/                  부하·복구 시험 (전송, Gold 반영 추적, p95, 채점기)
│   ├─ Verdicts/                     판정 대상 (연습용 / 실제 백엔드), 세션 → 이벤트 변환, 판정 코드·묶음
│   ├─ Pipeline/                     이벤트 종류·계약 형식·시나리오·전송 계획·정답 계산기·가짜 파이프라인·Kafka/MongoDB/Docker 연결
│   ├─ Reports/                      JSON·HTML 보고서, 실행 번호, 기록 읽기, 대시보드(영역별로 묶음)
│   ├─ SelfCheck/                    QA 도구 점검 (답을 아는 입력으로 채점기·정답 계산기·보고서 확인)
│   ├─ AppMock/                      앱 영역의 가짜 예매·판정 서버 (응답 방식, 요청 계약 검사, 예매 상태, HTTP 서버, 요청 채점)
│   └─ Collaboration/                예제·Runpod 세션 생성기, 팀 공유 클라이언트, TeamQaWorkflow
├─ src/QaRunner.Contracts/           생성 사례·공유 실행 데이터 형식과 인터페이스
├─ src/QaRunner.Sharing.Api/         로컬 팀 공유 서버 뼈대 (127.0.0.1:5085, 메모리 저장)
├─ src/QaRunner.Desktop/             데스크탑 앱 (WPF)
│   ├─ App.xaml(.cs)                 시작점, 테마 불러오기, 캡처·UI 점검 모드
│   ├─ DesktopTheme.xaml             색·글자·카드·버튼·메뉴 스타일 (디자인 규칙의 기준)
│   ├─ MainWindow.xaml(.cs)          메인 창: 메뉴·홈·시험 결과·기록·설정·조건 시트
│   ├─ GuidedStart.cs                홈 여정 지도·다음 할 일·조건 시트·시험 대상 선택
│   ├─ MockServerPanel.cs            홈 › 앱 카드의 가짜 예매·판정 서버 켜기·끄기·받은 요청 목록
│   ├─ CollaborationView.xaml(.cs)   시험 세션·AI 세션·팀 공유 화면 (별도 창에서도 재사용)
│   ├─ CollaborationWindow.xaml(.cs) 팀 공유 별도 창
│   ├─ HelpWindow.xaml(.cs)          사용 안내 창 (F1)
│   ├─ WindowTitleBar.xaml(.cs)      제목 표시줄과 창 조작 버튼
│   ├─ UsageHelp.cs                  버튼·입력칸 말풍선 설명 모음
│   ├─ AdaptiveWindowLayout.cs       모니터·DPI에 맞춘 창 크기와 본문 자동 배율
│   ├─ DesktopBackgrounds.cs         홈 배너 배경 이미지 무작위 선택
│   ├─ DesktopSmoke.cs               --ui-smoke 화면 점검
│   ├─ ResultRow.cs                  결과 표의 한 줄
│   ├─ QaFolderLocator.cs            qa 폴더를 찾아 기준 폴더로 삼기
│   ├─ Assets/Backgrounds/           배너 배경 이미지 20개 (압축본)
│   ├─ Assets/AppIcon.svg·.ico·.png  앱 아이콘 원본과 크기별 파일 (exe·창·작업 표시줄·왼쪽 메뉴 로고)
│   └─ THIRD-PARTY-NOTICES.txt       외부 소스·이미지 출처와 라이선스
├─ tests/QaRunner.Tests/             QA 러너 자체의 테스트
└─ bots/                            (구현 예정) 매크로 봇 · 가상 사람 트래픽 생성기 (Python · Locust · Playwright). 구조는 매크로 봇 작업 계획 참고
```

Git에 올리지 않는 폴더: `reports/`(실행 보고서), `dist/`(배포 파일), `designs/`(디자인 시안·캡처), `bin/`·`obj/`(빌드 결과).

## 확장 방법

### 계약이 바뀌면 고칠 곳

연결 코드는 [docs/contract.md](../../docs/contract.md) 초안대로 이미 만들어 두었습니다. 팀이 계약을 바꾸면 아래 파일만 고치면 됩니다.

| 계약에서 바뀐 것 | 고칠 파일 |
|---|---|
| 수집 API·판정 조회 API 경로·필드 (4·5장) | `Verdicts/ApiVerdictService.cs` |
| 판정 코드와 묶음 (2장) | `Verdicts/VerdictCodes.cs` |
| 이벤트 필드 (4-1장) | `Pipeline/ContractEventFormat.cs`, `Verdicts/SessionEventBuilder.cs` |
| 이벤트 종류·origin (4-2장) | `Pipeline/EventTypes.cs` |
| 예매 API 경로·필드·결제 응답 (3장) | `AppMock/MockRequestInspector.cs`, `AppMock/MockTicketBackend.cs` |
| 토픽·데이터베이스·Spark 컨테이너 기본 이름 | `Pipeline/PipelineConnectionSettings.cs` |
| 메달리온 컬렉션·필드 이름 (7-2장) | `Pipeline/MongoPipelineReader.cs` |
| 처리 규칙: 중복·격리·구간 (8장) | `Pipeline/ExpectedResultCalculator.cs` |
| 탐지 목표값 (9장) | `DetectionSummary.cs` |

### 처음 실제로 연결할 때 확인할 것

실제 연결 코드는 서버 없이 확인할 수 있는 부분(형식 변환, 옵션, 연결 실패 처리)만 테스트했습니다. 처음 붙일 때는 아래 순서로 확인합니다.

1. B의 백엔드(수집·판정 조회 API)와 Spark 판정 작업을 켜고 판정 시험: `--suite verdict --api-url http://localhost:8000`
2. 시험 환경(infra의 docker-compose)을 띄우고 파이프라인 시험: `--suite pipeline --kafka localhost:9092 --mongo mongodb://localhost:27017`
3. 짧은 부하 시험: `--suite load --duration 30 --kafka ... --mongo ...`
4. 짧은 복구 시험: `--suite recovery --duration 60 --stop-seconds 15 --kafka ... --mongo ...` (docker 명령을 쓸 수 있는 PC에서)
5. 결과가 이상하면 MongoDB 컬렉션을 직접 열어 계약 7-2장의 필드 이름·형식과 같은지 먼저 봅니다.

### 새로운 시험 영역·시험 종류 추가하기

1. `Models/TestSuite.cs`에 시험 종류를 추가하고 `Area()`에서 영역(서비스·앱·QA 도구)을 정합니다. 새 영역이 필요하면 `Models/TestArea.cs`에 추가합니다.
2. `QaRunService.cs`의 `Prepare`에 준비 함수를 추가합니다. 결과는 `TestResult` 목록으로 돌려주면 보고서·대시보드·데스크탑 결과 화면이 그대로 씁니다.
3. 데스크탑 앱은 홈의 영역 카드와 조건 시트의 시험 목록(`MainWindow.xaml`)에 항목을 추가합니다.
4. 사용자가 끌 때까지 도는 시험(가짜 예매·판정 서버처럼)은 `PreparedRun`의 `RequestStop`으로 멈추는 방법을, `Announcement`로 시작할 때 알릴 말을 넘깁니다. 콘솔은 Enter·Ctrl+C에, 데스크탑 앱은 끄기 버튼에 연결합니다.
5. 앱 영역의 다음 시험(앱 계약 시험, 화면 흐름 시험)도 같은 순서로 `TestSuite`에 추가하고 `Area()`를 `TestArea.App`으로 정합니다.

### 새로운 시험 대상 추가하기

판정 시험은 `IVerdictService`, 파이프라인·부하 시험은 `IEventSender`·`IPipelineReader`, 복구 시험은 여기에 `IProcessingControl`까지 구현한 클래스를 추가하고 `QaRunService.cs`의 `Prepare`에서 고르게 합니다. 가짜 대상의 클래스 이름은 `Fake`로 시작해야 보고서와 데스크탑 앱에 가짜 안내가 붙습니다.

### 보고서·화면에 항목 추가하기

1. `Models/TestRun.cs`(실행 전체) 또는 `Models/TestResult.cs`(확인 항목 하나)에 속성을 추가합니다. JSON 보고서에는 자동으로 들어갑니다.
2. HTML 보고서는 `Reports/HtmlReportWriter.cs`의 `BuildHtml`을, 데스크탑 앱은 `MainWindow.xaml`과 `ShowTestRun`을 고칩니다.
3. 데스크탑 화면을 바꾸면 `DesktopSmoke.cs`의 점검도 함께 고치고 `--ui-smoke`가 통과하는지 확인합니다.

### 코드를 고칠 때 지킬 것

- 이름은 무엇인지 바로 알 수 있게 짓습니다. 예: `sessionCase`, `failedCount` (한 글자 변수, 줄임말 쓰지 않기)
- 클래스, 함수, 중요한 변수에는 무엇이고 왜 있는지 한국어 주석을 답니다.
- 동작을 바꾸면 `tests/QaRunner.Tests/`에 테스트를 추가하고 `dotnet test`가 통과하는지 확인합니다.
- Kafka·MongoDB·Spark 연결 코드와 Runpod·EC2 확장 뼈대는 지금 쓰지 않더라도 지우지 않습니다.
- 코드, 옵션, 파일, 화면이 바뀌면 해당 문서(`qa/docs/`, `qa/bots/README.md`)와 `qa/README.md`의 진행 상황도 같은 커밋에서 고칩니다. 방향이 크게 바뀌면 [변경 기록](changes.md)에 한 줄 남깁니다.

## AI 사례·팀 공유 스켈레톤

후속 개발을 위한 뼈대입니다. 데스크탑 앱에서 **예제 세션 → 기존 판정 채점 → 로컬 보고서 → 공유 API → 팀 기록 조회** 흐름을 확인할 수 있습니다. 기획서의 "RunPod로 새 유형 봇 시나리오 생성"이 붙을 자리이며, Runpod 호출, AWS 자원 생성, 실제 팀원 간 원격 동기화는 아직 하지 않습니다.

1. **관리 › 시험 세션**에서 **예제 4개**를 누릅니다. 미리 만든 합성 세션 4개(봇 A, 사람 2, 새 유형 봇)이며 AI가 만든 세션은 아닙니다.
2. **예제 시험하고 공유**를 누르면 연습용 판정기로 채점하고 결과를 공유한 뒤 **기록 › 팀 공유**로 이동합니다.
3. 실행 목록에서 하나를 고르면 사례별 예상 정답과 실제 판정이 나옵니다. 탐지율·오탐률은 로컬 보고서에 남습니다.

공유 방식은 **이 앱 안에서만 확인**(메모리)과 **같은 PC의 공유 서버**(`dotnet run --project src/QaRunner.Sharing.Api`, `127.0.0.1:5085`) 두 가지입니다. 데이터 계약, 공유 API 경로, 후속 작업 순서는 [연결 스켈레톤 안내](collaboration-skeleton.md)에 있습니다.
