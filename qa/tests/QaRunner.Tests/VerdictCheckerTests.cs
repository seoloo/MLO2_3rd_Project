using QaRunner.Models;
using QaRunner.Verdicts;

namespace QaRunner.Tests;

/// <summary>
/// 판정 시험 채점기(VerdictChecker)가 통과·실패·시스템 실패를 올바르게 판단하는지 확인한다.
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

    /// <summary>
    /// API가 꺼져 있을 때처럼 항상 연결 오류를 내는 시험용 판정 대상.
    /// </summary>
    private sealed class AlwaysFailingVerdictService : IVerdictService
    {
        public Task<string> GetVerdictAsync(string message) =>
            throw new HttpRequestException("연결할 수 없습니다");
    }

    [Fact]
    public async Task Passes_when_actual_verdict_matches_expected()
    {
        var verdictChecker = new VerdictChecker(new AlwaysSameVerdictService("고위험"));
        var testCase = new TestCase("R-1", "아무 문자", ExpectedVerdict: "고위험");

        var testResults = await verdictChecker.CheckAllAsync([testCase]);

        var testResult = testResults.Single();
        Assert.True(testResult.IsPassed);
        Assert.Equal(VerdictChecker.CheckName, testResult.CheckName);
        Assert.Equal("아무 문자", testResult.Input);
    }

    [Fact]
    public async Task Fails_when_actual_verdict_differs_from_expected()
    {
        var verdictChecker = new VerdictChecker(new AlwaysSameVerdictService("주의"));
        var testCase = new TestCase("R-1", "아무 문자", ExpectedVerdict: "고위험");

        var testResults = await verdictChecker.CheckAllAsync([testCase]);

        var testResult = testResults.Single();
        Assert.False(testResult.IsPassed);
        Assert.Equal("고위험", testResult.Expected);
        Assert.Equal("주의", testResult.Actual);
    }

    [Fact]
    public async Task Records_system_failure_and_keeps_checking_other_cases()
    {
        var verdictChecker = new VerdictChecker(new AlwaysFailingVerdictService());
        TestCase[] testCases =
        [
            new("R-1", "첫 번째 문자", ExpectedVerdict: "고위험"),
            new("R-2", "두 번째 문자", ExpectedVerdict: "주의"),
        ];

        var testResults = await verdictChecker.CheckAllAsync(testCases);

        // 첫 케이스에서 오류가 나도 멈추지 않고 두 케이스 모두 기록해야 한다
        Assert.Equal(2, testResults.Count);
        Assert.All(testResults, testResult =>
        {
            Assert.False(testResult.IsPassed);
            Assert.True(testResult.IsSystemFailure);
            Assert.Equal(TestResult.SystemFailureValue, testResult.Actual);
            Assert.Equal("연결할 수 없습니다", testResult.ErrorMessage);
        });
    }
}
