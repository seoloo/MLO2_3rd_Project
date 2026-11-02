using System.Text.Json;
using QaRunner.Models;
using QaRunner.Verdicts;

namespace QaRunner;

/// <summary>
/// 판정 시험 채점기. 정답을 아는 세션마다 행동을 판정 대상에 보내고,
/// 돌아온 판정의 묶음(정상 처리 / 추가 확인 이상)을 정답의 묶음과 비교한다.
/// </summary>
/// <param name="verdictService">세션을 판정해 줄 대상 (연습용 판정기 또는 실제 API)</param>
public sealed class VerdictChecker(IVerdictService verdictService)
{
    /// <summary>판정 시험 결과의 확인 항목 이름</summary>
    public const string CheckName = "판정";

    /// <summary>
    /// 모든 세션 사례를 차례로 채점하고 결과 목록을 돌려준다.
    /// 한 사례에서 API 오류가 나도 멈추지 않고 "시스템 실패"로 기록한 뒤 다음 사례로 넘어간다.
    /// </summary>
    /// <param name="sessionCases">채점할 세션 사례 목록</param>
    /// <param name="runId">이번 QA 실행 번호. 세션 해시에 붙어 실행마다 겹치지 않는다</param>
    public async Task<List<TestResult>> CheckAllAsync(IEnumerable<SessionCase> sessionCases, string runId)
    {
        var testResults = new List<TestResult>();

        foreach (var sessionCase in sessionCases)
        {
            testResults.Add(await CheckOneAsync(sessionCase, runId));
        }

        return testResults;
    }

    /// <summary>보고서 정답 칸에 쓰는 말. 예: "추가 확인 이상 (추가 확인)"</summary>
    public static string DescribeExpected(SessionCase sessionCase) =>
        $"{VerdictCodes.ToGroup(sessionCase.ExpectedVerdict)} ({VerdictCodes.ToKorean(sessionCase.ExpectedVerdict)})";

    /// <summary>보고서 입력 칸에 쓰는 말. 예: "봇 A · 오픈 0.03초 뒤 진입, 0.1초 간격 일정"</summary>
    public static string DescribeInput(SessionCase sessionCase) =>
        $"{sessionCase.Actor} · {sessionCase.Description}"
        + (sessionCase.ParallelSessions > 1 ? $" (같은 기기 세션 {sessionCase.ParallelSessions}개)" : "");

    // 사례 하나를 채점한다. 판정을 못 받으면 시스템 실패 결과를 만든다
    private async Task<TestResult> CheckOneAsync(SessionCase sessionCase, string runId)
    {
        var input = DescribeInput(sessionCase);
        var expected = DescribeExpected(sessionCase);
        try
        {
            // 판정 대상에는 행동만 보낸다. 행위자와 정답은 보내지 않는다 (독립 정답).
            var actualVerdict = await verdictService.GetVerdictAsync(sessionCase, runId, $"{runId}-{sessionCase.CaseId}");

            // 같은 묶음이면 통과다. 사람이 "관찰"이어도, 봇이 "보류"여도 기획서의 탐지·오탐 기준으로는 맞다
            var sameGroup = VerdictCodes.ToGroup(actualVerdict) is { } actualGroup
                && actualGroup == VerdictCodes.ToGroup(sessionCase.ExpectedVerdict);
            return new TestResult(sessionCase.CaseId, CheckName, input, expected, actualVerdict, MeetsTarget: sameGroup);
        }
        catch (Exception exception) when (IsVerdictServiceFailure(exception))
        {
            return TestResult.SystemFailure(sessionCase.CaseId, CheckName, input, expected, exception.Message);
        }
    }

    /// <summary>
    /// 판정 대상 쪽 문제로 볼 예외인지 판단한다. (부하 시험에서도 같은 기준으로 오류를 센다)
    /// 연결 실패·오류 응답, 시간 초과, 응답 형식 오류만 시스템 실패로 기록하고,
    /// 그 밖의 예외는 QA 러너 자체의 버그일 수 있으므로 그대로 드러나게 둔다.
    /// </summary>
    public static bool IsVerdictServiceFailure(Exception exception) => exception
        is HttpRequestException
        or TaskCanceledException
        or JsonException
        or VerdictServiceException;
}
