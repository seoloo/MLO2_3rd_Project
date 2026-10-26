using QaRunner.Models;
using QaRunner.Verdicts;

namespace QaRunner.Tests;

/// <summary>
/// 채점기(VerdictChecker)가 통과·실패를 올바르게 판단하는지 확인한다.
/// </summary>
public class VerdictCheckerTests
{
    /// <summary>
    /// 어떤 문자가 들어와도 항상 같은 판정을 돌려주는 시험용 판정 대상.
    /// 결과를 미리 알 수 있어서 채점기만 따로 시험할 수 있다.
    /// </summary>
    private sealed class AlwaysSameVerdictService(string verdict) : IVerdictService
    {
        public Task<string> GetVerdictAsync(string message) => Task.FromResult(verdict);
    }

    [Fact]
    public async Task Passes_when_actual_verdict_matches_expected()
    {
        var verdictChecker = new VerdictChecker(new AlwaysSameVerdictService("고위험"));
        var testCase = new TestCase("R-1", "아무 문자", ExpectedVerdict: "고위험");

        var testResults = await verdictChecker.CheckAllAsync([testCase]);

        Assert.True(testResults.Single().IsPassed);
    }

    [Fact]
    public async Task Fails_when_actual_verdict_differs_from_expected()
    {
        var verdictChecker = new VerdictChecker(new AlwaysSameVerdictService("주의"));
        var testCase = new TestCase("R-1", "아무 문자", ExpectedVerdict: "고위험");

        var testResults = await verdictChecker.CheckAllAsync([testCase]);

        var testResult = testResults.Single();
        Assert.False(testResult.IsPassed);
        Assert.Equal("고위험", testResult.ExpectedVerdict);
        Assert.Equal("주의", testResult.ActualVerdict);
    }
}
