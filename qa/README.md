# 정보지킴 자동화 QA 러너

입력값과 정답을 파일에 적어 두면, 프로그램이 입력값을 시험 대상에 넣고 돌아온 결과를 정답과 비교해 채점하는 C# 콘솔 프로그램입니다. 실행할 때마다 결과를 보고서 파일로 남깁니다.

시험은 네 종류입니다.

| 시험 | 넣는 것 | 비교하는 것 |
|---|---|---|
| 판정 시험 (`verdict`) | 문자 | 판정 (고위험·주의·신호 미발견·판단 보류) |
| 파이프라인 시험 (`pipeline`) | 이벤트 + 중복·역순·지연 조건 | Bronze 보관 건수, Silver 고유 건수, 격리 건수, Gold 60초 구간 집계 |
| 부하 시험 (`load`) | 판정 API 동시 요청 + 이벤트를 초당 N건씩 계속 | API 응답 시간 p95, API 오류율, Gold 반영 시간 p95, 적체 |
| 복구 시험 (`recovery`) | 이벤트를 계속 보내면서 처리를 멈췄다 재가동 | 재가동 후 회복 시간, 최종 집계 일치 |

## 진행 상황

| 단계 | 내용 | 상태 |
|---|---|---|
| ① 스켈레톤 | 입력값 파일 → 가짜 판정 대상 → 채점 → 콘솔 출력·종료 코드 | 완료 |
| ② 결과 저장·HTML 보고서 | 실행 번호별 JSON·HTML 보고서 저장 | 완료 |
| ③ 실제 API 연결 | FastAPI 호출 코드 완료. B의 API가 나오면 주소·형식만 맞춰 연결 | 연결 대기 |
| ④ 파이프라인 시험 | 시나리오·정답 계산기·채점기 완료, 가짜 파이프라인으로 동작 확인. Kafka·MongoDB 연결 코드는 C의 파이프라인 계약 확정 후 | 진행 중 |
| ⑤ 부하·복구 시험 | 측정 코드 완료, 처리 시간을 흉내 내는 가짜 파이프라인으로 동작 확인. 실제 Spark 중단·재가동은 인프라가 생긴 뒤 | 진행 중 |
| ⑥ 자동 실행·CI | GitHub Actions에서 푸시마다 빌드·테스트·시험 실행 완료. docker-compose 서비스는 인프라가 생긴 뒤 | 일부 완료 |
| ⑦ 실행 기록 대시보드 | 실행할 때마다 `reports/dashboard.html`을 다시 만들어 통과율·p95·회복 시간 추이를 표시 | 완료 |
| 시연용 데스크탑 앱 | 버튼으로 시험을 돌리고 결과를 화면에서 바로 보는 Windows 앱 | 완료 |

## 팀원이 쓰는 방법

실행 파일을 주고받지 않습니다. 용도에 따라 두 가지 중 하나를 씁니다.

| 하고 싶은 것 | 방법 | 필요한 것 |
|---|---|---|
| 결과만 보기 | 웹 대시보드 **https://seoloo.github.io/MLO2_3rd_Project/** 열기 | 없음 (브라우저만) |
| 직접 시험 돌리기 (예: 내 API 시험) | `qa/run-qa-desktop.cmd` 더블클릭 | 처음 한 번: .NET 10 SDK 설치, 저장소 clone |

### 결과만 보기: 웹 대시보드

- 기본 브랜치에 푸시할 때마다 GitHub Actions가 네 가지 시험을 돌리고, 실행 기록을 `qa-history` 브랜치에 쌓은 뒤, 지금까지의 기록으로 만든 대시보드를 GitHub Pages에 올립니다.
- 대시보드의 실행 번호를 누르면 그 실행의 상세 보고서가 열립니다.
- 웹 대시보드는 CI에서 돌린 기록만 보여 줍니다. 각자 PC에서 돌린 기록은 각자의 `qa/reports/dashboard.html`에 쌓입니다.

### 직접 돌리기: run-qa-desktop.cmd

처음 한 번만 .NET 10 SDK를 설치하고 저장소를 받습니다.

```bash
winget install Microsoft.DotNet.SDK.10
```

```bash
git clone https://github.com/seoloo/MLO2_3rd_Project.git
```

