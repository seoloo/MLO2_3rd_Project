# 매크로 봇 작업 계획

[← QA 안내로 돌아가기](../README.md)

> **상태 (2026-11-02):** 설계를 마치고 구현을 시작하는 단계입니다. 아래는 **앞으로 구현할 방법**이고, 아직 `qa/bots/` 코드는 없습니다. 단계가 끝날 때마다 [구현 순서](#구현-순서와-완료-기준) 표의 상태와 이 절의 내용을 실제 코드에 맞게 고칩니다.

정답(사람인지 봇인지)을 아는 트래픽을 **우리 예매 사이트에 실제로 보내** 판정 쪽이 학습·평가에 쓸 첫 데이터 묶음을 만듭니다. 기획서 WBS 4.3(트래픽 생성)·4.4(정답 표·첫 데이터)·4.7.1(동시 점유 시험)에 해당합니다.

**한 줄 요약:** 예매 흐름 코드는 하나만 만들고, 봇마다 다른 것은 "행동 프로필" 숫자로만 바꿉니다. 정답은 QA 쪽 정답 표에만 적고 사이트에는 보내지 않습니다.

## 무엇을 만드나

| 종류 | 도구 | 진입 시점 | 단계·클릭 간격 | 화면 이벤트 | 세션·기기 | 기대 판정 |
|---|---|---|---|---|---|---|
| 가상 사람 | Locust | 오픈 뒤 수 초~수십 초로 퍼짐 | 프론트가 잰 단계별 체류 시간에서 뽑음 · 망설임 · 되돌아가기 | 합성 클릭을 `/api/events/`로 보냄 | 1계정 1세션 | 정상 · 관찰 |
| 봇 A | Locust | 오픈 즉시 (약 0.03초) | 고정 간격 `constant(0.1)` | 없음 (API만 부름) | 1계정 1세션 | 추가 확인 이상 |
| 봇 B | Locust | 오픈 즉시 (약 0.05초) | 작은 무작위 `between(0.08, 0.16)` | 없음 | 1계정 1세션 | 추가 확인 이상 |
| 봇 C | Playwright | 사람 속도 | 실제 버튼을 누르지만 간격이 약 0.4초로 지나치게 일정 | 사이트의 `tracking.js`가 스스로 보냄 | 1계정 1세션 | 추가 확인 이상 |
| 봇 D | Locust | 여러 세션이 동시에 | 세션 하나하나는 사람 속도 | 없음 | 한 기기에서 세션 20~수십 개 | 추가 확인 이상 |

숫자는 [판정 시험 사례](../docs/test-cases.md#판정-시험-casessessionsjson)의 S-001~S-007 기준 초안이며, 확정값은 `qa/docs/bot-rules.md`(공개 행동 규칙)에 적습니다. 가상 사람이 화면 이벤트를 보내지 않으면 "화면 기록이 있으면 사람"이라는 정답 단서가 생기므로 반드시 합성 이벤트를 함께 보냅니다.

## 브랜치와 작업 폴더

- **봇 코드는 `qa` 브랜치의 `qa/bots/`에** 만듭니다. `main`에서 고치지 않습니다.
- **시험 대상 사이트는 프론트 브랜치(`codex/frontend`)를 옆 폴더에 꺼내서** 실행합니다. `qa`와 `codex/frontend`는 2026-11-02 이력 정리 이후 공통 이력이 없어서 병합하지 않습니다. 브랜치를 바꿔 가며 확인할 필요 없이 두 폴더를 동시에 씁니다.

처음 한 번, 저장소 폴더에서 프론트 브랜치를 옆 폴더에 꺼냅니다.

```powershell
git worktree add ../MLO2_frontend codex/frontend
```

팀원이 프론트를 새로 올리면 프론트 폴더만 최신으로 받습니다.

```powershell
git -C ../MLO2_frontend pull --ff-only
```

| 폴더 | 브랜치 | 용도 |
|---|---|---|
| `MLO2_3rd_Project` | `qa` | 봇·QA 코드 작업과 커밋 |
| `MLO2_frontend` | `codex/frontend` | 시험 대상 사이트 실행 (보기·실행만, 커밋하지 않음) |

- 사이트 실행 방법은 프론트 폴더의 `app/ticketing-site/README.md`를 따릅니다. 지금은 `127.0.0.1:8000`, 공연 slug는 `aurora`·`jazz`·`wave`입니다.
- 봇 실행 환경은 사이트와 따로 `qa/bots/.venv`(Git 제외)로 만듭니다. 사이트는 프론트가 정한 Python 버전을 쓰고, 봇은 Locust·Playwright가 지원하는 버전(지금 PC는 Python 3.13)을 씁니다.

## 지킬 원칙

1. **사이트 규칙은 그대로.** CSRF·3초 입장 대기·회원당 4매 제한을 끄거나 봇 전용 우회 경로를 사이트에 만들지 않습니다. 봇도 사람과 같은 길로 예매합니다.
2. **정답은 QA에만.** 계정 이름·비밀번호 규칙·이벤트·요청 헤더에 사람/봇 단서를 넣지 않습니다. 계정 이름은 `user_7f3a2c`처럼 같은 규칙으로 만들고, `bot_a_01` 같은 이름은 쓰지 않습니다.
3. **도구 흔적을 맞춤.** 가상 사람과 봇 A·B·D는 같은 Locust라 User-Agent 같은 헤더를 같은 값으로 보냅니다. 도구 차이가 정답 단서가 되지 않게 합니다.
4. **봇 차이는 프로필로만.** 진입 지연·단계 체류·클릭 간격·되돌아가기 확률·화면 이벤트 여부·기기당 세션 수는 `behavior_profiles.py` 한 곳에만 두고, `bot-rules.md`의 숫자와 같게 유지합니다.
5. **다시 돌리면 같게.** 실행마다 `run_id`와 난수 `seed`를 만들어 정답 표와 사이트(`TG_RUN_ID`)에 같이 남깁니다. 난수는 seed로 만든 전용 `Random`만 씁니다.
6. **작게 확인하고 키움.** 세션 1개 → 수십 → 수백 순서로 늘립니다. 대상 주소는 설정값이고, 허용 목록 검사를 통과해야만 시작합니다.

## 봇이 따라가는 예매 흐름

프론트 사이트(`booking/views.py`)의 실제 흐름입니다. 모든 Locust 봇이 같은 `booking_flow.py` 한 벌을 씁니다.

| 순서 | 요청 | 확인할 것 |
|---|---|---|
| 1. 가입·로그인 | `GET /accounts/signup/`로 `csrftoken`을 받고 `username`·`password1`·`password2`·`tracking_consent=on` POST | 세션 쿠키가 생김. 회원당 4매 한도 때문에 세션마다 새 계정 |
| 2. 입장 | `GET /shows/<slug>/enter/` | 서버가 세션에 "지금 + 3초"를 적고 `queue_enter`를 기록 |
| 3. 대기 후 좌석 화면 | `/shows/<slug>/queue/` → `/shows/<slug>/seats/` | 3초가 지나야 열림. 화면 HTML 안의 `request_key`를 꺼내 둠 |
| 4. 좌석 확보 | `GET /api/shows/<slug>/seats/`로 빈 좌석을 고르고 `POST /api/shows/<slug>/reserve/` (JSON `{seats, request_key}` + `X-CSRFToken`) | 3초 전에 보내면 409와 `queue_bypass_rejected`. 이미 팔린 좌석도 409 |
| 5. 예매 완료 | 응답의 `url`(`/bookings/<id>/checkout/`)로 `agree=on` POST | 승인 시뮬레이션이라 실제 결제 없음 |

봇 C는 같은 흐름을 브라우저로 진행하며 `data-testid`로 누릅니다: `seat-A-1` → `reserve-seats` → `payment-agree` → `checkout`.

## 코드 구조

파일 이름은 제안이며 만들 때 이 구조를 실제에 맞게 고칩니다.

```
qa/bots/
├─ requirements.txt                 locust · playwright · pytest
├─ bot-settings.example.json        대상 주소 · 공연 · 계정 수 예시 (실제 값 파일은 Git 제외)
├─ common/
│   ├─ settings.py                  설정 읽기 · run_id · seed 만들기
│   ├─ target_guard.py              대상 주소 허용 목록 검사 (가장 먼저 실행)
│   ├─ accounts.py                  같은 규칙의 중립 이름 계정 가입 · 로그인
│   ├─ booking_flow.py              예매 5단계 · 단계별 결과 (409·만료도 결과로 돌려줌)
│   ├─ behavior_profiles.py         봇마다 다른 숫자 (진입 · 간격 · 세션 수 · 프로필 버전)
│   ├─ screen_events.py             /api/events/ 형식의 화면 이벤트 합성 (가상 사람용)
│   └─ answer_key.py                정답 표 한 줄씩 기록
├─ locust/locustfile.py             VirtualHuman · BotA · BotB · BotD (비율 = 클래스 weight)
├─ playwright/bot_c.py              실제 브라우저로 화면을 누르는 봇 C
├─ tools/leak_check.py              내보낸 이벤트·요청 로그에서 정답 단서 검색
├─ tools/contention_test.py         같은 좌석에 100명 동시 확보 → 1건만 성공하는지
└─ tests/                           pytest 단위 테스트 (안전장치 · 프로필 · 합성기)
qa/docs/bot-rules.md                봇 행동 규칙 공개 문서
qa/cases/ticketing/answer-key.example.jsonl   정답 표 형식 예시
qa/reports/bot-runs/<run_id>/       실제 정답 표 · 실행 요약 (Git 제외)
```

모듈은 위에서 아래로만 부릅니다: **실행 진입점**(`locustfile.py`·`bot_c.py`·`tools/`) → **행동**(`behavior_profiles`·`screen_events`) → **공통 흐름**(`accounts`·`booking_flow`) → **안전장치**(`target_guard`·`settings`). 정답 표(`answer_key`)는 진입점이 세션을 만들 때만 기록하고, `booking_flow`와 사이트는 그 값을 모릅니다.

## 구현 순서와 완료 기준

앞 단계가 완료 기준을 통과해야 다음 단계로 갑니다. 단계마다 확인 후 커밋하고 이 README를 같은 커밋에서 고칩니다.

| 단계 | 만드는 것 | 구현 포인트 | 완료 기준 | 상태 |
|---|---|---|---|---|
| 1. 환경 준비 | `requirements.txt`, `.venv` | 프론트 폴더에서 사이트 실행 · `TG_RUN_ID`를 봇의 run_id와 같게 지정 | `/healthz/` 200 · `playwright install chromium` 완료 | 대기 |
| 2. 설정·안전장치 | `settings.py`, `target_guard.py`, 테스트 | 주소를 파싱해 **host만** 허용 목록과 비교 (문자열 포함 검사 금지: `localhost.evil.com` 통과 방지) · 허용은 localhost·127.0.0.1·팀 공용 주소 | 허용 밖 주소·비슷한 이름 주소가 모두 거부됨 | 대기 |
| 3. 계정 | `accounts.py` | CSRF 토큰을 받아 가입 · 이름은 무작위 · 사람과 봇이 같은 규칙 | N개 계정 생성·재로그인 성공 · 이름·비밀번호에 정답 단서 0건 | 대기 |
| 4. 예매 흐름 | `booking_flow.py`, 테스트 | HTTP 객체와 대기 함수를 인자로 받음 (Locust client·requests 세션 모두 사용, 테스트에서 시간 건너뜀) | 세션 1개가 예매 완료까지 감 · 팔린 좌석·3초 전 확보가 409 결과로 기록됨 | 대기 |
| 5. 프로필·정답 표 | `behavior_profiles.py`, `answer_key.py`, `bot-rules.md` | 봇 차이를 숫자로만 · 정답 표는 JSONL 한 줄 = 세션 하나 | 같은 seed → 같은 간격 목록 · 문서 숫자 = 코드 숫자 | 대기 |
| 6. Locust 봇 | `locustfile.py`, `screen_events.py` | `BaseBookingUser(HttpUser)` 하나에 공통 흐름, 서브클래스는 프로필·`wait_time`·`weight`만 다름 | 클래스마다 1세션 통과 · 섞어서 1분 실행 시 단계별 성공/거절 집계 | 대기 |
| 7. Playwright 봇 C | `bot_c.py` | 세션마다 새 브라우저 컨텍스트 · 클릭 간격은 프로필에서 | 5세션 예매 완료 · `/ops/`에 화면 이벤트가 쌓임 | 대기 |
| 8. 점검 도구 | `leak_check.py`, `contention_test.py` | 이벤트 내보내기에서 정답 단서 검색 · 정답 표 세션 수와 사이트 세션 수 대조 · 같은 좌석 100명 동시 확보 | 누설 0건 · 세션 수 일치 · 점유 성공 정확히 1건 (PostgreSQL) | 대기 |
| 9. QA 프로그램 연결 | C# 결과 읽기·새 시험 종류 | 아래 "QA 프로그램과 연결하는 방법" | 데스크탑 앱·보고서에 봇 실행 결과 표시 | 대기 |

## QA 프로그램과 연결하는 방법

봇은 Python(Locust·Playwright)으로 만들고 C#으로 다시 만들지 않습니다. 기획서가 Locust 기준이고, 봇 C까지 한 언어로 맞추는 편이 단순합니다. QA 프로그램과는 **결과 파일**로 연결합니다.

1. **혼자 실행:** 봇은 명령줄에서 혼자 돌고, 실행마다 `qa/reports/bot-runs/<run_id>/`에 두 파일을 남깁니다.
   - `answer-key.jsonl`: 세션 하나에 한 줄. 예) `{"runId":"20261103-0930-a1","sessionRef":"user_7f3a2c","actor":"봇 A","profileVersion":"bot_a@0.1","seed":1234,"startedAt":"2026-11-03T09:30:01+09:00"}`
   - `run-summary.json`: 실행 설정, 종류별 세션 수, 단계별 성공·거절 수, 시작·끝 시각
2. **채점에 쓰기:** `actor` 값은 판정 시험 사례(`cases/sessions.json`)와 같은 이름(`사람`, `봇 A`~`봇 D`)을 씁니다. 그러면 기존 채점기·탐지율·오탐률 계산(`VerdictChecker`, `DetectionSummary`)을 그대로 다시 씁니다. `sessionRef`를 판정 쪽 세션 키와 잇는 방법은 연결 계약 v0에서 정합니다.
3. **버튼으로 실행:** `Models/TestSuite.cs`에 트래픽 생성 시험 종류를 추가합니다. `DockerProcessingControl`이 `docker`를 외부 프로세스로 부르듯, 데스크탑 앱·콘솔이 Python 봇을 외부 프로세스로 실행하고 `run-summary.json`을 보고서에 보여 줍니다. 순서는 [새로운 시험 영역·시험 종류 추가하기](../docs/development.md#새로운-시험-영역시험-종류-추가하기)를 따릅니다.

GitHub Actions에서는 봇의 pytest 단위 테스트만 돌리고, 실제 트래픽은 보내지 않습니다.

## 실행 방법 (구현 후)

사이트를 프론트 폴더에서 먼저 켠 뒤 `qa/bots/`에서 실행합니다. 처음 한 번은 실행 환경을 만듭니다.

```powershell
python -m venv .venv
.venv\Scripts\pip install -r requirements.txt
.venv\Scripts\playwright install chromium
```

단위 테스트, 한 종류·한 세션 스모크, 섞어서 실행, 봇 C, 실행 뒤 점검 순서입니다.

```powershell
.venv\Scripts\pytest tests
.venv\Scripts\locust -f locust/locustfile.py BotA --host http://127.0.0.1:8000 --headless -u 1 -r 1 -t 30s
.venv\Scripts\locust -f locust/locustfile.py --host http://127.0.0.1:8000 --headless -u 200 -r 20 -t 5m
.venv\Scripts\python playwright/bot_c.py --host http://127.0.0.1:8000 --sessions 5
.venv\Scripts\python tools/leak_check.py ../reports/bot-runs/<run_id>
```

## 하지 말 것

- 사이트의 CSRF·입장 대기·구매 한도를 끄거나 봇 전용 경로를 만들기 (게이트 우회 시도는 봇 행동으로만 시험)
- 계정 이름·User-Agent·이벤트 필드에 사람/봇 단서 넣기
- 정답 표를 사이트로 보내거나 Git에 올리기 (형식 예시만 `qa/cases/ticketing/`에 둠)
- 봇 숫자를 코드 곳곳에 흩어 두거나 전역 `random` 쓰기
- 대상 주소를 코드에 고정하거나 허용 목록 검사를 건너뛰기
- 봇이 사이트의 임시 관리자(`admin`)·`member01` 계정을 쓰기. 관리자 로그인은 점검 도구가 로컬에서 이벤트를 내보낼 때만 씁니다

## 구현 전에 팀과 정할 것

| 정할 것 | 함께 정할 사람 | 내용 |
|---|---|---|
| 기기 식별 필드 | 전원 (연결 계약 v0) | 사이트 이벤트에 `deviceHash`가 없습니다. 봇 D의 "한 기기 세션 수"는 계약에서 정한 필드를 따라 구현합니다 |
| 로컬 IP | 전원 | 로컬에서는 모든 요청이 `127.0.0.1`이라 IP로 기기를 나눌 수 없습니다. 기기 구분은 계약 필드로만 합니다 |
| 가상 사람 시간 분포 | 프론트 | 단계별 체류 시간을 받기 전에는 임시 분포로 구현하고 프로필 버전에 "임시"를 표시합니다 |
| 동시 점유 시험 DB | DB 담당 | 프론트 브랜치에서 Docker 구성이 빠졌습니다. 사이트는 `POSTGRES_HOST`가 있으면 PostgreSQL을 쓰므로 PostgreSQL을 어디에 띄울지 정합니다 (SQLite는 요청을 한 줄로 처리해 진짜 동시 상황이 생기지 않음) |
| 공용 서버 주소 | 인프라 | AWS 공용 사이트 주소가 정해지면 허용 목록에 추가합니다. 그 전에는 로컬만 허용합니다. 공용 서버에 올리기 전에 임시 관리자 비밀번호를 바꿔야 합니다 |

> **봇·부하 도구는 우리 앱 · localhost · 우리 공용 서버에만 실행합니다.** 허용 목록 밖 주소는 시작 단계에서 거부하고, 프랙티켓·티켓링크 등 실제 예매 사이트에는 절대 실행하지 않습니다 (정보통신망법 제48조 · 형법 제314조 · 기획서 14쪽).
