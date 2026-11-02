# 예매 대상 서비스 이벤트 계약 · schema_version 1

## 데이터 경계

현재 Django는 예매 대상이자 수집 원천입니다. Kafka/Redis/Spark/MongoDB/판정·알림은 미연결입니다. 소스가 client인 이벤트는 비신뢰 입력이며 서버의 주문/좌석 검증을 대체하지 않습니다. QA 정답은 이 이벤트에 넣지 않습니다.

## 클라이언트 수집

POST /api/events/ · 로그인 쿠키·CSRF 필요 · 한 번에 최대 50개 · 1초마다 묶음 전송 · 중복 UUID는 한 번 저장.

필드: id(UUID), type(click/page_view/visibility/seat_select), at(발생시각 Unix ms), target(안정적 버튼/좌석 ID), path(화면 경로), x/y(뷰포트 정규화 0~1000), sequence(문서 내 순번). 현재 sequence는 페이지 이동 시 다시 시작하므로 세션 전체 절대 순서로 사용하지 않습니다.

저장 시 서버가 user/session/source/received_at/schema_version/run_id/ingest_request_id를 부여합니다. 재생 시 event ID 중복 정책과 발생/수집시각을 분리해야 합니다.

## 서버 원천

서버 이벤트: signup, queue_enter, queue_bypass_rejected, seat_hold, reservation_rejected, booking_confirmed, checkout_rejected, booking_cancelled. 저장된 data에 request_id/schema_version/run_id가 있습니다.

요청 파일: schema_version, run_id, at, request_id, path, method, status, duration_ms, user_id, session, ip_tag. 비밀번호·본문·쿼리는 기록하지 않습니다. TG_RUN_ID는 배포 환경에서 지정하며, 외부 클라이언트 헤더를 그대로 믿지 않습니다.

게이트웨이 파일: source=edge, request_id, at, method, path, status, duration_seconds, upstream_status. 앱 중단 시에도 프록시가 응답한 결과를 확인할 별도 원천입니다.

## 향후 전달

인프라 담당이 승인된 DB/파일 수집 경로를 Kafka에 연결합니다. 원천 보관·재시도·수신 확인·격리·S3 보관은 후속 계약이며 지금 원격 Kafka 전송 성공을 주장하지 않습니다. 관리자 export는 최신/전체 무제한 수집기가 아니며 최대 10,000건 수동 내보내기입니다. 요청 ID·회원·세션·공연·주문·발생/수집 시각으로 연결하는 기준을 1주 차 계약에서 확정해야 합니다.