그다음부터는 탐색기에서 `qa/run-qa-desktop.cmd`를 **더블클릭**합니다. 매번 아래 순서로 동작하므로 항상 최신 버전이 켜집니다.

1. 최신 코드 받기 (`git pull`). 고치던 파일이 있어서 받지 못하면 안내만 하고 지금 코드로 계속합니다.
2. 데스크탑 앱 빌드 (처음에는 1~2분, 그다음부터는 몇 초)
3. 데스크탑 앱 켜기

.NET SDK가 없으면 설치 명령을 안내하고 멈춥니다. 빌드에 실패하면 오류 내용을 보여 주고 멈추므로, 그 내용을 QA 담당에게 알려 주면 됩니다.

자기 API를 시험하려면 앱에서 **판정 시험** 또는 **부하 시험**을 고르고, **판정 API 주소** 칸에 주소(예: `http://localhost:8000`)를 넣고 실행합니다.

## 흐름

### 판정 시험

```
입력값 파일 ──(문자)──▶ 판정 대상 ──(실제 판정)──▶ 채점 ──▶ 결과 (콘솔 · 보고서 · 종료 코드)
     └──────────────(정답)─────────────────────────┘
```

### 파이프라인 시험

```
시나리오 ──▶ 전송 계획 ──(이벤트)──▶ 파이프라인 ──(실제 건수)──▶ 채점 ──▶ 결과
               └──▶ 정답 계산기 ──(정답 건수)───────────────────┘
```

### 부하·복구 시험

```
일정한 속도로 보내기 ──(이벤트)──▶ 파이프라인 ──▶ Gold
        │                                          │ 0.5초마다 건수 확인
        └──(보낸 시각)──▶ 반영 추적기 ◀──(늘어난 건수)─┘ → 반영 시간 p95, 회복 시간
```

- 정답은 시험 대상을 거치지 않습니다. 판정 시험은 사람이 적어 둔 정답을, 파이프라인 시험은 정답 계산기가 "보낸 기록"만 보고 계산한 정답을 씁니다. 시스템이 틀려도 정답은 오염되지 않습니다.
- 시험 대상은 바꿔 끼울 수 있습니다. 지금은 가짜(`FakeVerdictService`, `FakePipeline`, `FakeStreamingPipeline`)로 돌고, 실제 API·파이프라인이 준비되면 그 부분만 바꿉니다.

## 빠른 시작

### 1. .NET 10 SDK 설치 (처음 한 번)

```bash
winget install Microsoft.DotNet.SDK.10
```

설치 후 터미널을 새로 열고 버전을 확인합니다. `10.0.`으로 시작하면 됩니다.

```bash
dotnet --version
```

### 2. 실행

명령은 모두 `qa/` 폴더에서 실행합니다.

판정 시험:

```bash
dotnet run --project src/QaRunner
```

파이프라인 시험:

```bash
dotnet run --project src/QaRunner -- --suite pipeline
```

지금은 둘 다 일부러 엉성하게 만든 가짜 대상으로 돌기 때문에 실패가 나옵니다. 채점기가 틀린 결과를 잡아내는지 확인하려는 것입니다.

- 판정 시험: 가짜 판정 대상이 "인증번호"만 보고 판정해서 4건 중 2건 실패
- 파이프라인 시험: 가짜 파이프라인이 격리를 하지 않아서 5분 초과 도착·같은 ID 상충 관련 4건 실패

부하·복구 시험은 실행안 기준으로 기본 5분(부하), 2분(복구)이 걸립니다. 짧게 확인하려면 시간을 줄입니다.

```bash
dotnet run --project src/QaRunner -- --suite load --duration 10
```

```bash
dotnet run --project src/QaRunner -- --suite recovery --duration 20 --stop-seconds 5
```

### 3. 테스트

QA 러너 자체가 올바르게 동작하는지 확인합니다.

```bash
dotnet test
```

## 데스크탑 앱 (시연용)

명령어 대신 화면에서 시험을 고르고 버튼으로 돌려 볼 수 있는 Windows 앱입니다. 콘솔 프로그램과 같은 실행 절차(`QaRunService`)를 쓰므로 결과와 보고서도 똑같습니다.

