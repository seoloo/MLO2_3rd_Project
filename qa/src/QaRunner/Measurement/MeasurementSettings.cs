using QaRunner.Models;

namespace QaRunner.Measurement;

/// <summary>
/// 부하·복구 시험의 측정 조건.
/// </summary>
/// <param name="DurationSeconds">이벤트를 계속 보내는 시간(초)</param>
/// <param name="EventsPerSecond">1초에 보내는 이벤트 수</param>
/// <param name="StopSeconds">복구 시험에서 처리를 멈춰 두는 시간(초)</param>
/// <param name="ApiConcurrency">부하 시험에서 API를 동시에 부르는 가상 사용자 수</param>
public sealed record MeasurementSettings(
    int DurationSeconds,
    double EventsPerSecond,
    int StopSeconds,
    int ApiConcurrency = 5)
{
    /// <summary>
    /// 실행안 기준 기본값.
    /// 부하: 초당 5건 × 300초(=1,500건), API 동시 5개. 복구: 초당 5건 × 120초, 30초 중단.
    /// </summary>
    public static MeasurementSettings DefaultFor(TestSuite testSuite) => testSuite == TestSuite.Recovery
        ? new MeasurementSettings(DurationSeconds: 120, EventsPerSecond: 5, StopSeconds: 30)
        : new MeasurementSettings(DurationSeconds: 300, EventsPerSecond: 5, StopSeconds: 30);

    /// <summary>
    /// 복구 시험에서 처리를 멈추기 시작하는 시점(초). 전체 시간의 4분의 1 지점이다.
    /// 그 전까지는 정상 처리 상태를 만들고, 재가동 뒤에도 계속 보내면서 따라잡는지 본다.
    /// </summary>
    public int StopAfterSeconds => DurationSeconds / 4;

    /// <summary>
    /// 값이 시험을 돌릴 수 있는 범위인지 확인한다. 아니면 ArgumentException을 던진다.
    /// </summary>
    public void Validate(TestSuite testSuite)
    {
        if (DurationSeconds <= 0)
            throw new ArgumentException($"--duration은 1 이상이어야 합니다: {DurationSeconds}");
        if (EventsPerSecond <= 0)
            throw new ArgumentException($"--rate는 0보다 커야 합니다: {EventsPerSecond}");
        if (ApiConcurrency <= 0)
            throw new ArgumentException($"API 동시 사용자 수는 1 이상이어야 합니다: {ApiConcurrency}");

        // 재가동한 뒤에도 보내는 시간이 남아 있어야 "보내면서 따라잡기"를 볼 수 있다
        if (testSuite == TestSuite.Recovery && (StopSeconds <= 0 || StopAfterSeconds + StopSeconds >= DurationSeconds))
        {
            throw new ArgumentException(
                $"복구 시험은 중단 시간(--stop-seconds {StopSeconds})이 1 이상이고, " +
                $"중단 시작({StopAfterSeconds}초) + 중단 시간이 전체 시간(--duration {DurationSeconds})보다 짧아야 합니다.");
        }
    }
}
