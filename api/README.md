# api — 예매·수집·판정 조회 API (담당: B)

FastAPI로 예매(대기열·좌석 점유·결제·추가 확인)를 처리하고, 앱의 행동 이벤트를 받아 Kafka로 발행하며, Spark 판정 작업이 만든 세션 판정을 조회해 주는 서버입니다. 결제 요청이 오면 그 세션의 판정에 따라 바로 결제하거나 추가 확인·보류로 보냅니다.

## 여기에 둘 것

- FastAPI 앱: 예매 API, 수집 API(`POST /events` → Kafka), 판정 조회 API, 운영자 조회 API
- 좌석 5분 점유·대기 순번(Redis), 서비스 DB(`shows`·`seats`·`bookings`)
- 예매 API가 직접 만드는 서버 이벤트(`queue_enter`·`seat_hold`·`payment_request`, `origin: "server"`) 발행
- 이 폴더만의 실행 방법과 테스트

Spark 판정 작업(특징 → 점수 → 판정)도 B가 맡고, 코드는 [pipeline/](../pipeline/README.md)에 둡니다.

## 지켜야 할 계약

[docs/contract.md](../docs/contract.md)의 아래 장을 따릅니다. 바꾸고 싶으면 계약 문서를 먼저 고쳐 PR로 올립니다.

| 장 | 내용 |
|---|---|
| 2. 판정 값 | `NORMAL` / `WATCH` / `VERIFY` / `HOLD` |
| 3. 예매 API | 경로·필드·결제 응답 상태. 결제 때 판정이 없으면 2초 기다린 뒤 추가 확인 (초안) |
| 4. 수집 API | `POST /events` → 202 `{ "accepted": n }`, 형식이 틀린 이벤트도 받아서 넘김 (격리는 파이프라인) |
| 5. 판정 조회 API | `GET /verdicts/{sessionHash}` → 200 판정·근거 / 404 판정 전 |
| 6. Kafka | 토픽 `ticket-events`, 키 `sessionHash`, 재전송은 같은 `eventId` |
| 7-1. 서비스 DB · Redis | `shows`, `seats`, `bookings`, `seat_hold:*`, `queue:*` |

## QA로 확인하는 법

API를 켜 둔 상태에서 QA 러너를 실행하면, `qa/cases/sessions.json`의 사람·봇 세션 행동을 수집 API로 보내고 판정 조회 API에서 판정을 받아 정답과 비교합니다. 정답(사람인지 봇인지)은 API에 보내지 않습니다.

- 데스크탑 앱: `qa/run-qa-desktop.cmd` 실행 → **빠른 점검** → 시험 대상 **내 판정 API** → 주소 `http://localhost:8000` → 실행
- 명령어 (`qa/` 폴더에서):

```bash
dotnet run --project src/QaRunner -- --suite verdict --api-url http://localhost:8000
```

부하(동시 5명, 판정까지 p95 3초 목표)도 같은 주소로 확인할 수 있습니다.

```bash
dotnet run --project src/QaRunner -- --suite load --duration 60 --api-url http://localhost:8000
```

판정이 10초 안에 나오지 않으면 그 세션은 "응답 없음(시스템 실패)"으로 기록됩니다.

## 작업 브랜치

`main`에서 `feature/api-...` 브랜치를 만들어 작업하고 PR로 합칩니다. 예: `feature/api-booking`