```bash
dotnet run --project src/QaRunner.Desktop
```

| 화면 | 하는 일 |
|---|---|
| 왼쪽 | 시험 종류(판정·파이프라인·부하·복구), 입력 경로, 판정 API 주소, 측정 설정(부하·복구), 보고서 폴더를 고르고 **실행** |
| 오른쪽 위 | 실행 번호·시험 대상, 가짜 대상 안내, 전체·통과·실패·통과율 |
| 오른쪽 표 | 확인 항목별 결과. 실패한 항목이 위에, 붉은 줄로 표시 |
| HTML 보고서 열기 | 방금 저장한 보고서를 브라우저로 열기 |
| 실행 기록 대시보드 열기 | 지금까지의 실행 기록을 모은 대시보드를 브라우저로 열기 |

- 입력 경로와 보고서 폴더는 `qa/` 폴더 기준입니다. 실행 파일을 어디서 열어도 `qa/` 폴더를 찾아 기준으로 삼습니다.
- 부하·복구 시험은 화면에서 바로 보여 주기 좋게 기본 시간을 짧게 잡았습니다(부하 30초, 복구 60초·15초 중단). 칸의 숫자를 바꾸면 실행안 기준으로도 돌릴 수 있습니다.
- Windows 전용입니다(WPF). 맥·리눅스에서는 콘솔 프로그램을 쓰면 됩니다.

README·발표 자료에 넣을 화면이 필요하면 캡처 모드로 실행합니다. 창을 화면 밖에 띄워 시험을 한 번 돌린 뒤, 화면을 PNG로 저장하고 바로 꺼집니다.

```bash
dotnet run --project src/QaRunner.Desktop -- --capture screen.png --suite load
```

## 자동 검사 (GitHub Actions)

`qa/` 폴더가 바뀐 커밋을 `main`이나 `feature/...` 브랜치에 푸시하면, GitHub가 자동으로 아래 순서대로 검사합니다. 설정 파일은 저장소 맨 위의 `.github/workflows/qa.yml`입니다.

| 순서 | 하는 일 | 실패로 보는 경우 |
|---|---|---|
| 1 | `qa-history` 브랜치에서 지난 실행 기록 가져오기 (없으면 건너뜀) | — |
| 2 | .NET 10 설치 후 빌드 (Windows 환경, 데스크탑 앱 포함) | 빌드 오류 |
| 3 | `dotnet test` | 테스트가 하나라도 실패 |
| 4 | 판정·파이프라인 시험, 부하 시험(10초 축소판), 복구 시험(20초 축소판) 실행 | 채점을 시작하지 못함 (종료 코드 2) |
| 5 | **기본 브랜치일 때만:** 실행 기록을 `qa-history` 브랜치에 저장 | 푸시 실패 |
| 6 | 보고서를 결과물로 첨부 | — |
| 7 | **기본 브랜치일 때만:** 대시보드를 GitHub Pages에 공개 | Pages 설정이 안 되어 있음 |

- 결과는 GitHub 저장소의 **Actions** 탭, 또는 커밋 옆의 ✔(통과)·✘(실패)에서 볼 수 있습니다.
- 보고서는 해당 실행 화면 아래 **Artifacts**의 `qa-reports`를 내려받아 HTML을 열 수도 있습니다. 그 안의 `dashboard.html`에는 지난 CI 기록까지 들어 있습니다.
- **`qa-history` 브랜치**는 CI가 실행 기록(JSON·HTML 보고서)만 쌓아 두는 별도 브랜치입니다. 코드와 섞이지 않으며, 사람이 직접 고치지 않습니다.
- **처음 한 번:** 웹 대시보드를 공개하려면 저장소 **Settings → Pages → Build and deployment → Source**를 **GitHub Actions**로 바꿔야 합니다. 바꾸기 전에는 7번 단계가 실패로 표시됩니다.
- 보고서·대시보드의 시각은 한국 시간으로 맞춰서 기록합니다.
- 지금은 가짜 시험 대상이라 시험 실패(종료 코드 1)가 나오는 것이 정상이므로 검사 실패로 치지 않습니다. 실제 API·파이프라인을 연결하면 `qa.yml`에서 종료 코드 1도 실패로 바꿉니다.
- Actions 탭에서 **Run workflow** 버튼으로 직접 돌릴 수도 있습니다.
- 공개 저장소라서 GitHub Actions 사용 시간은 차감되지 않습니다.

