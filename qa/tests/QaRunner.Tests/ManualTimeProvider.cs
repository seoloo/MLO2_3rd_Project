namespace QaRunner.Tests;

/// <summary>
/// 테스트에서 현재 시각을 직접 정하고 움직이는 시계.
/// 실제로 기다리지 않고도 "3초 뒤"의 상태를 바로 확인할 수 있다.
/// </summary>
internal sealed class ManualTimeProvider(DateTimeOffset startTime) : TimeProvider
{
    private DateTimeOffset _now = startTime;

    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>시각을 앞으로 움직인다</summary>
    public void Advance(TimeSpan elapsed) => _now += elapsed;
}
