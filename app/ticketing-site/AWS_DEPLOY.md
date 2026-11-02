# 티켓가드 · AWS 공용 예매 대상 서비스

## 역할

이 Django 서비스는 프로젝트의 사람이 직접 예매하거나 허가된 QA 클라이언트가 접속하는 대상 사이트입니다. 고객 화면은 공연·계정·좌석·예매 흐름으로 유지하고, 기록은 관리자 /ops/와 /admin/에서 따로 확인합니다. 이미지의 React Native·FastAPI·Redis·Kafka·Spark 구조를 대체하거나 모두 구현했다고 주장하지 않습니다. 현재 예매 대상은 Django/PostgreSQL, 이미지의 수집·판정 파이프라인은 팀이 별도로 연결할 시스템입니다.

## 접속 구조

팀원 브라우저/QA → EC2 HTTP 80 → Nginx → Gunicorn/Django 8000(내부) → PostgreSQL(내부)

독립 설정 파일 `compose.aws.yaml`을 사용합니다. 로컬 전용 `compose.yaml`과 병합하지 않습니다. 고객에게는 EC2 공인 IP 또는 도메인만 공유합니다. `127.0.0.1` 링크는 팀원에게 공유하지 않습니다. 공인 주소는 EC2 재시작에 따라 바뀔 수 있어 유지가 필요하면 Elastic IP/도메인을 검토합니다.

## 인프라 담당 실행 절차

1. Docker Engine과 Compose가 있는 EC2 Linux에 이 디렉터리 전체를 전달합니다. 기존 서비스가 쓰는 서버를 임의로 대체하지 않습니다.
2. `.env.aws.example`을 `.env.aws`로 복사하고 서로 다른 무작위 `DJANGO_SECRET_KEY`, `POSTGRES_PASSWORD`를 입력합니다. 예시 문구 그대로 사용하지 않습니다.
3. `DJANGO_ALLOWED_HOSTS`에는 실제 공인 IP/도메인(스킴·포트 제외), `DJANGO_CSRF_TRUSTED_ORIGINS`에는 실제 `http://공인주소`를 넣습니다. 임의의 전체 호스트 허용은 사용하지 않습니다.
4. `docker compose --env-file .env.aws -f compose.aws.yaml config --quiet`
5. `docker compose --env-file .env.aws -f compose.aws.yaml up --build -d`
6. `docker compose --env-file .env.aws -f compose.aws.yaml exec web python manage.py setup_admin --reset-password`로 공통 관리자 `admin01`을 준비하고 팀 공통 비밀번호를 숨김 입력합니다.
7. EC2 보안 그룹 HTTP 80의 수신 출처에 팀원/QA의 실제 공인 IP 범위를 허용합니다. SSH 22는 관리 출처로 제한합니다. Django 8000과 PostgreSQL 5432는 외부에 열지 않습니다.
8. 팀원은 `http://공인주소/`에 접속해 가입·예매합니다. `/healthz/`가 200이고, 다른 계정의 동시 좌석 요청이 충돌하는지 확인합니다.

현재 AWS 프로필은 EC2 IP로 접속하기 위한 HTTP 구성입니다. HTTPS와 인증서는 포함하지 않습니다. 실제 개인정보/결제를 연결하는 공개 서비스로 전환할 때는 도메인·TLS를 적용하고 보안 쿠키·SSL 리다이렉트를 활성화해야 합니다. ALB/CloudFront가 앞에 추가되면 아래 프록시 신뢰 경계를 다시 설계해야 합니다.

## 반복 실행과 기록

- 상태: `docker compose --env-file .env.aws -f compose.aws.yaml ps`
- 웹 출력: `docker compose --env-file .env.aws -f compose.aws.yaml logs -f web`
- Nginx 출력: `docker compose --env-file .env.aws -f compose.aws.yaml logs gateway`
- Nginx 요청 파일: `docker compose --env-file .env.aws -f compose.aws.yaml exec gateway tail -n 100 /var/log/nginx/access.jsonl`
- 앱 기록: `/ops/`(관리자), 컨테이너 `/app/logs/requests-프로세스ID.jsonl`
- 코드 변경 후 재구성: `docker compose --env-file .env.aws -f compose.aws.yaml up --build -d`
- 일시 중지/재시작: 같은 접두 명령의 `stop` / `start`

DB와 요청 파일은 이름 있는 볼륨에 유지됩니다. `down -v`는 삭제이므로 장애 복구/일반 재시작에 사용하지 않습니다. 마이그레이션은 실행 시 적용되고 공연 생성은 멱등입니다. 이전 릴리스로 코드를 되돌릴 때 DB 스키마 호환성을 별도로 확인해야 합니다. 파일 로그에 장애 복구 능력이 생겼다고 전체 디스크/호스트 장애에도 안전한 것은 아닙니다. S3 보관·외부 수집·모니터링은 인프라 후속 작업입니다.

## 프록시와 IP

Nginx는 방문자가 보낸 X-Real-IP/X-Forwarded-For/X-Request-ID를 덮어씁니다. AWS 전용 구성에서만 `DJANGO_TRUST_PROXY_HEADERS=1`이며, 외부에 직접 노출하지 않은 Django가 프록시의 IP를 HMAC 가명값으로 변환합니다. 로컬 프로필에서는 전달 헤더를 무시합니다. 동일 공유망의 여러 사용자는 같은 공인 IP일 수 있습니다.

Nginx와 Django의 request_id를 연결하여 앱의 4xx/5xx뿐 아니라 upstream 장애의 502도 별도로 확인합니다. 요청 ID가 같다고 정확히 같은 성능 측정 구간은 아닙니다. Nginx는 초, Django는 ms를 사용합니다. Nginx 요청 파일은 영속 볼륨에 저장하지만 자동 회전은 이번 구성에 포함하지 않았으므로 장시간 부하 시험 전 용량 관리가 필요합니다. 앱 요청 파일은 프로세스별 10MB/5회 회전입니다.

## 접근·시험 범위

자체 EC2/허가된 대상만 시험합니다. 탐지/방어 규칙은 아직 없으며, 정상 Django 인증·CSRF·좌석 원자성은 유지합니다. 시험 목적으로 일부러 취약한 인증을 만들거나 가짜 판정을 표시하지 않습니다. 실제 결제는 승인 시뮬레이션이며 최종 화면에서 청구 없음/입장권 효력 없음을 알립니다.

## 확인 상태

로컬 Django 기능·전달 헤더 신뢰·거부 응답 추적·헬스 체크 시험과 Compose 문법 검증을 수행했습니다. Docker 엔진/실제 EC2가 연결되어 있지 않아 Nginx·Gunicorn·PostgreSQL 컨테이너 실행, 실제 공인 IP·보안 그룹·HTTPS는 미검증입니다. 실제 AWS 배포는 수행하지 않았습니다.

공식 참고: https://nginx.org/en/docs/http/ngx_http_proxy_module.html · https://docs.djangoproject.com/en/5.2/ref/settings/ · https://docs.aws.amazon.com/AWSEC2/latest/UserGuide/security-group-rules-reference.html