## 옵션

옵션은 `--` 뒤에 붙입니다. 생략하면 기본값을 씁니다.

```bash
dotnet run --project src/QaRunner -- --suite verdict --cases cases/rules.json --reports reports --api-url http://localhost:8000
```

| 옵션 | 뜻 | 기본값 |
|---|---|---|
| `--suite` | 시험 종류. `verdict`, `pipeline`, `load`, `recovery` | `verdict` |
| `--cases` | 판정·부하 시험은 입력값 파일, 파이프라인 시험은 시나리오 폴더 (복구 시험은 안 씀) | `cases/rules.json` / `cases/pipeline` |
| `--reports` | 보고서를 저장할 폴더 | `reports` |
| `--api-url` | 판정 API 주소. `http://` 또는 `https://`로 시작 (판정·부하 시험에서 씀) | 없음 (가짜 판정 대상 사용) |
| `--duration` | 부하·복구 시험에서 이벤트를 보내는 시간(초) | 부하 300 / 복구 120 |
| `--rate` | 부하·복구 시험에서 1초에 보내는 이벤트 수 | 5 |
| `--stop-seconds` | 복구 시험에서 처리를 멈춰 두는 시간(초). 전체 시간의 1/4 지점에서 멈춤 | 30 |

## 부하·복구 시험

실행안의 초기 목표를 그대로 잽니다. 목표는 실측 전 기준이라, 미달하면 조건·원인과 함께 그대로 남깁니다.

| 시험 | 확인 항목 | 목표 | 재는 방법 |
|---|---|---|---|
| 부하 | API 응답 시간 p95 | 3초 이하 | 가상 사용자 5명이 판정 API를 동시에 계속 부름 (응답 후 0.2초 쉬고 다음 요청) |
| 부하 | API 오류율 | 0% | 위 요청 중 연결 실패·오류 응답·시간 초과 비율 |
| 부하 | Gold 반영 시간 p95 | 10초 이하 | 이벤트를 보낸 시각부터 Gold 건수에 잡힌 시각까지 |
| 부하 | Gold 반영 건수 (적체 없음) | 보낸 건수와 같음 | 보내기가 끝나고 최대 60초 기다린 뒤 Gold에 반영된 건수 |
| 복구 | 재가동 후 회복 시간 | 60초 이하 | 재가동한 시각부터, 그 시점까지 보낸 이벤트가 모두 Gold에 반영된 시각까지 |
| 복구 | 최종 Gold 반영 건수 (집계 일치) | 보낸 건수와 같음 | 보내기가 끝나고 최대 60초 기다린 뒤 Gold에 반영된 건수 |

- **p95**는 "100건 중 95번째로 느린 건의 시간"입니다. 95%가 이 시간 안에 끝났다는 뜻이라 평균보다 체감 속도를 잘 보여 줍니다.
- **Gold 반영 시간은 근삿값입니다.** 0.5초마다 Gold 건수를 읽고, 늘어난 만큼 먼저 보낸 이벤트부터 반영된 것으로 봅니다. 이벤트 하나하나를 찾지 않아도 되어 실제 MongoDB에서도 같은 방식으로 잴 수 있습니다. 다른 데이터가 Gold에 섞이면 안 되므로 시험 환경에서 돌려야 합니다.
- **지금은 가짜 파이프라인(`FakeStreamingPipeline`)으로 돕니다.** 받은 이벤트를 1초 뒤에 반영하고, 1초에 최대 20건까지만 처리하며, 멈추라고 하면 받기만 하고 처리하지 않습니다. 그래서 측정 코드가 맞게 도는지는 확인할 수 있지만, 수치 자체는 실제 성능이 아닙니다.

## API 연결 약속 (판정 시험)

`--api-url`을 주면 케이스마다 아래처럼 요청합니다. B와 확정하기 전의 임시 약속이며, 바뀌면 `Verdicts/ApiVerdictService.cs`만 고칩니다.

