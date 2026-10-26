using QaRunner.Models;
using QaRunner.Verdicts;

namespace QaRunner;

/// <summary>
/// 채점기. 시험 케이스마다 문자를 판정 대상에 보내고,
/// 돌아온 판정을 입력값 파일의 정답과 비교한다.
/// </summary>
/// <param name="verdictService">문자를 판정해 줄 대상 (지금은 가짜, 나중에 실제 API)</param>
public sealed class VerdictChecker(IVerdictService verdictService)
{
    /// <summary>
    /// 모든 시험 케이스를 차례로 채점하고 결과 목록을 돌려준다.
    /// </summary>
    /// <param name="testCases">채점할 시험 케이스 목록</param>
    public async Task<List<TestResult>> CheckAllAsync(IEnumerable<TestCase> testCases)
    {
        var testResults = new List<TestResult>();

        foreach (var testCase in testCases)
        {
            // 판정 대상에는 문자만 보낸다. 정답은 보내지 않는다 (독립 정답).
            var actualVerdict = await verdictService.GetVerdictAsync(testCase.Message);

            testResults.Add(new TestResult(testCase.CaseId, testCase.Message, testCase.ExpectedVerdict, actualVerdict));
        }

        return testResults;
    }
}
