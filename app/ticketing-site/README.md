# 티켓가드 · Django 예매 서비스

## 로컬 실행 (Windows)

이 폴더에서 `./start-local.ps1`을 실행하면 가상환경 준비, 의존성 설치, 마이그레이션, 공연·좌석 생성 후 http://127.0.0.1:8000 으로 실행합니다. 가상환경은 `.venv`이며 Python 3.12 기준으로 검증했습니다.

이미 준비된 환경은 `.venv/Scripts/python.exe manage.py runserver 127.0.0.1:8000`으로 실행합니다. 필요하면 `.venv/Scripts/Activate.ps1`로 활성화할 수 있습니다. Django 개발 서버는 배포용 서버가 아닙니다.

## 관리자 설정과 기록

관리자 계정은 기본 비밀번호로 자동 생성하지 않습니다. 별도 터미널에서 `.venv/Scripts/python.exe manage.py createsuperuser`를 실행하여 직접 아이디와 비밀번호를 정합니다.

- 고객 사이트: http://127.0.0.1:8000/
- Django 관리: http://127.0.0.1:8000/admin/
- 관리자 기록: http://127.0.0.1:8000/ops/
- 관리자 이벤트 내보내기: http://127.0.0.1:8000/ops/export/ (최대 10,000건)
- 요청 파일: `logs/requests-프로세스ID.jsonl`, 10MB 기준 5개 회전 파일

일반 고객은 로그·내보내기에 접근할 수 없습니다. 운영 화면은 최신 이벤트 200건과 요청 파일 최근 200줄을 보여 줍니다. IP는 Django 비밀키를 사용하는 HMAC 가명값으로 저장하며 익명정보로 취급하지 않습니다. 프록시의 임의 X-Forwarded-For 헤더는 신뢰하지 않습니다. 프록시 배포 시 검증된 원천 IP 전달을 별도 설정해야 합니다. 쿼리 문자열·비밀번호·요청 본문은 요청 로그에 남기지 않습니다.

클라이언트는 로그인 후 1초마다 클릭·화면 이동·가시성 이벤트를 보냅니다. 중복 event ID는 한 번만 저장합니다. pagehide는 keepalive 전송을 시도하나 종료 직전 이벤트의 완전한 보존은 보장하지 않습니다. DB 오류 시 서버 요청 파일은 예매 DB와 독립되어 남도록 구성했습니다. 같은 머신·디스크 장애에는 함께 영향을 받으므로 실제 관제는 외부 수집기로 확장해야 합니다.

## 예매 기능

가입/로그인 → 공연 → 3초 입장 준비 → 좌석 → 5분 확보 → 승인 시뮬레이션 → 예매 완료 → 예매 내역/취소. 공연별 96석과 회원별 최대 4매를 제공합니다. 확보 중 수량도 구매 한도에 포함합니다. 중복 예약 키와 중복 완료 요청은 중복 예매를 만들지 않습니다. 고객 화면에는 테스트 배너·로그 메뉴가 없으며, 최종 승인 단계에서 실제 청구와 입장권 효력이 없음을 안내합니다. 실제 PG 결제는 연결하지 않았습니다.

대기는 실제 부하 순위가 아니라 고정 3초 게이트입니다. 매크로 탐지/추가 인증/Kafka/Spark/MongoDB 분석은 아직 연결하지 않았습니다. Django의 일반 CSRF/회원권한/서버 예약 검증은 유지합니다. 의도적인 취약점은 넣지 않았습니다.

## Docker 격리 실행 · PostgreSQL

`python -c "import secrets; print(secrets.token_urlsafe(64))"`로 비밀값을 각각 생성하고 `.env.example`을 `.env`로 복사해 `DJANGO_SECRET_KEY`, `POSTGRES_PASSWORD`를 서로 다른 값으로 채웁니다. `.env`는 Git과 Docker 이미지에 포함하지 않습니다.

- `docker compose config --quiet`: 설정 검증
- `docker compose up --build -d`: Gunicorn + PostgreSQL 실행
- `docker compose exec web python manage.py createsuperuser`: 관리자 생성
- `docker compose exec web python manage.py test booking`: 컨테이너 기능 검증
- `docker compose logs -f web`: 서버 출력 확인
- `docker compose stop` / `docker compose start`: 데이터 유지 중지·재시작

기본 포트는 `127.0.0.1:8000`에만 바인딩됩니다. PostgreSQL과 요청 로그는 이름 있는 볼륨에 남습니다. `docker compose down -v`는 데이터 삭제이므로 일반 재시작에 사용하지 않습니다. 별도 데이터 초기화 명령은 제공하지 않습니다. 컨테이너는 비루트 사용자로 실행합니다.

SQLite는 한 프로세스 개발용이며 IMMEDIATE 트랜잭션으로 직렬화합니다. 동시 부하 평가는 Docker PostgreSQL에서 수행하세요. 공연 행을 `select_for_update()`로 잠근 뒤 점유·수량·주문을 한 트랜잭션으로 처리합니다.

## 서버 배포 준비

Dockerfile과 Gunicorn 진입점을 사용합니다. 배포 대상에 HTTPS 프록시를 두고 도메인에 맞게 `DJANGO_ALLOWED_HOSTS`, `DJANGO_CSRF_TRUSTED_ORIGINS`를 설정하며 `DJANGO_COOKIE_SECURE=1`, `DJANGO_SSL_REDIRECT=1`로 바꿉니다. 프록시 뒤 HTTPS 판정은 신뢰 경계를 확인한 후 설정해야 합니다. 필요할 때 HSTS를 적용하고 `manage.py check --deploy`를 실행합니다. 현재 Compose의 보안 쿠키/리다이렉트 0은 로컬 HTTP용 설정입니다. SQLite·비밀키·가상환경·로그를 배포 이미지에 복사하지 않습니다.

외부 배포는 수행하지 않았습니다. 이전 Sites 주소는 이 Django 앱과 별개이며 자동 갱신·동기화되지 않습니다. GitHub나 모아에도 업로드하지 않았습니다.

## 반복 검증

`python manage.py test booking`으로 구매 한도·점유 충돌·중복 요청·만료·취소·타인 주문 접근·CSRF·관리자 권한·화면 범위를 확인합니다. 실제 부하/공격 도구는 소유한 로컬/허가된 인스턴스만 대상으로 지정하고, 실행 ID·규칙 버전·입력 정답은 QA 측에서 따로 보관하세요. 프로젝트 종료 시 로컬 DB·이벤트·회전 요청 파일·Docker 볼륨의 보존/삭제를 함께 관리해야 합니다.

## 이번 환경 검증 결과

Django 5.2.18 · Python 3.12 가상환경에서 8개 Django 테스트와 Edge 브라우저의 회원가입→로그인 상태 예매→취소 흐름을 통과했습니다. 브라우저 JavaScript 오류는 없었고, 390px 모바일 화면의 가로 넘침도 없었습니다. Docker Compose 문법은 검사했으나 Docker Desktop 엔진이 실행되어 있지 않아 실제 컨테이너와 PostgreSQL 부하 동작은 미검증입니다.

## AWS 공용 사이트

AWS EC2 배포 담당자는 `AWS_DEPLOY.md`와 독립 `compose.aws.yaml`을 사용하세요. 원천 이벤트 연결 규격은 `EVENT_CONTRACT.md`, 이미지 기준 일정은 프로젝트 `docs/티켓가드_일정과_서비스역할.md`에 정리했습니다. 이 사이트는 예매 대상이며 FastAPI·Kafka·Spark 분석 계층의 대체 구현이 아닙니다.
