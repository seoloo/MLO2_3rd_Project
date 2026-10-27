# api — 판정 API (담당: B)

FastAPI로 문자 판정 규칙을 실행하고 결과를 바로 돌려주는 서버입니다. 판정 결과는 `checks`에, 발행할 이벤트는 `outbox`에 함께 저장하고, 발행기가 Kafka로 보냅니다.

## 여기에 둘 것

- FastAPI 앱, 판정 규칙(핵심 규칙 4~6개), `checks`·`outbox` 저장, 발행기
- 이 폴더만의 실행 방법과 테스트

## 지켜야 할 계약

[docs/contract.md](../docs/contract.md)의 아래 장을 따릅니다. 바꾸고 싶으면 계약 문서를 먼저 고쳐 PR로 올립니다.

| 장 | 내용 |
|---|---|
| 2. 판정 값 | `HIGH_RISK` / `CAUTION` / `NO_SIGNAL` / `UNDECIDED` |
| 3. 판정 API | `POST /checks`, 요청 `message`, 응답 `scanId`·`verdict`·`evidence`·`guidance`·`checkedAt` |
| 4. Kafka 이벤트 | 토픽 `scan-events`, 키 `scanId`, 재전송은 같은 `eventId` |
| 5-1. API 쪽 컬렉션 | `checks`, `outbox` |

## QA로 확인하는 법

API를 켜 둔 상태에서 QA 러너를 실행하면, `qa/cases/rules.json`의 문자를 API에 보내 정답과 비교합니다.

- 데스크탑 앱: `qa/run-qa-desktop.cmd` 실행 → **판정 시험** 선택 → **판정 API 주소**에 `http://localhost:8000` → 실행
- 명령어 (`qa/` 폴더에서):

```bash
dotnet run --project src/QaRunner -- --suite verdict --api-url http://localhost:8000
```

부하(동시 5명, p95 3초 목표)도 같은 주소로 확인할 수 있습니다.

```bash
dotnet run --project src/QaRunner -- --suite load --duration 60 --api-url http://localhost:8000
```

## 작업 브랜치

`main`에서 `feature/api-...` 브랜치를 만들어 작업하고 PR로 합칩니다. 예: `feature/api-checks`
