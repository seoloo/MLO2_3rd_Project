namespace QaRunner.Pipeline;

/// <summary>
/// 시나리오 하나에 대해 파이프라인에 실제로 쌓인 건수.
/// </summary>
/// <param name="BronzeCount">Bronze에 보관된 건수</param>
/// <param name="SilverCount">Silver에 남은 고유 이벤트 수</param>
/// <param name="QuarantineCount">격리된 건수</param>
public sealed record CaseCounts(int BronzeCount, int SilverCount, int QuarantineCount);

/// <summary>
/// 파이프라인 결과를 읽지 못했을 때 쓰는 예외 (MongoDB 연결 실패 등).
/// 결과를 못 읽으면 채점 자체가 안 되므로, 콘솔은 종료 코드 2로 끝내고 데스크탑 앱은 오류로 알린다.
/// </summary>
public sealed class PipelineReadException(string message, Exception? innerException = null)
    : Exception(message, innerException);

/// <summary>
/// 파이프라인에 실제로 쌓인 결과를 읽는 쪽. 지금은 FakePipeline을 쓰고,
/// C의 파이프라인이 준비되면 MongoDB(bronze_events, silver_events, gold_metrics)를 읽는 구현으로 바꾼다.
/// </summary>
public interface IPipelineReader
{
    /// <summary>
    /// 이번 실행의 시나리오 하나에 해당하는 Bronze·Silver·격리 건수를 읽는다.
    /// </summary>
    /// <param name="runId">QA 실행 번호</param>
    /// <param name="caseId">시나리오 번호</param>
    Task<CaseCounts> ReadCaseCountsAsync(string runId, string caseId, CancellationToken cancellationToken);

    /// <summary>
    /// Gold에서 주어진 60초 구간들의 집계 건수를 읽는다. 구간에 데이터가 없으면 0으로 돌려준다.
    /// Gold는 실행 번호와 상관없이 구간별로 합쳐지므로 실행 번호로 거르지 않는다.
    /// </summary>
    /// <param name="windowStarts">읽을 구간의 시작 시각들</param>
    Task<IReadOnlyDictionary<DateTimeOffset, int>> ReadGoldCountsAsync(
        IReadOnlyCollection<DateTimeOffset> windowStarts, CancellationToken cancellationToken);
}