| 구분 | 내용 |
|---|---|
| 요청 | `POST {API 주소}/checks`, 본문 `{ "message": "문자 내용" }` |
| 응답 | `200 OK`, 본문 `{ "verdict": "고위험" }` (다른 필드는 읽지 않음) |
| 시간 제한 | 10초 |

B와 맞출 것: 경로(`/checks`), 요청 키(`message`), 응답 키(`verdict`), 판정 값을 한글로 주는지 영문 코드로 주는지.

API가 꺼져 있거나, 오류 응답(4xx·5xx)이 오거나, 10초 안에 답이 없거나, 응답에 `verdict`가 없으면 그 케이스는 **시스템 실패**로 기록하고 다음 케이스로 넘어갑니다.

## 시험 케이스 작성법

### 판정 시험 (`cases/rules.json`)

케이스 하나가 한 줄입니다. 줄을 추가하면 코드를 고치지 않아도 채점 대상이 늘어납니다.

```json
[
  { "caseId": "R-001", "message": "[택배] 상담원에게 인증번호를 알려주세요", "expectedVerdict": "고위험" },
  { "caseId": "R-005", "message": "입력할 문자", "expectedVerdict": "주의" }
]
```

| 키 | 뜻 | 규칙 |
|---|---|---|
| `caseId` | 케이스 번호 | 겹치지 않게. 실패했을 때 이 번호로 찾음 |
| `message` | 판정 대상에 넣을 문자 (입력값) | 실제 사용자 문자가 아닌 시험용으로 만든 문자 |
| `expectedVerdict` | 정답 판정 | `고위험`, `주의`, `신호 미발견`, `판단 보류` 중 하나 |

정답 라벨은 A와 B가 함께 정하고 PR로 검토받아 넣습니다.

### 파이프라인 시나리오 (`cases/pipeline/*.json`)

파일 하나가 시나리오 하나입니다. 폴더에 파일을 추가하면 다음 실행부터 함께 채점합니다. **정답은 적지 않습니다.** 정답 계산기가 보낸 이벤트를 보고 직접 계산합니다.

```json
{
  "caseId": "DUP-001",
  "description": "같은 이벤트를 3번 보내도 Silver·Gold에는 1건만 남는다",
  "events": [
    { "eventId": "dup-001-e1", "scanId": "dup-001-s1", "offsetSeconds": 0, "verdict": "고위험" }
  ],
  "injection": { "duplicateCount": 3 }
}
```

| 키 | 뜻 |
|---|---|
| `caseId` | 시나리오 번호 |
| `description` | 무엇을 확인하는 시나리오인지. 보고서의 입력 칸에 나옴 |
| `events[].eventId` | 이벤트 고유 번호. 같은 번호를 다른 내용으로 두 번 적으면 "같은 ID 상충" 시나리오 |
| `events[].scanId` | 검사 번호 (Kafka 메시지 키) |
| `events[].offsetSeconds` | 시나리오 시작 기준 몇 초 뒤에 일어난 이벤트인지 |
| `events[].verdict` | 판정 (생략하면 `신호 미발견`) |
| `injection.duplicateCount` | 같은 이벤트를 몇 번 보낼지 (생략하면 1) |
| `injection.sendInReverseOrder` | `true`면 나중 이벤트부터 거꾸로 보냄 |
| `injection.delaySeconds` | 몇 초 늦게 도착시킬지. 300초를 넘으면 격리 대상 |

지연은 실제로 기다리지 않고 이벤트 시각을 과거로 당겨서 만들기 때문에, 6분 지연 시나리오도 바로 끝납니다.

지금 들어 있는 시나리오:

| 시나리오 | 확인하는 것 |
|---|---|
| `DUP-001` | 3번 중복 전송 → Bronze 3, Silver 1 |
| `ORDER-001` | 역순 도착 → 모두 집계 |
| `DELAY-001` | 30초 지연 → 원래 구간에 집계 |
| `LATE-001` | 6분 지연 → 격리, Gold에 집계 안 됨 |
| `CONFLICT-001` | 같은 event_id가 다른 내용으로 도착 → 뒤에 온 것 격리 |

### 정답 계산 규칙

