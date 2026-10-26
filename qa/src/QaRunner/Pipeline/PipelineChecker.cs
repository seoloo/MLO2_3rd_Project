using QaRunner.Models;

namespace QaRunner.Pipeline;

/// <summary>
/// 파이프라인 시험 채점기.
/// 1. 시나리오마다 전송 계획을 세우고 정답 계산기에 기록한다.
/// 2. 계획한 시각 순서대로 이벤트를 보낸다.
/// 3. Gold가 정답을 따라잡을 때까지 기다린다 (제한 시간 있음).
/// 4. 시나리오별 Bronze·Silver·격리 건수와, 실행 전체의 Gold 구간 집계를 정답과 비교한다.
/// </summary>
/// <param name="eventSender">이벤트를 넣을 곳 (가짜 파이프라인 또는 Kafka)</param>
/// <param name="pipelineReader">결과를 읽을 곳 (가짜 파이프라인 또는 MongoDB)</param>
/// <param name="catchUpTimeout">Gold가 정답을 따라잡기를 기다리는 최대 시간</param>
public sealed class PipelineChecker(IEventSender eventSender, IPipelineReader pipelineReader, TimeSpan catchUpTimeout)
{
    /// <summary>기본 대기 시간. 실행안의 "재가동 후 60초 안에 따라잡기"에 맞췄다</summary>
    public static readonly TimeSpan DefaultCatchUpTimeout = TimeSpan.FromSeconds(60);

    // Gold를 다시 읽기 전에 쉬는 시간
    private static readonly TimeSpan GoldPollInterval = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 모든 시나리오를 보내고 채점한 결과를 돌려준다.
    /// </summary>
    /// <param name="scenarios">시험할 시나리오 목록</param>
    /// <param name="runId">이번 QA 실행 번호. 모든 이벤트에 붙여서 이번 실행 것만 골라 읽는다</param>
    public async Task<List<TestResult>> CheckAllAsync(
        IReadOnlyList<PipelineScenario> scenarios, string runId, CancellationToken cancellationToken = default)
    {
        // 1. 전송 계획과 정답 계산
        //    시나리오별 정답(Bronze·Silver·격리)과, 시나리오를 모두 합친 실행 전체 정답(Gold)을 따로 계산한다
        var planStartTime = DateTimeOffset.Now;
        var wholeRunExpected = new ExpectedResultCalculator();
        var expectedByCaseId = new Dictionary<string, ExpectedResultCalculator>();
        var allPlannedSends = new List<PlannedSend>();

        foreach (var scenario in scenarios)
        {
            var scenarioExpected = new ExpectedResultCalculator();
            foreach (var plannedSend in SendPlanner.BuildPlan(scenario, runId, planStartTime))
            {
                scenarioExpected.Record(plannedSend.Event, plannedSend.SendAt);
                wholeRunExpected.Record(plannedSend.Event, plannedSend.SendAt);
                allPlannedSends.Add(plannedSend);
            }
            expectedByCaseId[scenario.CaseId] = scenarioExpected;
        }

        // 2. 계획한 시각 순서대로 보내기. 보내지 못한 시나리오는 이유를 기억해 둔다
        var sendErrorByCaseId = await SendAllAsync(allPlannedSends, cancellationToken);

        // 3. Gold 기다리기. 보낸 이벤트가 걸친 구간을 모두 확인해야
        //    격리됐어야 할 이벤트가 잘못 집계된 경우도 잡을 수 있다
        var windowsToCheck = allPlannedSends
            .Select(plannedSend => ExpectedResultCalculator.GetWindowStart(plannedSend.Event.EventTime))
            .Distinct()
            .Order()
            .ToList();
        var expectedGoldCounts = windowsToCheck.ToDictionary(
            windowStart => windowStart,
            windowStart => wholeRunExpected.GoldCountsByWindow.GetValueOrDefault(windowStart));
        var actualGoldCounts = await WaitForGoldAsync(expectedGoldCounts, cancellationToken);

        // 4. 비교
        var testResults = new List<TestResult>();
        foreach (var scenario in scenarios)
        {
            var scenarioExpected = expectedByCaseId[scenario.CaseId];
            sendErrorByCaseId.TryGetValue(scenario.CaseId, out var sendError);
            testResults.AddRange(await CompareScenarioAsync(scenario, scenarioExpected, runId, sendError, cancellationToken));
        }

        foreach (var windowStart in windowsToCheck)
        {
            testResults.Add(new TestResult(
                CaseId: TestResult.WholeRunCaseId,
                CheckName: $"Gold {windowStart.ToLocalTime():HH:mm} 구간 건수",
                Input: "모든 시나리오 합계",
                Expected: expectedGoldCounts[windowStart].ToString(),
                Actual: actualGoldCounts.GetValueOrDefault(windowStart).ToString()));
        }

        return testResults;
    }

