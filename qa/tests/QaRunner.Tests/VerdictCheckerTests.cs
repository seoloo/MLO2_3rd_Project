using QaRunner.Models;
using QaRunner.Verdicts;

namespace QaRunner.Tests;

/// <summary>
/// 판정 시험 채점기(VerdictChecker)와 탐지율·오탐률 계산(DetectionSummary)이
/// 통과·불일치·시스템 실패와 묶음 비교를 올바르게 판단하는지 확인한다.
/// </summary>
public class VerdictCheckerTests
{
    /// <summary>
    /// 어떤 세션이 들어와도 항상 같은 판정을 돌려주는 시험용 판정 대상. 받은 세션 해시 값을 기록한다.
    /// </summary>
    private sealed class AlwaysSameVerdictService(string verdict) : IVerdictService
    {
        public List<string> SessionKeys { get; } = [];

        public Task<string> GetVerdictAsync(SessionCase session, string runId, string sessionKey)
        {
            SessionKeys.Add(sessionKey);
            return Task.FromResult(verdict);
        }
    }

    /// <summary>사례 번호마다 정해 둔 판정을 돌려주는 시험용 판정 대상</summary>
    private sealed class VerdictByCaseService(Dictionary<string, string> verdictByCaseId) : IVerdictService
    {
        public Task<string> GetVerdictAsync(SessionCase session, string runId, string sessionKey) =>
            verdictByCaseId.TryGetValue(session.CaseId, out var verdict)
                ? Task.FromResult(verdict)
                : throw new HttpRequestException("연결할 수 없습니다");
    }

    /// <summary>
    /// API가 꺼져 있을 때처럼 항상 연결 오류를 내는 시험용 판정 대상.
    /// </summary>
    private sealed class AlwaysFailingVerdictService : IVerdictService
    {
        public Task<string> GetVerdictAsync(SessionCase session, string runId, string sessionKey) =>
            throw new HttpRequestException("연결할 수 없습니다");
    }

    [Fact]
    public async Task Passes_when_actual_verdict_matches_expected()
    {
        var verdictService = new AlwaysSameVerdictService("추가 확인");
        var verdictChecker = new VerdictChecker(verdictService);

        var testResults = await verdictChecker.CheckAllAsync([SampleSessions.Bot("S-1")], "run-1");

        var testResult = testResults.Single();
        Assert.True(testResult.IsPassed);
        Assert.Equal(VerdictChecker.CheckName, testResult.CheckName);
        Assert.Equal("봇 A · 오픈 0.03초 뒤 진입 · 0.1초 간격", testResult.Input);
        Assert.Equal("추가 확인 이상 (추가 확인)", testResult.Expected);
        Assert.Equal("추가 확인", testResult.Actual);

        // 세션 해시는 실행 번호와 사례 번호로 만들어 실행마다 겹치지 않는다
        Assert.Equal(["run-1-S-1"], verdictService.SessionKeys);
    }

    [Theory]
    [InlineData("정상", "관찰")]
    [InlineData("추가 확인", "보류")]
    public async Task Passes_when_verdict_is_in_the_same_group(string expectedVerdict, string actualVerdict)
    {
        // 사람이 "관찰"이어도, 봇이 "보류"여도 기획서의 탐지·오탐 기준으로는 맞다
        var verdictChecker = new VerdictChecker(new AlwaysSameVerdictService(actualVerdict));
        var sessionCase = expectedVerdict == "정상" ? SampleSessions.Human() : SampleSessions.Bot();

        var testResult = (await verdictChecker.CheckAllAsync([sessionCase], "run-1")).Single();

        Assert.True(testResult.IsPassed);
        Assert.Equal(actualVerdict, testResult.Actual);
    }