정답 계산기(`Pipeline/ExpectedResultCalculator.cs`)는 실행안 기준으로 계산합니다. 일부는 C와 확정하기 전의 임시안입니다.

| 대상 | 규칙 |
|---|---|
| Bronze | 받은 이벤트를 모두 보관. 중복 전송도 각각 남음 |
| Silver | 같은 event_id는 한 번만 남김 |
| 격리 | 이벤트 시각보다 5분 넘게 늦게 도착했거나, 같은 event_id인데 내용이 다르면 격리 (임시안: 먼저 온 내용을 기준으로 뒤에 온 것을 격리) |
| Gold | Silver에 남은 이벤트를 이벤트 시각 기준 60초 구간별로 셈 |

Gold는 시나리오별로 나뉘지 않고 구간별로 합쳐지므로, 실행 전체를 대상으로 한 번에 비교합니다(보고서의 케이스 `전체`). 실제 파이프라인으로 돌릴 때는 다른 데이터가 섞이지 않는 시험 환경에서 실행해야 합니다.

## 보고서

실행할 때마다 보고서 폴더(기본 `reports/`)에 실행 번호로 파일 두 개가 생깁니다. 실행 번호는 시작 시각입니다(예: `2026-10-26-143015`). 같은 초에 다시 실행하면 `-2`, `-3`이 붙어 앞의 보고서를 덮어쓰지 않습니다. 이 폴더는 git에 올라가지 않습니다.

| 파일 | 용도 |
|---|---|
| `<실행 번호>.html` | 브라우저로 여는 보고서. 전체·통과·실패·통과율과 확인 항목별 결과 |
| `<실행 번호>.json` | 같은 내용을 다른 프로그램(대시보드, CI)이 읽는 용도 |
| `dashboard.html` | 폴더에 쌓인 모든 실행 기록을 모은 대시보드. 실행할 때마다 다시 만듦 |

### 실행 기록 대시보드

`reports/dashboard.html`을 브라우저로 열면 지금까지 돌린 시험의 추이를 한 화면에서 볼 수 있습니다. 데스크탑 앱의 **실행 기록 대시보드 열기** 버튼으로도 열립니다.

| 구역 | 내용 |
|---|---|
| 맨 위 | 저장된 실행 수, 마지막 실행 시각, 마지막 실행 통과율 |
| 시험 종류별 | 통과율 추이 막대 (최근 20회) |
| 부하 시험 | API 응답 시간 p95, Gold 반영 시간 p95 추이와 목표 점선 |
| 복구 시험 | 재가동 후 회복 시간 추이와 목표 점선 |
| 시험 종류별 표 | 최근 10회 실행 (실행 번호를 누르면 그 실행의 HTML 보고서로 이동, 가짜 대상이면 "가짜" 표시) |

- 새 데이터를 만들지 않고 폴더에 저장된 JSON 보고서만 읽습니다. 보고서를 지우면 대시보드에서도 빠집니다.
- 막대에 마우스를 올리면 그 실행의 시각·값·시험 대상이 나옵니다.
- 결과 형태를 통일하기 전에 만든 옛 보고서(판정 시험만 있던 때)는 읽지 않고 건너뜁니다.

보고서 표는 `케이스 · 확인 항목 · 입력 · 정답 · 실제 · 결과`로 되어 있습니다.

- 실패한 항목이 표의 위쪽에 먼저 나옵니다.
- 값을 아예 못 받은 항목은 실제 값이 "시스템 실패"로 나오고, 그 아래에 이유(연결 실패, 시간 초과 등)가 붙습니다.
- 가짜 시험 대상으로 실행한 보고서에는 "실제 시스템의 성능이 아닙니다"라는 안내가 붙습니다.

프로그램이 끝날 때 돌려주는 종료 코드는 CI가 성공·실패를 판단하는 데 씁니다.

| 종료 코드 | 뜻 |
|---|---|
| `0` | 모든 항목 통과 |
| `1` | 실패한 항목이 하나 이상 있음 (시스템 실패 포함) |
| `2` | 채점을 시작하지 못함 (옵션·측정 숫자가 틀림, 입력값 파일·시나리오 폴더가 없거나 형식이 틀림, API 주소가 잘못됨) |

## 폴더 구조

