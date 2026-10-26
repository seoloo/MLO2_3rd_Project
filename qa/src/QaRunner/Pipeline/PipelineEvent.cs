namespace QaRunner.Pipeline;

/// <summary>
/// QA 러너가 파이프라인(Kafka)에 넣는 이벤트 한 건.
/// 필드 구성은 1주차 계약(이벤트 스키마, event_id와 scan_id 관계)을 C와 확정하기 전의 임시안이다.
/// </summary>
/// <param name="RunId">어느 QA 실행에서 보낸 이벤트인지. 결과를 읽을 때 이번 실행 것만 골라내는 데 쓴다</param>
/// <param name="CaseId">어느 시나리오에서 보낸 이벤트인지</param>
/// <param name="EventId">이벤트 고유 번호. Silver는 이 값으로 중복을 없앤다</param>
/// <param name="ScanId">검사 번호. Kafka 메시지 키로 쓴다</param>
/// <param name="EventTime">이벤트가 실제로 일어난 시각. Gold는 이 시각 기준 60초 구간으로 집계한다</param>
/// <param name="Verdict">이 검사의 판정</param>
public sealed record PipelineEvent(
    string RunId,
    string CaseId,
    string EventId,
    string ScanId,
    DateTimeOffset EventTime,
    string Verdict);
