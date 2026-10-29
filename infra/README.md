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

## AWS·EC2 보안 체크리스트

EC2에 올리기 전에 한 번씩 확인합니다. QA 러너는 AWS 키를 쓰지 않아서 프로그램 자체로 계정이 털리지는 않지만, 키 유출·포트 공개·과한 권한 같은 운영 실수로는 털릴 수 있습니다. 털리면 대개 채굴 서버가 켜져 큰 요금이 나옵니다.

### 계정

- [ ] 루트 계정에 MFA를 켜고, 루트 액세스 키는 만들지 않는다
- [ ] 팀원마다 IAM 사용자를 따로 만들고 각자 MFA를 켠다. 계정·비밀번호를 돌려 쓰지 않는다
- [ ] AWS Budgets로 결제 알림을 건다 (예: 월 $10 넘으면 메일). 털렸을 때 가장 빨리 알아채는 방법
- [ ] 다 쓴 인스턴스·볼륨·탄력적 IP는 지운다. 켜 둔 채로 잊은 자원이 요금과 공격 대상이 된다

### 키와 비밀번호

- [ ] AWS 액세스 키·MongoDB 비밀번호·`.pem`을 코드, README, 단톡방, 이슈에 붙이지 않는다. 공개 저장소의 키는 봇이 금방 찾아간다
- [ ] 로컬 비밀 값은 `.env`에 두고, 저장소에는 값을 비운 `.env.example`만 올린다 (`.env`, `*.pem`, `*.key`, `credentials`는 `.gitignore`에 들어 있음)
- [ ] EC2 안에서는 액세스 키 대신 **인스턴스 역할(IAM Role)** 을 쓰고, 필요한 권한만 준다 (`AdministratorAccess` 금지)
- [ ] GitHub Actions에서 AWS에 접근하면 키를 Secrets에 넣고, 가능하면 OIDC로 임시 자격 증명을 받는다
- [ ] 저장소 설정에서 Secret scanning과 push protection을 켜서 키가 담긴 커밋은 푸시 단계에서 막는다
- [ ] 키가 새었다고 의심되면 바로 IAM에서 비활성화·삭제하고, CloudTrail과 결제 화면에서 낯선 리전·인스턴스를 확인한다

### 네트워크

- [ ] 보안 그룹에서 MongoDB(27017), Kafka(9092), Spark UI(8080)를 `0.0.0.0/0`으로 열지 않는다. compose 내부 네트워크로만 통신한다
- [ ] 외부에 여는 포트는 API(HTTPS)뿐이고, SSH(22)는 팀원 IP로만 열거나 Session Manager로 대신한다
- [ ] MongoDB는 인증을 켜고 앱 전용 사용자로 접속한다. 인증 없이 열린 MongoDB는 데이터 삭제·몸값 요구 공격을 자주 당한다
- [ ] 인스턴스 메타데이터는 IMDSv2만 허용한다. 서버가 남이 준 주소로 요청을 보내게 되는 경우(SSRF) 역할 자격 증명이 새는 것을 막는다

### QA를 EC2에 붙일 때

- [ ] `--mongo`에 비밀번호가 든 연결 문자열을 넘기면 셸 기록·프로세스 목록·CI 로그에 남는다. CI에서는 Secrets로 넣는다. QA 보고서에는 연결 문자열이 기록되지 않는다
- [ ] QA 공유 API(`qa/src/QaRunner.Sharing.Api`)는 지금 인증 없이 `127.0.0.1`에서만 열린다. EC2에서 외부로 열기 전에 HTTPS·인증·요청 제한을 먼저 구현한다
- [ ] 부하 시험은 우리 서버에만 건다. Auto Scaling을 켜 둔 상태라면 상한을 정해 요금이 불어나지 않게 한다

## QA를 붙이는 법

compose로 시험 환경을 띄운 뒤, QA 러너에 주소만 넘기면 됩니다. 예시는 [pipeline/README.md](../pipeline/README.md)의 "QA로 확인하는 법"에 있습니다. 나중에 compose에 `qa` 서비스를 추가해 `docker compose run --rm qa`로 돌리는 것도 이 폴더에서 합니다.

## 작업 브랜치

`main`에서 `feature/infra-...` 브랜치를 만들어 작업하고 PR로 합칩니다. 예: `feature/infra-compose`
