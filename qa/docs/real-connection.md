# 실제 시스템에 연결하기

[← QA 안내로 돌아가기](../README.md)

형식은 모두 [docs/contract.md](../../docs/contract.md)(연결 계약 초안)를 따릅니다. 옵션을 주지 않으면 가짜 대상으로, 주면 실제 시스템으로 시험합니다.

| 시험 | 실제 대상 | 옵션 |
|---|---|---|
| 판정 | B의 백엔드 (수집 API + 판정 조회 API) | `--api-url http://localhost:8000` (데스크탑 앱은 조건 시트의 ‘내 판정 API’) |
| 파이프라인 | Kafka + MongoDB | `--kafka localhost:9092 --mongo mongodb://localhost:27017` |
| 부하 | 판정 부하는 `--api-url`, 스트림 부하는 `--kafka`·`--mongo` | 둘을 따로 또는 함께 |
| 복구 | Kafka + MongoDB + Spark 판정 작업 컨테이너 | `--kafka`·`--mongo` (+ `--spark-container`, 기본 `spark`) |

## 수집 API와 판정 조회 API (계약 4·5장)

| 구분 | 내용 |
|---|---|
| 보내기 | `POST {API 주소}/events`, 본문 `{ "events": [ ... ] }`. 이벤트는 계약 4-1장 형식(`origin`, `source: "qa"`, `runId`, `caseId` 포함), 202를 기대 |
| 받기 | `GET {API 주소}/verdicts/{세션 해시}`를 0.5초마다. 404는 "판정 전", 200이면 본문의 `verdict`만 읽어 영문 코드(`VERIFY`)를 한글(`추가 확인`)로 바꿔 비교 |
| 시간 제한 | 요청 하나 10초, 판정이 나오기까지 10초 |

API가 꺼져 있거나, 오류 응답(4xx·5xx, 판정 조회의 404 제외)이 오거나, 10초 안에 판정이 나오지 않거나, 응답에 `verdict`가 없으면 그 사례는 **응답 없음(시스템 실패)**으로 기록하고 다음 사례로 넘어갑니다. 행위자와 정답은 API에 보내지 않습니다.

## Kafka·MongoDB·Spark (계약 6~8장)

| 단계 | QA가 하는 일 |
|---|---|
| 보내기 | 시나리오 이벤트를 계약 형식(`origin`, `deviceHash`, `showId: "qa-show"`, `source: "qa"`, `runId`, `caseId` 포함)으로 `ticket-events` 토픽에 발행, 키는 `sessionHash`. 이벤트 번호 앞에 실행 번호를 붙여 실행끼리 겹치지 않게 함 |
| 읽기 | `bronze_events`·`silver_events`·`quarantine_events`에서 이번 실행·시나리오 건수, `gold_live_metrics`에서 구간별 `total`을 읽음 |
| 멈추고 켜기 | 복구 시험에서 `docker stop spark` → `docker start spark` |

- 실제 파이프라인 시험은 **다른 데이터가 섞이지 않는 시험 환경**에서 돌립니다. Gold는 구간별로 모든 이벤트를 합치기 때문입니다.
- Kafka에 보내지 못하면 그 시나리오를 시스템 실패로 기록합니다. MongoDB를 읽지 못하면 채점 자체가 안 되므로 종료 코드 2로 끝납니다.
