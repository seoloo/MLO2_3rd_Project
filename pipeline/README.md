# pipeline — Kafka · Spark · 메달리온 (담당: C)

Kafka의 `scan-events`를 Spark로 읽어 MongoDB의 Bronze → Silver → Gold로 정리·집계하는 파이프라인입니다.

## 여기에 둘 것

- Spark 작업 코드 (Kafka 읽기, 정제·중복 제거·격리, 60초 구간 집계)
- 이 폴더만의 실행 방법과 테스트

Kafka·Spark·MongoDB를 함께 띄우는 docker-compose는 [infra/](../infra/README.md)에 둡니다.

## 지켜야 할 계약

[docs/contract.md](../docs/contract.md)의 아래 장을 따릅니다. 바꾸고 싶으면 계약 문서를 먼저 고쳐 PR로 올립니다.

| 장 | 내용 |
|---|---|
| 4. Kafka 이벤트 | 읽을 메시지 형식. `source: "qa"`인 QA 시험 이벤트도 똑같이 처리 |
| 5-2. 메달리온 컬렉션 | `bronze_events`, `silver_events`, `quarantine_events`, `gold_metrics`와 고유 키 |
| 6. 처리 규칙 | 중복 제거, 5분 초과 격리, 같은 ID 상충 격리, 60초 구간 절대값 upsert |
| 6. Spark 중단·재가동 | 컨테이너 이름 `spark`, 재가동하면 checkpoint부터 이어서 처리 |

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

복구 (Spark 컨테이너를 30초 멈췄다 재가동, 60초 안에 따라잡기):

```bash
dotnet run --project src/QaRunner -- --suite recovery --kafka localhost:9092 --mongo mongodb://localhost:27017 --spark-container spark
```

주소·컬렉션 이름이 다르면 옵션으로 바꿀 수 있습니다. 자세한 옵션은 [qa/README.md](../qa/README.md)에 있습니다.

## 작업 브랜치

`main`에서 `feature/pipeline-...` 브랜치를 만들어 작업하고 PR로 합칩니다. 예: `feature/pipeline-bronze`
