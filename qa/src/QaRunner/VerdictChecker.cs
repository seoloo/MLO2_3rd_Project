using System.Text.Json;
using QaRunner.Models;
using QaRunner.Verdicts;

namespace QaRunner;

/// <summary>
/// 채점기. 시험 케이스마다 문자를 판정 대상에 보내고,
/// 돌아온 판정을 입력값 파일의 정답과 비교한다.
/// </summary>
/// <param name="verdictService">문자를 판정해 줄 대상 (가짜 또는 실제 API)</param>
public sealed class VerdictChecker(IVerdictService verdictService)
{
    /// <summary>
    /// 모든 시험 케이스를 차례로 채점하고 결과 목록을 돌려준다.
    /// 한 케이스에서 API 오류가 나도 멈추지 않고 "시스템 실패"로 기록한 뒤 다음 케이스로 넘어간다.
    /// </summary>
    /// <param name="testCases">채점할 시험 케이스 목록</param>
    public async Task<List<TestResult>> CheckAllAsync(IEnumerable<TestCase> testCases)
    {
        var testResults = new List<TestResult>();

        foreach (var testCase in testCases)
        {
            testResults.Add(await CheckOneAsync(testCase));
        }

        return testResults;
    }

    // 케이스 하나를 채점한다. 판정을 못 받으면 시스템 실패 결과를 만든다
    private async Task<TestResult> CheckOneAsync(TestCase testCase)
    {
        try
        {
            // 판정 대상에는 문자만 보낸다. 정답은 보내지 않는다 (독립 정답).
            var actualVerdict = await verdictService.GetVerdictAsync(testCase.Message);

            return new TestResult(testCase.CaseId, testCase.Message, testCase.ExpectedVerdict, actualVerdict);
        }
        catch (Exception exception) when (IsVerdictServiceFailure(exception))
        {
            return new TestResult(
                testCase.CaseId,
                testCase.Message,
                testCase.ExpectedVerdict,
                ActualVerdict: TestResult.SystemFailureVerdict,
                ErrorMessage: exception.Message);
        }
    }

    // 판정 대상 쪽 문제로 볼 예외인지 판단한다.
    // 연결 실패·오류 응답, 시간 초과, 응답 형식 오류만 시스템 실패로 기록하고,
    // 그 밖의 예외는 QA 러너 자체의 버그일 수 있으므로 그대로 드러나게 둔다.
    private static bool IsVerdictServiceFailure(Exception exception) => exception
        is HttpRequestException
        or TaskCanceledException
        or JsonException
        or VerdictServiceException;
}
