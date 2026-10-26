using QaRunner.Models;

namespace QaRunner.Tests;

/// <summary>
/// 실행 기록의 집계 값(통과·실패 수, 통과율)과 실행 번호 형식을 확인한다.
/// </summary>
public class TestRunTests
{
    [Fact]
    public void Counts_passed_and_failed_results()
    {
        var testRun = SampleTestRuns.OnePassedOneFailed();

        Assert.Equal(2, testRun.TotalCount);
        Assert.Equal(1, testRun.PassedCount);
        Assert.Equal(1, testRun.FailedCount);
        Assert.Equal(50.0, testRun.PassRate);
        Assert.Equal(TimeSpan.FromSeconds(1.5), testRun.Duration);
    }

    [Fact]
    public void Pass_rate_is_zero_when_there_are_no_results()
    {
        var emptyTestRun = SampleTestRuns.OnePassedOneFailed() with { Results = [] };

        Assert.Equal(0, emptyTestRun.PassRate);
    }

    [Fact]
    public void Run_id_is_start_time_without_colons()
    {
        var runId = TestRun.CreateRunId(SampleTestRuns.StartedAt);

        // 파일 이름으로 쓰기 때문에 콜론이 들어가면 안 된다
        Assert.Equal("2026-10-26-143015", runId);
    }
}
