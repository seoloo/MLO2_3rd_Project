# 콘솔 프로그램

[← QA 안내로 돌아가기](../README.md)

## 설치

```bash
winget install Microsoft.DotNet.SDK.10
```

터미널을 새로 열고 버전이 `10.0.`으로 시작하는지 확인합니다.

```bash
dotnet --version
```

## 실행

명령은 모두 `qa/` 폴더에서 실행합니다.

```bash
dotnet run --project src/QaRunner
```

```bash
dotnet run --project src/QaRunner -- --suite pipeline
```

지금은 일부러 엉성하게 만든 연습용 대상으로 돌기 때문에 실패가 나옵니다. 판정 시험은 세션 7개 중 봇 C·D 2개와 탐지율 2개(10개 중 4개), 파이프라인 시험은 5분 초과 도착·같은 ID 상충 관련 4개가 실패합니다. 채점기가 틀린 결과를 잡아내는지 보는 것입니다.

부하·복구 시험은 실행안 기준으로 기본 5분(부하), 2분(복구)이 걸립니다. 짧게 확인하려면 시간을 줄입니다.

```bash
dotnet run --project src/QaRunner -- --suite load --duration 10
```

```bash
dotnet run --project src/QaRunner -- --suite recovery --duration 20 --stop-seconds 5
```

QA 러너 자체의 테스트:

```bash
dotnet test
```

## 옵션

옵션은 `--` 뒤에 붙입니다. 생략하면 기본값을 씁니다.

| 옵션 | 뜻 | 기본값 |
|---|---|---|
| `--suite` | 시험 종류. `verdict`, `pipeline`, `load`, `recovery`, `selfcheck`, `mockapi` | `verdict` |
| `--cases` | 판정·부하 시험은 세션 사례 파일, 파이프라인 시험은 시나리오 폴더 (복구 시험은 안 씀) | `cases/sessions.json` / `cases/pipeline` |
| `--reports` | 보고서를 저장할 폴더 | `reports` |
| `--api-url` | 백엔드 API 주소. `http://` 또는 `https://`로 시작. QA가 뒤에 `/events`·`/verdicts`를 붙임 (판정·부하 시험) | 없음 (연습용 판정기) |
| `--duration` | 부하·복구 시험에서 이벤트를 보내는 시간(초). 가짜 예매·판정 서버는 켜 둘 시간 | 부하 300 / 복구 120 / 가짜 서버는 직접 끌 때까지 |
| `--rate` | 부하·복구 시험에서 1초에 보내는 이벤트 수 | 5 |
| `--stop-seconds` | 복구 시험에서 처리를 멈춰 두는 시간(초). 전체 시간의 1/4 지점에서 멈춤 | 30 |
| `--kafka` | 실제 Kafka 주소. `--mongo`와 함께 주면 실제 파이프라인을 대상으로 시험 | 없음 (가짜 파이프라인) |
| `--mongo` | 실제 MongoDB 연결 문자열. 비밀번호가 들어 있으면 셸 기록·CI 로그에 남으니 CI에서는 Secrets로 넣는다 (보고서에는 기록하지 않음, [AWS·EC2 보안 체크리스트](../../infra/README.md#awsec2-보안-체크리스트)) | 없음 |
| `--kafka-topic` | 이벤트를 넣을 토픽 | `ticket-events` |
| `--mongo-db` | 메달리온 컬렉션이 있는 분석 DB | `ticketguard` |
| `--port` | 가짜 예매·판정 서버를 열 포트 | 8000 |
| `--scenario` | 가짜 예매·판정 서버의 처음 응답 방식. `auto`, `normal`, `watch`, `verify`, `hold`, `queue`, `taken`, `soldout`, `expired`, `delayed`, `slow`, `timeout`, `broken`, `400`, `500`, `503` | `auto` |
| `--allow-lan` | 가짜 예매·판정 서버를 같은 Wi-Fi의 다른 기기에도 엶 (값 없이 씀) | 이 PC에서만 |
| `--spark-container` | 복구 시험에서 멈췄다 켤 Spark 판정 작업 컨테이너 | `spark` |

## 종료 코드

CI가 성공·실패를 판단할 때 씁니다.

| 종료 코드 | 뜻 |
|---|---|
| `0` | 모든 항목 통과 |
| `1` | 실패한 항목이 하나 이상 있음 (응답 없음 포함) |
| `2` | 채점을 시작하지 못함 (옵션·측정 숫자 오류, 사례 파일·시나리오 폴더 없음/형식 오류, API 주소 오류, 가짜 서버 포트 사용 중, 실제 MongoDB를 읽지 못함) |