```
qa/
├─ run-qa-desktop.cmd                팀원용: 최신 코드 받기 → 빌드 → 데스크탑 앱 켜기 (더블클릭)
├─ cases/
│   ├─ rules.json                    판정 시험 입력값 + 정답
│   └─ pipeline/*.json               파이프라인 시험 시나리오
├─ src/QaRunner/                     콘솔 프로그램 (QA 러너 본체)
│   ├─ Program.cs                    콘솔 실행 순서 (옵션 → 준비 → 채점 → 출력 → 저장)
│   ├─ QaRunService.cs               콘솔·데스크탑 앱이 함께 쓰는 실행 절차 (준비 → 채점 → 보고서 저장)
│   ├─ CommandLineOptions.cs         --suite, --cases, --reports, --api-url, --duration, --rate, --stop-seconds 옵션 읽기
│   ├─ TestCaseLoader.cs             판정 시험 입력값 파일 읽기
│   ├─ VerdictChecker.cs             판정 시험 채점기
│   ├─ Models/
│   │   ├─ TestCase.cs               판정 시험 케이스 한 건 (케이스 번호, 문자, 정답)
│   │   ├─ TestResult.cs             확인 항목 하나의 채점 결과 (모든 시험 공통, 측정 항목은 목표 만족 여부 포함)
│   │   ├─ TestRun.cs                한 번 실행한 기록 (실행 번호, 시험 종류, 결과 목록, 통과율)
│   │   └─ TestSuite.cs              시험 종류 (판정 / 파이프라인 / 부하 / 복구)
│   ├─ Measurement/
│   │   ├─ MeasurementSettings.cs    측정 조건 (시간, 초당 건수, 중단 시간, 동시 사용자 수)
│   │   ├─ EventStreamSender.cs      초당 N건씩 이벤트를 계속 보내기
│   │   ├─ GoldReflectionTracker.cs  보낸 이벤트가 Gold에 반영된 시각 기록 (반영 시간·회복 시간의 출처)
│   │   ├─ LatencyStatistics.cs      p95 계산
│   │   ├─ LoadChecker.cs            부하 시험 채점기 (API 부하 + 스트림 부하)
│   │   └─ RecoveryChecker.cs        복구 시험 채점기 (중단·재가동 → 회복 시간)
│   ├─ Verdicts/
│   │   ├─ IVerdictService.cs        판정 대상이 갖춰야 할 모양
│   │   ├─ FakeVerdictService.cs     임시 가짜 판정 대상
│   │   └─ ApiVerdictService.cs      FastAPI를 호출하는 실제 판정 대상
│   ├─ Pipeline/
│   │   ├─ PipelineEvent.cs          파이프라인에 넣는 이벤트 한 건 (임시 계약)
│   │   ├─ PipelineScenario.cs       시나리오 형식 (이벤트 + 중복·역순·지연 조건)
│   │   ├─ PipelineScenarioLoader.cs 시나리오 폴더 읽기
│   │   ├─ SendPlanner.cs            언제·몇 번·어떤 순서로 보낼지 전송 계획
│   │   ├─ ExpectedResultCalculator.cs 정답 계산기
│   │   ├─ IEventSender.cs           이벤트를 넣는 쪽이 갖춰야 할 모양
│   │   ├─ IPipelineReader.cs        결과를 읽는 쪽이 갖춰야 할 모양
│   │   ├─ IProcessingControl.cs     처리를 멈추고 다시 켜는 쪽이 갖춰야 할 모양 (복구 시험)
│   │   ├─ FakePipeline.cs           임시 가짜 파이프라인 (즉시 반영, 격리 기능 없음)
│   │   ├─ FakeStreamingPipeline.cs  부하·복구 시험용 가짜 파이프라인 (1초 지연, 초당 20건, 중단·재가동)
│   │   └─ PipelineChecker.cs        파이프라인 시험 채점기
│   └─ Reports/
│       ├─ JsonReportWriter.cs       JSON 보고서 저장
│       ├─ HtmlReportWriter.cs       HTML 보고서 저장
│       ├─ RunIdGenerator.cs         겹치지 않는 실행 번호 만들기
│       ├─ ReportHistoryLoader.cs    저장된 JSON 보고서를 실행 기록으로 다시 읽기
│       ├─ DashboardWriter.cs        실행 기록 대시보드(dashboard.html) 만들기
│       └─ SvgBarChart.cs            대시보드의 추이 막대 차트(SVG) 그리기
├─ src/QaRunner.Desktop/             시연용 데스크탑 앱 (WPF)
│   ├─ App.xaml(.cs)                 시작점, 색상 정의, 화면 캡처 모드
│   ├─ MainWindow.xaml(.cs)          메인 창 (설정 · 요약 숫자 · 결과 표)
│   ├─ ResultRow.cs                  결과 표의 한 줄
│   └─ QaFolderLocator.cs            qa 폴더를 찾아 기준 폴더로 삼기
└─ tests/QaRunner.Tests/             QA 러너 자체의 테스트
```

