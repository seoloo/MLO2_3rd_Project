# 티켓가드 연결 계약 (초안)

앱·백엔드·인프라·QA가 서로 주고받는 형식을 한곳에 모은 문서입니다. 각자 이 형식대로 만들면 따로 맞추지 않아도 연결됩니다.

- **상태: 초안.** 표의 값은 QA 러너가 지금 기대하는 형식입니다. 팀이 다르게 정하면 **이 문서를 먼저 고치고**, 각 영역 코드를 거기에 맞춥니다.
- 고칠 때는 PR로 올리고, 영향받는 담당(표의 "만드는 쪽"·"쓰는 쪽")에게 리뷰를 받습니다.
- 맨 아래 [정해야 할 것](#10-정해야-할-것) 목록을 하나씩 확정해 나갑니다.
- 담당: **A 앱**, **B 백엔드**(FastAPI·Spark 판정 작업), **C 인프라**(Docker·EC2·Kafka·Bronze 수집·DB·S3), **QA**.

## 1. 전체 흐름

```
예매 앱(A) ──① 예매 요청──▶ FastAPI(B) ──좌석 점유·대기 순번──▶ Redis · 서비스 DB
     │                         │
     └──② 행동 이벤트────────▶ 수집 API ──③──▶ Kafka ──▶ Spark 수집(C) ──▶ Bronze
                                                  └──▶ Spark 판정(B) ──④──▶ Silver · 세션 특징 · Gold 판정
운영자 상황판(A) ◀──⑤ 판정 조회 API(B) ◀───────────────────────────────── 분석 DB(MongoDB)
QA ──②와 같은 이벤트──▶ 수집 API      QA ──③과 같은 이벤트──▶ Kafka      QA ──⑤·④를 읽어 정답과 비교
```

| 번호 | 연결 | 만드는 쪽 | 쓰는 쪽 |
|---|---|---|---|
| ① | 예매 API (대기열·좌석 점유·결제·추가 확인) | B | A |
| ② | 수집 API (행동 이벤트) | B | A, QA |
| ③ | Kafka 이벤트 | B(수집 API) | C(Bronze 수집), B(판정 작업), QA(직접 주입) |
| ④ | 분석 DB 메달리온 컬렉션 | C(Bronze), B(Silver·Gold) | A(상황판·리포트), QA |
| ⑤ | 판정 조회 API | B | A(운영자 화면), QA |

## 2. 판정 값

API·이벤트·DB는 **영문 코드**를 쓰고, 화면에는 한글로 보여 줍니다. 점수가 높을수록 아래 단계입니다.

| 코드 | 화면 표시 | 사용자에게 일어나는 일 | 묶음 |
|---|---|---|---|
| `NORMAL` | 정상 | 그대로 진행 | 정상 처리 |
| `WATCH` | 관찰 | 사용자에겐 변화 없음, 상황판에만 표시 | 정상 처리 |
| `VERIFY` | 추가 확인 | 결제 직전 본인 확인 한 번 더. 통과하면 그대로 결제 | 추가 확인 이상 |
| `HOLD` | 보류 | 결제 대기, 운영자 검토 | 추가 확인 이상 |

- **탐지율·오탐률은 묶음으로 셉니다.** 봇이 "추가 확인 이상"이면 탐지, 사람이 "추가 확인 이상"이면 오탐입니다 (기획서 21쪽).
- 판정을 내리지 못한 경우(서버 오류, 시간 초과)는 판정 값이 아닙니다. 오류 응답(5xx)으로 따로 알립니다.
- 사람을 "매크로 사용자"로 단정하는 표현은 쓰지 않습니다. 화면에는 단계 이름과 근거만 보여 줍니다.

## 3. 예매 API (B → A)

모든 요청·응답은 `Content-Type: application/json`입니다. 사용자는 이름 대신 해시(`sessionHash`, `deviceHash`)로만 다룹니다.

| 요청 | 본문 | 성공 응답 | 실패 응답 |
|---|---|---|---|
| `POST /queue/enter` | `{ "showId", "sessionHash", "deviceHash" }` | 200 `{ "queueToken", "position" }` | 400, 404(공연 없음) |
| `GET /queue/{queueToken}` | — | 200 `{ "position", "admitted" }` (`admitted`가 `true`면 좌석 화면으로) | 404 |
| `POST /seats/hold` | `{ "showId", "seatId", "sessionHash" }` | 200 `{ "holdId", "expiresAt" }` (5분 점유) | 409(이미 점유됨), 410(매진) |
| `POST /payments` | `{ "holdId", "sessionHash" }` | 200 `{ "status": "PAID" }` 또는 202 `{ "status": "VERIFY_REQUIRED", "verifyId" }` 또는 202 `{ "status": "ON_HOLD" }` | 404(점유 만료), 409 |
| `POST /verifications/{verifyId}` | `{ "code" }` | 200 `{ "status": "PAID" }` | 400(코드 틀림), 404 |

- 결제 응답은 그 세션의 판정을 따릅니다: `NORMAL`·`WATCH` → `PAID`, `VERIFY` → `VERIFY_REQUIRED`, `HOLD` → `ON_HOLD`.
- **결제 요청 때 판정이 아직 없으면** 최대 2초 기다리고, 그래도 없으면 `VERIFY_REQUIRED`로 처리합니다(초안). 스트림이 멈춰도 결제가 판정 없이 통과하지 않게 하려는 규칙이며, [정해야 할 것](#10-정해야-할-것)에서 확정합니다.
- 오류 본문은 모두 `{ "error": "설명" }`입니다. 서버 오류는 500·503입니다.
- 앱 개발 중에는 QA 데스크탑 앱의 **가짜 예매·판정 서버**가 이 API를 흉내 냅니다 (`qa/src/QaRunner/AppMock/`).

## 4. 수집 API와 행동 이벤트 (A·QA → B)

```
POST /events
Content-Type: application/json

{ "events": [ { 이벤트 }, { 이벤트 }, ... ] }
```

- 성공: 202 `{ "accepted": 3 }`. 앱은 1초 단위로 모아서 보냅니다.
- 형식이 틀린 이벤트도 받아서 Kafka로 넘깁니다. 걸러 내는 것은 파이프라인의 격리 규칙(8장)입니다. 본문 자체가 JSON이 아니면 400입니다.

### 4-1. 이벤트 한 건

```json
{
  "eventId": "e-9f1c2b8e-0001",
  "type": "seat_click",
  "origin": "client",
  "sessionHash": "s-a3f9…",
  "deviceHash": "d-71c2…",
  "showId": "p-1120-1930",
  "screen": "seat",
  "x": 212,
  "y": 344,
  "occurredAt": "2026-11-20T11:02:01.204Z",
  "source": "app",
  "runId": null,
  "caseId": null
}
```

| 필드 | 형식 | 필수 | 뜻 |
|---|---|---|---|
| `eventId` | 문자열 | 예 | 이벤트 고유 번호. Silver 중복 제거 기준. 재전송 때도 같은 값 |
| `type` | 문자열 | 예 | 이벤트 종류 (4-2) |
| `origin` | `client` / `server` | 예 | 누가 만든 이벤트인지. 기기가 보낸 것은 꾸밀 수 있으므로 판정에서 무게를 다르게 둠 |
| `sessionHash` | 문자열 | 예 | 세션 해시. **Kafka 메시지 키** |
| `deviceHash` | 문자열 | 예 | 기기 해시. 같은 기기의 세션 수를 셀 때 씀 |
| `showId` | 문자열 | 예 | 공연 회차 |
| `screen` | 문자열 | 아니요 | 이벤트가 난 화면: `show`, `queue`, `seat`, `payment`, `verify` |
| `x`, `y` | 정수 | 아니요 | 클릭 위치 (클릭 이벤트만) |
| `occurredAt` | ISO 8601 (UTC, 밀리초) | 예 | 이벤트가 일어난 시각. **집계 구간과 지연 판단의 기준** |
| `source` | `app` / `qa` | 예 | 실제 앱 이벤트인지 QA가 넣은 시험 이벤트인지 |
| `runId`, `caseId` | 문자열 또는 null | 아니요 | QA 실행 번호와 사례 번호. QA 이벤트에만 있음 |

- **개인정보:** 이름·전화번호·정답 정보(사람인지 봇인지)는 넣지 않습니다.

### 4-2. 이벤트 종류

| `type` | 만드는 곳 (`origin`) | 언제 |
|---|---|---|
| `page_view` | client | 화면에 들어올 때 (`screen`에 화면 이름) |
| `show_select` | client | 공연·회차를 고를 때 |
| `queue_enter` | server | 예매 API가 대기열 진입 요청을 받았을 때 |
| `seat_click` | client | 좌석을 누를 때 (`x`, `y`) |
| `seat_hold` | server | 예매 API가 좌석 점유 요청을 받았을 때 |
| `payment_request` | server | 예매 API가 결제 요청을 받았을 때 |
| `verify_submit` | server | 추가 확인 코드를 받았을 때 |
| `back_navigation` | client | 뒤로 가기·이전 화면으로 돌아갈 때 |

- `origin: server` 이벤트는 예매 API가 직접 수집 API(또는 Kafka)로 보냅니다. 화면 기록 없이 API만 부른 세션(서버 이벤트만 있고 클라이언트 이벤트가 없음)도 판정 신호가 됩니다.

## 5. 판정 조회 API (B → A·QA)

```
GET /verdicts/{sessionHash}
```

| 상황 | 응답 |
|---|---|
| 판정이 있음 | 200 (아래 본문) |
| 아직 판정이 없음 | 404 `{ "error": "판정 전" }` |
| 서버 오류 | 500·503 `{ "error": "설명" }` |

```json
{
  "sessionHash": "s-a3f9…",
  "verdict": "VERIFY",
  "score": 0.82,
  "rules": [ { "ruleId": "R1", "measured": "클릭 간격 평균 0.10초", "threshold": "0.25초 미만" } ],
  "ruleVersion": "rules-2026-11-20",
  "decidedAt": "2026-11-20T11:02:03.900Z"
}
```

| 필드 | QA가 쓰는지 |
|---|---|
| `verdict` | **예** (2장 코드) |
| `score`, `rules`, `ruleVersion`, `decidedAt` | 아니요 (운영자 화면의 근거 표시용) |

- QA는 세션 이벤트를 수집 API로 보낸 뒤 이 API를 0.5초마다 조회하고, **10초 안에 판정이 나오지 않거나 오류 응답이 오면 그 사례를 시스템 실패**로 기록합니다.
- QA 쪽 구현: `qa/src/QaRunner/Verdicts/ApiVerdictService.cs`

## 6. Kafka

| 항목 | 값 |
|---|---|
| 이벤트 토픽 | `ticket-events` (수집 API가 발행) |
| 판정 토픽 | `ticket-verdicts` (판정 작업이 발행, 상황판 알림용) |
| 메시지 키 | `sessionHash` (같은 세션의 이벤트는 같은 파티션으로 → 세션 안 순서 유지) |
| 메시지 값 | 4-1의 이벤트 JSON (UTF-8) |
| 재전송 | 확인(ack)을 못 받아 다시 보낼 때는 **같은 `eventId`** 로 보냄 (중복 제거는 Silver가 함) |

- QA 파이프라인 시험은 수집 API를 거치지 않고 `ticket-events`에 직접 넣습니다. QA 쪽 구현: `qa/src/QaRunner/Pipeline/KafkaEventSender.cs`

## 7. MongoDB 컬렉션

예매를 처리하는 **서비스 DB**와 분석하는 **분석 DB**를 나눕니다. 분석 부하가 예매 처리를 느리게 하지 않게 하기 위해서입니다.

### 7-1. 서비스 DB와 Redis (B)

| 위치 | 컬렉션·키 | 들어가는 것 |
|---|---|---|
| 서비스 DB | `shows`, `seats`, `bookings` | 공연·회차·좌석·예매 (사용자는 가명 ID) |
| Redis | `seat_hold:{showId}:{seatId}` | 좌석 점유 (5분 만료 키) |
| Redis | `queue:{showId}` | 대기 순번 |

MongoDB의 TTL 삭제는 약 60초 간격으로 돌아 좌석이 최대 1분 늦게 풀릴 수 있어서, 좌석 점유는 Redis 만료 키로 처리합니다.

### 7-2. 분석 DB 메달리온

데이터베이스 이름: `ticketguard`

| 컬렉션 | 문서 하나 | 고유 키(`_id`) | 주요 필드 | 만드는 쪽 |
|---|---|---|---|---|
| `bronze_events` | Kafka에서 받은 메시지 1건 (재전송 포함 전부) | `"{topic}:{partition}:{offset}"` | `topic`, `partition`, `offset`, `receivedAt`, `raw`(이벤트 JSON을 **하위 문서로** 그대로) | C |
| `quarantine_events` | 격리된 메시지 1건 | 자동(ObjectId) | `eventId`, `reason`(`late_arrival`/`conflict`/`invalid`), `receivedAt`, `raw` | B |
| `silver_events` | 중복을 없앤 이벤트 1건 | `eventId` | 4-1의 필드 전부 + `firstReceivedAt` | B |
| `silver_session_features` | 세션 1개의 특징 | `sessionHash` | 클릭 간격 평균·편차, 오픈 후 진입 속도, 같은 기기 세션 수, 화면 흐름 건너뜀 여부 | B |
| `gold_verdicts` | 세션 1개의 판정 | `sessionHash` | 5장의 응답 필드 전부 | B |
| `gold_live_metrics` | 60초 구간 1개의 집계 | 구간 시작 시각 문자열 (예: `"2026-11-20T11:02:00Z"`) | `windowStart`(날짜), `total`, `byType`(`{ "seat_click": 120, ... }`), `byVerdict`, `updatedAt` | B |
| `gold_reports` | 회차 1개의 판매 결과 리포트 | `showId` | 정상 판정 구매 비율, 구역별 결과, 조치 기록 | C(배치) |

QA는 이 컬렉션을 아래처럼 읽습니다. 필드 이름이 바뀌면 `qa/src/QaRunner/Pipeline/MongoPipelineReader.cs`를 같이 고칩니다.

| QA가 확인하는 것 | 읽는 방법 |
|---|---|
| Bronze 보관 건수 | `bronze_events`에서 `raw.runId`, `raw.caseId`가 일치하는 문서 수 |
| Silver 고유 건수 | `silver_events`에서 `runId`, `caseId`가 일치하는 문서 수 |
| 격리 건수 | `quarantine_events`에서 `raw.runId`, `raw.caseId`가 일치하는 문서 수 |
| Gold 구간 건수 | `gold_live_metrics`에서 `windowStart`가 일치하는 문서의 `total` |

## 8. 처리 규칙 (B·C)

QA의 정답 계산기(`qa/src/QaRunner/Pipeline/ExpectedResultCalculator.cs`)가 이 규칙으로 정답을 계산합니다. 규칙이 바뀌면 둘 다 고칩니다.

| 규칙 | 내용 |
|---|---|
| Bronze | 받은 메시지를 모두 보관한다. 재전송·중복도 각각 남긴다 |
| 중복 제거 | 같은 `eventId`는 Silver에 한 번만 남긴다. **먼저 도착한 것**을 기준으로 한다 |
| 늦은 도착 격리 | 받은 시각 − `occurredAt` > **5분**이면 Silver·Gold에 넣지 않고 격리한다 (`late_arrival`) |
| 같은 ID 상충 격리 | 이미 Silver에 있는 `eventId`인데 내용이 다르면, 뒤에 온 것을 격리한다 (`conflict`) |
| Gold 구간 | `occurredAt`을 UTC 기준 60초 단위로 내림한 시각이 구간 시작 |
| Gold 갱신 | 배치마다 영향받은 구간을 Silver에서 **다시 세어 절대값으로 upsert** (다시 실행해도 값이 같음) |
| 세션 판정 | 이벤트가 들어올 때마다 세션 특징을 갱신하고, 규칙 R1~R6 점수를 합쳐 판정 단계를 정한다. 판정이 바뀌면 `gold_verdicts`를 덮어쓰고 판정 토픽으로 알린다 |

### Spark 중단·재가동 (복구 시험)

QA 복구 시험은 Spark 판정 작업 컨테이너를 `docker stop` → `docker start`로 멈췄다 켭니다.

| 항목 | 값 |
|---|---|
| 컨테이너 이름 | `spark` (Silver·Gold를 만드는 판정 작업의 docker-compose 서비스 이름) |
| 재가동 후 기대 | checkpoint와 볼륨이 남아 있어서, 멈춘 동안 Kafka에 쌓인 이벤트부터 이어서 처리 |
| 합격 기준 | 재가동 후 회복 시간 60초 이하, 최종 집계가 보낸 건수와 같음 |
| QA 쪽 구현 | `qa/src/QaRunner/Pipeline/DockerProcessingControl.cs` |

## 9. 합격 기준 (QA로 측정)

| 구분 | 항목 | 기준 |
|---|---|---|
| 정확성 | Bronze·Silver·격리·Gold 건수 | QA 정답 계산값과 같음 |
| 부하 | 판정까지 걸린 시간 p95 / 오류율 | 3초 이하 / 0% |
| 부하 | Gold 반영 시간 p95 | 10초 이하 |
| 복구 | 재가동 후 회복 시간 | 60초 이하 |
| 탐지 | 봇 A·B·D 탐지율 / 봇 C 탐지율 | 90% 이상 / 70% 이상 (초기 목표) |
| 탐지 | 사람 오탐률 | 1% 이하 (초기 목표, 표본 수와 함께 보고) |

## 10. 정해야 할 것

QA 사례 생성과 팀 결과 공유의 후속 초안은 [QA 연결 스켈레톤](../qa/docs/collaboration-skeleton.md)에 별도로 정리합니다. 이 초안의 `/qa/runs`는 QA 결과 공유용이며, 위 서비스 API나 Kafka 이벤트 계약을 바꾸지 않습니다.

확정되면 체크하고, 위 본문을 고친 뒤 이 줄에 결정 내용을 짧게 적습니다.

- [ ] 판정 값 네 단계와 영문 코드 (2장) — B, A
- [ ] 예매 API 경로·필드, 결제 응답 상태 (3장) — B, A
- [ ] 결제 때 판정이 없으면 2초 기다린 뒤 추가 확인으로 처리할지 (3장) — B, A
- [ ] 이벤트 종류와 `origin`(client/server) 구분 (4장) — A, B
- [ ] QA 이벤트를 `source: "qa"`로 구분하고 `runId`·`caseId`를 넣는 것 (4장) — B, C
- [ ] 판정 조회 API 경로와 404 의미 (5장) — B, A
- [ ] 토픽 이름과 메시지 키 (6장) — C, B
- [ ] 메달리온 컬렉션 이름·고유 키·보관 기간 (7장) — C, B
- [ ] 판정 규칙 R1~R6 기준값과 단계별 점수 경계 (8장, 1주 차) — B, QA
- [ ] 탐지 목표값과 사람 테스트 표본 수 (9장) — QA, 전원
