# 정보지킴 연결 계약 (초안)

앱·API·파이프라인·QA가 서로 주고받는 형식을 한곳에 모은 문서입니다. 각자 이 형식대로 만들면 따로 맞추지 않아도 연결됩니다.

- **상태: 초안.** 표의 값은 QA 러너가 지금 기대하는 형식입니다. 팀이 다르게 정하면 **이 문서를 먼저 고치고**, 각 영역 코드를 거기에 맞춥니다.
- 고칠 때는 PR로 올리고, 영향받는 담당(표의 "만드는 쪽"·"쓰는 쪽")에게 리뷰를 받습니다.
- 맨 아래 [정해야 할 것](#7-정해야-할-것) 목록을 하나씩 확정해 나갑니다.

## 1. 전체 흐름

```
앱(A) ──① 판정 요청──▶ API(B) ──② checks·outbox 저장──▶ MongoDB
                          │
                          └─③ 발행기(B) ──이벤트──▶ Kafka ──▶ Spark(C) ──④──▶ bronze / silver / 격리 / gold
QA ──①과 같은 요청──▶ API          QA ──③과 같은 이벤트──▶ Kafka       QA ──④를 읽어 정답과 비교
```

| 번호 | 연결 | 만드는 쪽 | 쓰는 쪽 |
|---|---|---|---|
| ① | 판정 API | B | A, QA |
| ② | `checks`, `outbox` 컬렉션 | B | B(발행기) |
| ③ | Kafka 이벤트 | B(발행기) | C, QA(직접 주입) |
| ④ | 메달리온 컬렉션 | C | 운영 화면(A), QA |

## 2. 판정 값

API 응답과 이벤트에서는 **영문 코드**를 쓰고, 화면에는 한글로 보여 줍니다.

| 코드 | 화면 표시 | 뜻 |
|---|---|---|
| `HIGH_RISK` | 고위험 | 인증번호 타인 전달, 외부 앱 설치 요구가 분명함 |
| `CAUTION` | 주의 | 위험 신호가 일부 있음 |
| `NO_SIGNAL` | 신호 미발견 | 위험 신호를 찾지 못함 |
| `UNDECIDED` | 판단 보류 | 정보가 부족해 판단하지 않음 |

시스템 실패(판정을 내리지 못함)는 판정 값이 아닙니다. API가 오류 응답(5xx)을 돌려주고, 이벤트도 만들지 않습니다.

## 3. 판정 API (B)

### 요청

```
POST /checks
Content-Type: application/json
```

```json
{ "message": "[택배] 상담원에게 인증번호를 알려주세요" }
```

| 필드 | 형식 | 필수 | 뜻 |
|---|---|---|---|
| `message` | 문자열 | 예 | 사용자가 공유하거나 붙여 넣은 문자 내용 |

### 응답 (200 OK)

```json
{
  "scanId": "6f1c2b8e-1d4a-4c51-9a3e-2f0b7c9d1e22",
  "verdict": "HIGH_RISK",
  "evidence": ["상담원에게 인증번호를 알려 달라는 요청이 있습니다"],
  "guidance": "전달을 멈추고, 직접 찾은 공식 앱이나 고객센터에서 확인하세요.",
  "checkedAt": "2026-11-02T09:00:10Z"
}
```

| 필드 | 형식 | 뜻 | QA가 쓰는지 |
|---|---|---|---|
| `scanId` | 문자열(UUID) | 검사 번호. 이벤트의 `scanId`와 같은 값 | 아니요 |
| `verdict` | 판정 코드 | [2. 판정 값](#2-판정-값) 중 하나 | **예** |
| `evidence` | 문자열 배열 | 판정 근거 (앱 결과 화면용) | 아니요 |
| `guidance` | 문자열 | 대응 안내 | 아니요 |
| `checkedAt` | ISO 8601 (UTC) | 판정한 시각 | 아니요 |

### 오류

| 상황 | 상태 코드 | 본문 |
|---|---|---|
| `message`가 비었거나 형식이 틀림 | 400 | `{ "error": "설명" }` |
| 서버 내부 오류, 판정 실패 | 500·503 | `{ "error": "설명" }` |

- QA는 10초 안에 응답이 없거나 오류 응답이 오면 그 케이스를 **시스템 실패**로 기록합니다.
- QA 쪽 구현: `qa/src/QaRunner/Verdicts/ApiVerdictService.cs`

## 4. Kafka 이벤트 (B 발행기 → C)

| 항목 | 값 |
|---|---|
| 토픽 | `scan-events` |
| 메시지 키 | `scanId` (같은 검사의 이벤트는 같은 파티션으로) |
| 메시지 값 | 아래 JSON (UTF-8) |
| 발행 시점 | `checks`와 `outbox`를 함께 저장한 뒤, 발행기가 `outbox`를 읽어 발행. Kafka 확인(ack)을 받으면 `outbox`에 전송 완료 표시 |
| 재전송 | 확인을 못 받아 다시 보낼 때는 **같은 `eventId`** 로 보냄 (중복 제거는 Silver가 함) |

```json
{
  "eventId": "0b9a4c6e-7f2d-4e1a-8c3b-5d6e7f8a9b0c",
  "scanId": "6f1c2b8e-1d4a-4c51-9a3e-2f0b7c9d1e22",
  "eventType": "check_completed",
  "verdict": "HIGH_RISK",
  "occurredAt": "2026-11-02T09:00:10Z",
  "source": "app",
  "runId": null,
  "caseId": null
}
```

| 필드 | 형식 | 뜻 |
|---|---|---|
| `eventId` | 문자열(UUID) | 이벤트 고유 번호. Silver 중복 제거 기준 |
| `scanId` | 문자열(UUID) | 검사 번호. 판정 API 응답의 `scanId` |
| `eventType` | 문자열 | 지금은 `check_completed` 하나 (검사 1건 = 이벤트 1건) |
| `verdict` | 판정 코드 | [2. 판정 값](#2-판정-값) 중 하나 |
| `occurredAt` | ISO 8601 (UTC) | 검사가 끝난 시각. **Gold 구간과 지연 판단의 기준** |
| `source` | 문자열 | `app`(실제 사용) 또는 `qa`(QA 러너가 넣은 시험 이벤트) |
| `runId` | 문자열 또는 null | QA 실행 번호. QA 이벤트에만 있음 |
| `caseId` | 문자열 또는 null | QA 시나리오 번호. QA 이벤트에만 있음 |

- **개인정보:** 이벤트에는 문자 원문, 전체 URL, 인증정보를 넣지 않습니다.
- QA 쪽 구현: `qa/src/QaRunner/Pipeline/KafkaEventSender.cs`

## 5. MongoDB 컬렉션

데이터베이스 이름: `jeongbo`

### 5-1. API 쪽 (B)

| 컬렉션 | 문서 하나 | 주요 필드 |
|---|---|---|
| `checks` | 검사 1건의 결과 | `_id`(= scanId), `verdict`, `evidence`, `guidance`, `checkedAt` |
| `outbox` | 발행할 이벤트 1건 | `_id`(= eventId), `payload`(이벤트 JSON), `status`(`pending`/`sent`), `createdAt`, `sentAt` |

### 5-2. 메달리온 (C)

| 컬렉션 | 문서 하나 | 고유 키(`_id`) | 주요 필드 |
|---|---|---|---|
| `bronze_events` | Kafka에서 받은 메시지 1건 (재전송 포함 전부) | `"{topic}:{partition}:{offset}"` | `topic`, `partition`, `offset`, `receivedAt`, `raw`(이벤트 JSON을 **문자열이 아닌 하위 문서로** 그대로) |
| `silver_events` | 중복을 없앤 이벤트 1건 | `eventId` | `scanId`, `verdict`, `occurredAt`, `source`, `runId`, `caseId`, `firstReceivedAt` |
| `quarantine_events` | 격리된 메시지 1건 | 자동(ObjectId) | `eventId`, `reason`(`late_arrival`/`conflict`), `receivedAt`, `raw`(하위 문서) |
| `gold_metrics` | 60초 구간 1개의 집계 | 구간 시작 시각 문자열 (예: `"2026-11-02T09:00:00Z"`) | `windowStart`(날짜), `total`, `byVerdict`(`{ "HIGH_RISK": 3, ... }`), `updatedAt` |

QA는 이 컬렉션을 아래처럼 읽습니다. 필드 이름이 바뀌면 `qa/src/QaRunner/Pipeline/MongoPipelineReader.cs`를 같이 고칩니다.

| QA가 확인하는 것 | 읽는 방법 |
|---|---|
| Bronze 보관 건수 | `bronze_events`에서 `raw.runId`, `raw.caseId`가 일치하는 문서 수 |
| Silver 고유 건수 | `silver_events`에서 `runId`, `caseId`가 일치하는 문서 수 |
| 격리 건수 | `quarantine_events`에서 `raw.runId`, `raw.caseId`가 일치하는 문서 수 |
| Gold 구간 건수 | `gold_metrics`에서 `windowStart`가 일치하는 문서의 `total` |

## 6. 처리 규칙 (C)

QA의 정답 계산기(`qa/src/QaRunner/Pipeline/ExpectedResultCalculator.cs`)가 이 규칙으로 정답을 계산합니다. 규칙이 바뀌면 둘 다 고칩니다.

| 규칙 | 내용 |
|---|---|
| Bronze | 받은 메시지를 모두 보관한다. 재전송·중복도 각각 남긴다 |
| 중복 제거 | 같은 `eventId`는 Silver에 한 번만 남긴다. **먼저 도착한 것**을 기준으로 한다 |
| 늦은 도착 격리 | 받은 시각 − `occurredAt` > **5분**이면 Silver·Gold에 넣지 않고 격리한다 (`late_arrival`) |
| 같은 ID 상충 격리 | 이미 Silver에 있는 `eventId`인데 내용이 다르면, 뒤에 온 것을 격리한다 (`conflict`) |
| Gold 구간 | `occurredAt`을 UTC 기준 60초 단위로 내림한 시각이 구간 시작 |
| Gold 갱신 | 배치마다 영향받은 구간을 Silver에서 **다시 세어 절대값으로 upsert** (다시 실행해도 값이 같음) |

### Spark 중단·재가동 (복구 시험)

QA 복구 시험은 Spark 컨테이너를 `docker stop` → `docker start`로 멈췄다 켭니다.

| 항목 | 값 |
|---|---|
| 컨테이너 이름 | `spark` (docker-compose 서비스 이름) |
| 재가동 후 기대 | checkpoint와 볼륨이 남아 있어서, 멈춘 동안 Kafka에 쌓인 이벤트부터 이어서 처리 |
| QA 쪽 구현 | `qa/src/QaRunner/Pipeline/DockerProcessingControl.cs` |

## 7. 정해야 할 것

QA 사례 생성과 팀 결과 공유의 후속 초안은 [QA 연결 스켈레톤](../qa/docs/collaboration-skeleton.md)에 별도로 정리합니다. 이 초안의 `/qa/runs`는 QA 결과 공유용이며, 위 서비스 판정 API `/checks`나 Kafka 이벤트 계약을 변경하지 않습니다.

확정되면 체크하고, 위 본문을 고친 뒤 이 줄에 결정 내용을 짧게 적습니다.

- [ ] 판정 값을 영문 코드로 주고받는 것 (2장) — B, A
- [ ] 판정 API 경로·필드 (3장) — B, A
- [ ] 검사 1건 = 이벤트 1건 (`eventType` 하나)으로 충분한지 — B, C
- [ ] QA 이벤트를 `source: "qa"`로 구분하고 `runId`·`caseId`를 넣는 것 (4장) — C
- [ ] 메달리온 컬렉션 이름과 고유 키 (5-2장) — C
- [ ] 같은 ID 상충 때 먼저 온 것을 남길지 (6장) — C
- [ ] Gold 분모: 판단 보류를 집계 분모에 넣을지 — C, A
- [ ] `checks`에 문자 원문을 저장할지, 저장한다면 보관 기간 (5-1장) — B
- [ ] 운영 화면(A)이 Gold와 마지막 성공 시각을 어떻게 읽을지 — A, C
