using QaRunner.Measurement;
using QaRunner.Models;
using QaRunner.Pipeline;
using QaRunner.Verdicts;

namespace QaRunner.Tests;

/// <summary>
/// 부하·복구 시험을 실제 시간으로 짧게(몇 초) 돌려서, 처음부터 끝까지 측정 결과가 나오는지 확인한다.
/// </summary>
public class MeasurementCheckerTests
{
    /// <summary>
    /// API가 꺼져 있을 때처럼 항상 연결 오류를 내는 시험용 판정 대상.
    /// </summary>
    private sealed class AlwaysFailingVerdictService : IVerdictService
    {
        public Task<string> GetVerdictAsync(string message) => throw new HttpRequestException("연결할 수 없습니다");
    }

    private static readonly TestCase[] SampleCases = [new("R-001", "인증번호를 알려주세요", "고위험")];

    [Fact]
    public async Task Load_check_measures_api_and_gold_latency_within_targets()
    {
        var pipeline = new FakeStreamingPipeline();
        var settings = new MeasurementSettings(DurationSeconds: 2, EventsPerSecond: 10, StopSeconds: 0, ApiConcurrency: 2);
        var loadChecker = new LoadChecker(new FakeVerdictService(), SampleCases, pipeline, pipeline, settings);

        var testResults = await loadChecker.CheckAllAsync("run-1");

        Assert.All(testResults, testResult => Assert.True(testResult.IsPassed, $"{testResult.CheckName}: {testResult.Actual}"));

        // 2초 × 초당 10건 = 20건을 보내고 모두 Gold에 반영돼야 한다
        var reflectedCount = testResults.Single(testResult => testResult.CheckName.StartsWith("Gold 반영 건수"));
        Assert.Equal("20", reflectedCount.Expected);
        Assert.Equal("20", reflectedCount.Actual);
    }

    [Fact]
    public async Task Load_check_reports_system_failure_when_api_is_down()
    {
        var pipeline = new FakeStreamingPipeline();
        var settings = new MeasurementSettings(DurationSeconds: 1, EventsPerSecond: 5, StopSeconds: 0, ApiConcurrency: 1);
        var loadChecker = new LoadChecker(new AlwaysFailingVerdictService(), SampleCases, pipeline, pipeline, settings);

        var testResults = await loadChecker.CheckAllAsync("run-1");

        var latencyResult = testResults.Single(testResult => testResult.CheckName == "API 응답 시간 p95");
        Assert.True(latencyResult.IsSystemFailure);

        var errorRateResult = testResults.Single(testResult => testResult.CheckName == "API 오류율");
        Assert.False(errorRateResult.IsPassed);
        Assert.Equal("100.0%", errorRateResult.Actual);
    }

    [Fact]
    public async Task Recovery_check_measures_catch_up_time_after_restart()
    {
        var pipeline = new FakeStreamingPipeline();

        // 4초 동안 보내면서 1초 지점에서 1초 멈췄다가 다시 켠다
        var settings = new MeasurementSettings(DurationSeconds: 4, EventsPerSecond: 10, StopSeconds: 1);
        var recoveryChecker = new RecoveryChecker(pipeline, pipeline, pipeline, settings);

        var testResults = await recoveryChecker.CheckAllAsync("run-1");

        Assert.Equal(2, testResults.Count);
        Assert.All(testResults, testResult => Assert.True(testResult.IsPassed, $"{testResult.CheckName}: {testResult.Actual}"));
        Assert.EndsWith("초", testResults.Single(testResult => testResult.CheckName == "재가동 후 회복 시간").Actual);
    }

    [Fact]
    public void Measurement_result_passes_by_target_not_by_text_equality()
    {
        var withinTarget = new TestResult("LOAD-API", "API 응답 시간 p95", "입력", "3초 이하", "0.42초", MeetsTarget: true);
        var overTarget = withinTarget with { Actual = "4.10초", MeetsTarget = false };

        Assert.True(withinTarget.IsPassed);
        Assert.False(overTarget.IsPassed);
    }
}