    [Theory]
    [InlineData("정상", "추가 확인")]
    [InlineData("추가 확인", "관찰")]
    [InlineData("추가 확인", "VERY_DANGEROUS")]
    public async Task Fails_when_verdict_is_in_a_different_group_or_unknown(string expectedVerdict, string actualVerdict)
    {
        var verdictChecker = new VerdictChecker(new AlwaysSameVerdictService(actualVerdict));
        var sessionCase = expectedVerdict == "정상" ? SampleSessions.Human() : SampleSessions.Bot();

        var testResult = (await verdictChecker.CheckAllAsync([sessionCase], "run-1")).Single();

        Assert.False(testResult.IsPassed);
        Assert.False(testResult.IsSystemFailure);
        Assert.Equal(actualVerdict, testResult.Actual);
    }

    [Fact]
    public async Task Records_system_failure_and_keeps_checking_other_cases()
    {
        var verdictChecker = new VerdictChecker(new AlwaysFailingVerdictService());

        var testResults = await verdictChecker.CheckAllAsync([SampleSessions.Human("S-1"), SampleSessions.Bot("S-2")], "run-1");

        // 첫 사례에서 오류가 나도 멈추지 않고 두 사례 모두 기록해야 한다
        Assert.Equal(2, testResults.Count);
        Assert.All(testResults, testResult =>
        {
            Assert.False(testResult.IsPassed);
            Assert.True(testResult.IsSystemFailure);
            Assert.Equal(TestResult.SystemFailureValue, testResult.Actual);
            Assert.Equal("연결할 수 없습니다", testResult.ErrorMessage);
        });
    }

    [Fact]
    public async Task Detection_summary_counts_bots_and_humans_separately()
    {
        SessionCase[] sessions =
        [
            SampleSessions.Bot("A1"), SampleSessions.Bot("A2", "봇 B"), SampleSessions.Bot("D1", "봇 D", parallelSessions: 20),
            SampleSessions.Bot("C1", "봇 C"),
            SampleSessions.Bot("N1", "새 유형 봇"),
            SampleSessions.Human("H1"), SampleSessions.Human("H2"),
            SampleSessions.Human("H3"),
        ];
        var verdicts = new Dictionary<string, string>
        {
            ["A1"] = "추가 확인", ["A2"] = "보류", ["D1"] = "정상", ["C1"] = "추가 확인", ["N1"] = "관찰",
            ["H1"] = "정상", ["H2"] = "추가 확인",
            // H3은 응답이 없어 오탐률에서 빠진다
        };
        var caseResults = await new VerdictChecker(new VerdictByCaseService(verdicts)).CheckAllAsync(sessions, "run-1");

        var summary = DetectionSummary.Build(sessions, caseResults).ToDictionary(result => result.CheckName);

        Assert.Equal("66.7% (2/3)", summary[DetectionSummary.RegularBotDetectionCheckName].Actual);
        Assert.False(summary[DetectionSummary.RegularBotDetectionCheckName].IsPassed);
        Assert.Equal("100% (1/1)", summary[DetectionSummary.BrowserBotDetectionCheckName].Actual);
        Assert.True(summary[DetectionSummary.BrowserBotDetectionCheckName].IsPassed);

        // 새 유형 봇은 목표 없이 결과만 공개한다
        Assert.Equal("0% (0/1)", summary[DetectionSummary.NewBotDetectionCheckName].Actual);
        Assert.True(summary[DetectionSummary.NewBotDetectionCheckName].IsPassed);

        var humans = summary[DetectionSummary.HumanFalsePositiveCheckName];
        Assert.Equal("50% (1/2)", humans.Actual);
        Assert.False(humans.IsPassed);
        Assert.Contains("표본 2개라 참고값", humans.Input);
        Assert.All(summary.Values, result => Assert.Equal(TestResult.WholeRunCaseId, result.CaseId));
    }

    [Fact]
    public void Detection_summary_skips_actor_groups_without_cases()
    {
        var caseResults = new List<TestResult>
        {
            new("H1", VerdictChecker.CheckName, "사람", "정상 처리 (정상)", "정상", MeetsTarget: true),
        };

        var summary = DetectionSummary.Build([SampleSessions.Human("H1")], caseResults);

        Assert.Equal([DetectionSummary.HumanFalsePositiveCheckName], summary.Select(result => result.CheckName));
        Assert.True(summary.Single().IsPassed);
    }
}
