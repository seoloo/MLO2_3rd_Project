# infra — 실행 환경 (공용)

Kafka, Spark, MongoDB, API를 한 번에 띄우는 docker-compose와 EC2 배포 설정을 둡니다. 파이프라인은 C, 배포는 B가 주로 맡습니다.

## 여기에 둘 것

- `docker-compose.yml` (Kafka, Spark, MongoDB, API)
- EC2 배포 스크립트·설정

## 지켜야 할 것

| 항목 | 값 | 이유 |
|---|---|---|
| MongoDB 레플리카셋 | 단일 노드라도 레플리카셋으로 실행 | `checks`와 `outbox`를 한 트랜잭션으로 저장하려면 필요 |
| Spark 서비스 이름 | `spark` | QA 복구 시험이 `docker stop spark` / `docker start spark`로 멈췄다 켬 ([계약 6장](../docs/contract.md#6-처리-규칙-c)) |
| Spark checkpoint·MongoDB 데이터 | 볼륨에 저장 | 재가동 후 이어서 처리하고 최종 집계가 일치해야 함 |
| Kafka 토픽 | `scan-events` | [계약 4장](../docs/contract.md#4-kafka-이벤트-b-발행기--c) |

## QA를 붙이는 법

compose로 시험 환경을 띄운 뒤, QA 러너에 주소만 넘기면 됩니다. 예시는 [pipeline/README.md](../pipeline/README.md)의 "QA로 확인하는 법"에 있습니다. 나중에 compose에 `qa` 서비스를 추가해 `docker compose run --rm qa`로 돌리는 것도 이 폴더에서 합니다.

## 작업 브랜치

`main`에서 `feature/infra-...` 브랜치를 만들어 작업하고 PR로 합칩니다. 예: `feature/infra-compose`
