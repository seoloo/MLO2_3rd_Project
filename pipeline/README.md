# pipeline — Kafka · Spark · 메달리온 (담당: B · C)

Kafka의 `ticket-events`를 Spark 구조적 스트리밍으로 읽어 MongoDB 분석 DB의 Bronze → Silver → Gold로 정리하고, 세션마다 특징을 계산해 판정하는 곳입니다.

| 작업 | 하는 일 | 담당 |
|---|---|---|
| Spark 수집 | Kafka 원본을 그대로 `bronze_events`에 보관 (7일 뒤 S3 일별 파일) | C (인프라) |
| Spark 판정 | 중복·순서·지연 정리 → 세션 특징 → 규칙 R1~R6 점수 → 판정, 60초 구간 실시간 집계 | B (백엔드) |
| 배치 집계 | 판매 종료 후 회차별 기획사 리포트 | C (인프라) |

## 여기에 둘 것

- Spark 작업 코드 (수집, 판정, 배치 집계)
- 판정 규칙 기준값 파일과 버전 (`ruleVersion`)
- 이 폴더만의 실행 방법과 테스트

Kafka·Spark·MongoDB·Redis·API를 함께 띄우는 docker-compose는 [infra/](../infra/README.md)에 둡니다.

## 지켜야 할 계약

[docs/contract.md](../docs/contract.md)의 아래 장을 따릅니다. 바꾸고 싶으면 계약 문서를 먼저 고쳐 PR로 올립니다.

| 장 | 내용 |
|---|---|
| 4. 행동 이벤트 | 읽을 메시지 형식과 `origin`(client/server). `source: "qa"`인 QA 시험 이벤트도 똑같이 처리 |
| 6. Kafka | 토픽 `ticket-events`, 키 `sessionHash` (같은 세션은 같은 파티션) |
| 7-2. 메달리온 컬렉션 | `bronze_events`, `quarantine_events`, `silver_events`, `silver_session_features`, `gold_verdicts`, `gold_live_metrics`, `gold_reports`와 고유 키 |
| 8. 처리 규칙 | 중복 제거, 5분 초과 격리, 같은 ID 상충 격리, 60초 구간 절대값 upsert, 판정이 바뀌면 `gold_verdicts` 덮어쓰기 |
| 8. Spark 중단·재가동 | 판정 작업 컨테이너 이름 `spark`, 재가동하면 checkpoint부터 이어서 처리 |

## QA로 확인하는 법

QA 러너는 Kafka에 시험 이벤트를 직접 넣고, MongoDB를 읽어 정답 계산기의 정답과 비교합니다. 다른 데이터가 섞이지 않는 시험 환경에서 돌립니다. 명령은 `qa/` 폴더에서 실행합니다.

정확성 (중복·역순·30초 지연·6분 지연·같은 ID 상충):

```bash
dotnet run --project src/QaRunner -- --suite pipeline --kafka localhost:9092 --mongo mongodb://localhost:27017
```

부하 (초당 5건, Gold 반영 p95 10초 목표):

```bash
dotnet run --project src/QaRunner -- --suite load --kafka localhost:9092 --mongo mongodb://localhost:27017
```

복구 (Spark 판정 작업 컨테이너를 30초 멈췄다 재가동, 60초 안에 따라잡기):

```bash
dotnet run --project src/QaRunner -- --suite recovery --kafka localhost:9092 --mongo mongodb://localhost:27017 --spark-container spark
```

판정까지 보려면 백엔드 API도 켜고 판정 시험(`--suite verdict --api-url ...`)을 함께 돌립니다. 토픽·DB·컨테이너 이름이 다르면 옵션으로 바꿀 수 있습니다. 자세한 옵션은 [qa/README.md](../qa/README.md)에 있습니다.

## 작업 브랜치

`main`에서 `feature/pipeline-...` 브랜치를 만들어 작업하고 PR로 합칩니다. 예: `feature/pipeline-judge`
