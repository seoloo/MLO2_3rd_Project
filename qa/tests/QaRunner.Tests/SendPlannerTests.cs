using QaRunner.Pipeline;

namespace QaRunner.Tests;

/// <summary>
/// 전송 계획이 시나리오의 조건(중복·역순·지연)대로 만들어지는지 확인한다.
/// </summary>
public class SendPlannerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    private static ScenarioEvent CreateScenarioEvent(string eventId, int offsetSeconds) =>
        new() { EventId = eventId, ScanId = $"scan-{eventId}", OffsetSeconds = offsetSeconds };

    [Fact]
    public void Duplicate_count_repeats_each_event()
    {
        var scenario = new PipelineScenario
        {
            CaseId = "DUP",
            Events = [CreateScenarioEvent("e1", 0)],
            Injection = new FaultInjection { DuplicateCount = 3 },
        };

        var plannedSends = SendPlanner.BuildPlan(scenario, "run-1", Now);

        Assert.Equal(3, plannedSends.Count);
        Assert.All(plannedSends, plannedSend => Assert.Equal("run-1-e1", plannedSend.Event.EventId));
    }

    [Fact]
    public void Reverse_order_sends_newest_event_first_after_the_last_event()
    {
        var scenario = new PipelineScenario
        {
            CaseId = "ORDER",
            Events = [CreateScenarioEvent("e1", 0), CreateScenarioEvent("e2", 1), CreateScenarioEvent("e3", 2)],
            Injection = new FaultInjection { SendInReverseOrder = true },
        };

        var plannedSends = SendPlanner.BuildPlan(scenario, "run-1", Now);

        Assert.Equal(new[] { "run-1-e3", "run-1-e2", "run-1-e1" }, plannedSends.Select(plannedSend => plannedSend.Event.EventId));
        Assert.All(plannedSends, plannedSend => Assert.Equal(Now.AddSeconds(2), plannedSend.SendAt));
    }

    [Fact]
    public void Delay_moves_event_time_into_the_past_instead_of_waiting()
    {
        var scenario = new PipelineScenario
        {
            CaseId = "DELAY",
            Events = [CreateScenarioEvent("e1", 0)],
            Injection = new FaultInjection { DelaySeconds = 30 },
        };

        var plannedSend = SendPlanner.BuildPlan(scenario, "run-1", Now).Single();

        // 지금 바로 보내지만, 이벤트 시각은 30초 전이라 30초 늦게 도착한 것이 된다
        Assert.Equal(Now, plannedSend.SendAt);
        Assert.Equal(TimeSpan.FromSeconds(30), plannedSend.SendAt - plannedSend.Event.EventTime);
    }

    [Fact]
    public void Every_event_carries_run_id_and_case_id()
    {
        var scenario = new PipelineScenario { CaseId = "CASE-1", Events = [CreateScenarioEvent("e1", 0)] };

        var plannedSend = SendPlanner.BuildPlan(scenario, "run-7", Now).Single();

        Assert.Equal("run-7", plannedSend.Event.RunId);
        Assert.Equal("CASE-1", plannedSend.Event.CaseId);
    }
}
