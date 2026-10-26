using QaRunner.Models;
using QaRunner.Pipeline;

namespace QaRunner.Tests;

/// <summary>
/// 파이프라인 시험 채점기가 맞는 결과는 통과로, 틀린 결과는 실패로 잡아내는지 확인한다.
/// 가짜 파이프라인(FakePipeline)은 격리 기능이 없으므로 격리가 필요한 시나리오에서 실패가 나와야 한다.
/// </summary>
public class PipelineCheckerTests
{
    /// <summary>
    /// 항상 전송에 실패하는 시험용 전송기 (Kafka가 꺼져 있는 상황).
    /// </summary>
    private sealed class AlwaysFailingEventSender : IEventSender
    {
        public Task SendAsync(PipelineEvent pipelineEvent, CancellationToken cancellationToken) =>
            throw new EventSendException("Kafka에 연결할 수 없습니다");
    }

    private static ScenarioEvent CreateScenarioEvent(string eventId, string verdict = "고위험") =>
        new() { EventId = eventId, ScanId = $"scan-{eventId}", Verdict = verdict };

    private static PipelineChecker CreateCheckerWithFakePipeline()
    {
        var fakePipeline = new FakePipeline();
        return new PipelineChecker(fakePipeline, fakePipeline, catchUpTimeout: TimeSpan.Zero);
    }

    [Fact]
    public async Task Duplicate_scenario_passes_on_fake_pipeline_because_it_removes_duplicates()
    {
        var scenario = new PipelineScenario
        {
            CaseId = "DUP-001",
            Events = [CreateScenarioEvent("e1")],
            Injection = new FaultInjection { DuplicateCount = 3 },
        };

        var testResults = await CreateCheckerWithFakePipeline().CheckAllAsync([scenario], "run-1");

        Assert.All(testResults, testResult => Assert.True(testResult.IsPassed, testResult.CheckName));

        var silverResult = testResults.Single(testResult => testResult.CheckName == "Silver 고유 건수");
        Assert.Equal("1", silverResult.Expected);
    }

    [Fact]
    public async Task Late_scenario_fails_on_fake_pipeline_because_it_does_not_quarantine()
    {
        var scenario = new PipelineScenario
        {
            CaseId = "LATE-001",
            Events = [CreateScenarioEvent("e1")],
            Injection = new FaultInjection { DelaySeconds = 360 },
        };

        var testResults = await CreateCheckerWithFakePipeline().CheckAllAsync([scenario], "run-1");

        var quarantineResult = testResults.Single(testResult => testResult.CheckName == "격리 건수");
        Assert.False(quarantineResult.IsPassed);
        Assert.Equal("1", quarantineResult.Expected);
        Assert.Equal("0", quarantineResult.Actual);

        // 격리됐어야 할 이벤트가 Gold에 잘못 집계된 것도 잡아내야 한다
        var goldResult = testResults.Single(testResult => testResult.CaseId == TestResult.WholeRunCaseId);
        Assert.False(goldResult.IsPassed);
        Assert.Equal("0", goldResult.Expected);
        Assert.Equal("1", goldResult.Actual);
    }

    [Fact]
    public async Task Bronze_silver_and_quarantine_are_checked_per_scenario()
    {
        PipelineScenario[] scenarios =
        [
            new() { CaseId = "A", Events = [CreateScenarioEvent("a1")] },
            new() { CaseId = "B", Events = [CreateScenarioEvent("b1"), CreateScenarioEvent("b2")] },
        ];

        var testResults = await CreateCheckerWithFakePipeline().CheckAllAsync(scenarios, "run-1");

        Assert.Equal(3, testResults.Count(testResult => testResult.CaseId == "A"));
        Assert.Equal(3, testResults.Count(testResult => testResult.CaseId == "B"));

        var bronzeOfB = testResults.Single(testResult => testResult.CaseId == "B" && testResult.CheckName == "Bronze 보관 건수");
        Assert.Equal("2", bronzeOfB.Actual);
    }

    [Fact]
    public async Task Send_failure_is_recorded_as_system_failure()
    {
        var fakePipeline = new FakePipeline();
        var pipelineChecker = new PipelineChecker(new AlwaysFailingEventSender(), fakePipeline, catchUpTimeout: TimeSpan.Zero);
        var scenario = new PipelineScenario { CaseId = "DUP-001", Events = [CreateScenarioEvent("e1")] };

        var testResults = await pipelineChecker.CheckAllAsync([scenario], "run-1");

        var scenarioResults = testResults.Where(testResult => testResult.CaseId == "DUP-001").ToList();
        Assert.Equal(3, scenarioResults.Count);
        Assert.All(scenarioResults, testResult =>
        {
            Assert.True(testResult.IsSystemFailure);
            Assert.Equal("Kafka에 연결할 수 없습니다", testResult.ErrorMessage);
        });
    }
}