## 확장 방법

### B의 API 형식이 확정되면

1. `Verdicts/ApiVerdictService.cs`에서 경로(`CheckEndpointPath`), 요청·응답 키를 확정된 형식에 맞춥니다.
2. 판정 값이 영문 코드로 오면 같은 파일에서 한글 판정으로 바꿔 돌려줍니다.
3. `--api-url`에 실제 주소를 넣어 실행합니다. 채점기, 보고서 등 나머지 코드는 고치지 않습니다.

### C의 파이프라인 계약이 확정되면

1. `Pipeline/PipelineEvent.cs`의 필드를 확정된 이벤트 스키마에 맞춥니다. (event_id와 scan_id 관계, run_id를 어디에 담을지)
2. `IEventSender`를 구현해 Kafka에 발행하는 클래스를 만듭니다. 보내지 못하면 `EventSendException`을 던집니다.
3. `IPipelineReader`를 구현해 MongoDB의 `bronze_events`, `silver_events`, 격리 컬렉션, `gold_metrics`를 읽는 클래스를 만듭니다.
4. 격리 규칙이 임시안과 다르게 정해지면 `Pipeline/ExpectedResultCalculator.cs`를 고칩니다.
5. `QaRunService.cs`의 `Prepare`에서 `FakePipeline`·`FakeStreamingPipeline` 대신 새 클래스를 고르게 하고, 파이프라인 시험의 Gold 대기 시간을 `PipelineChecker.DefaultCatchUpTimeout`(60초)으로 둡니다. 콘솔과 데스크탑 앱에 함께 적용됩니다.
6. 복구 시험용으로 `IProcessingControl`을 구현해 `docker stop`/`docker start`로 Spark 컨테이너를 멈추고 켜는 클래스를 만듭니다. 실패하면 `ProcessingControlException`을 던집니다.

### 새로운 시험 대상 추가하기

판정 시험은 `IVerdictService`, 파이프라인·부하 시험은 `IEventSender`·`IPipelineReader`, 복구 시험은 여기에 `IProcessingControl`까지 구현한 클래스를 추가하고, `QaRunService.cs`의 `Prepare`에서 고르게 합니다. 가짜 대상의 클래스 이름은 `Fake`로 시작해야 보고서와 데스크탑 앱에 가짜 안내가 붙습니다.

### 보고서에 항목 추가하기

1. `Models/TestRun.cs`(실행 전체) 또는 `Models/TestResult.cs`(확인 항목 하나)에 속성을 추가합니다. JSON 보고서에는 자동으로 들어갑니다.
2. HTML 보고서에 보여 주려면 `Reports/HtmlReportWriter.cs`의 `BuildHtml`을, 데스크탑 앱에 보여 주려면 `MainWindow.xaml`과 `ShowTestRun`을 고칩니다.

### 코드를 고칠 때 지킬 것

- 이름은 무엇인지 바로 알 수 있게 짓습니다. 예: `testCase`, `failedCount` (한 글자 변수, 줄임말 쓰지 않기)
- 클래스, 함수, 중요한 변수에는 무엇이고 왜 있는지 한국어 주석을 답니다.
- 동작을 바꾸면 `tests/QaRunner.Tests/`에 테스트를 추가하고 `dotnet test`가 통과하는지 확인합니다.
- 코드, 옵션, 파일이 바뀌면 이 README도 같은 커밋에서 고칩니다.