    // 계획한 시각이 될 때까지 기다렸다가 보낸다. 실패한 시나리오 번호 → 실패 이유를 돌려준다
    private async Task<Dictionary<string, string>> SendAllAsync(
        List<PlannedSend> plannedSends, CancellationToken cancellationToken)
    {
        var sendErrorByCaseId = new Dictionary<string, string>();

        // OrderBy는 같은 시각끼리 계획 순서를 유지한다 (역순·중복 순서가 그대로 지켜짐)
        foreach (var plannedSend in plannedSends.OrderBy(plannedSend => plannedSend.SendAt))
        {
            var waitTime = plannedSend.SendAt - DateTimeOffset.Now;
            if (waitTime > TimeSpan.Zero)
                await Task.Delay(waitTime, cancellationToken);

            try
            {
                await eventSender.SendAsync(plannedSend.Event, cancellationToken);
            }
            catch (EventSendException exception)
            {
                sendErrorByCaseId.TryAdd(plannedSend.Event.CaseId, exception.Message);
            }
        }

        return sendErrorByCaseId;
    }

    // Gold가 정답과 같아질 때까지 다시 읽는다. 제한 시간이 지나면 그때 값을 돌려준다
    private async Task<IReadOnlyDictionary<DateTimeOffset, int>> WaitForGoldAsync(
        Dictionary<DateTimeOffset, int> expectedGoldCounts, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.Now + catchUpTimeout;

        while (true)
        {
            var actualGoldCounts = await pipelineReader.ReadGoldCountsAsync(expectedGoldCounts.Keys, cancellationToken);
            var hasCaughtUp = expectedGoldCounts.All(
                expected => actualGoldCounts.GetValueOrDefault(expected.Key) == expected.Value);

            if (hasCaughtUp || DateTimeOffset.Now >= deadline)
                return actualGoldCounts;

            await Task.Delay(GoldPollInterval, cancellationToken);
        }
    }

    // 시나리오 하나의 Bronze·Silver·격리 건수를 정답과 비교한다
    private async Task<List<TestResult>> CompareScenarioAsync(
        PipelineScenario scenario,
        ExpectedResultCalculator scenarioExpected,
        string runId,
        string? sendError,
        CancellationToken cancellationToken)
    {
        var expectedCounts = new (string CheckName, int ExpectedCount)[]
        {
            ("Bronze 보관 건수", scenarioExpected.BronzeCount),
            ("Silver 고유 건수", scenarioExpected.SilverCount),
            ("격리 건수", scenarioExpected.QuarantineCount),
        };

        // 이벤트를 보내지도 못했으면 비교할 수 없으므로 모두 시스템 실패로 기록한다
        if (sendError is not null)
        {
            return expectedCounts
                .Select(expected => TestResult.SystemFailure(
                    scenario.CaseId, expected.CheckName, scenario.Description, expected.ExpectedCount.ToString(), sendError))
                .ToList();
        }

        var actualCounts = await pipelineReader.ReadCaseCountsAsync(runId, scenario.CaseId, cancellationToken);
        var actualByCheckName = new Dictionary<string, int>
        {
            ["Bronze 보관 건수"] = actualCounts.BronzeCount,
            ["Silver 고유 건수"] = actualCounts.SilverCount,
            ["격리 건수"] = actualCounts.QuarantineCount,
        };

        return expectedCounts
            .Select(expected => new TestResult(
                scenario.CaseId,
                expected.CheckName,
                scenario.Description,
                expected.ExpectedCount.ToString(),
                actualByCheckName[expected.CheckName].ToString()))
            .ToList();
    }
}
