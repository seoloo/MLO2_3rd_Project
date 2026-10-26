using QaRunner.Models;

namespace QaRunner.Desktop;

/// <summary>
/// 결과 표의 한 줄. 채점 결과(TestResult)를 화면에 보여 주기 좋은 모양으로 바꾼 것이다.
/// </summary>
/// <param name="CaseId">케이스 번호</param>
/// <param name="CheckName">확인 항목</param>
/// <param name="Input">넣은 것 (문자 또는 시나리오 설명)</param>
/// <param name="Expected">정답</param>
/// <param name="ActualDisplay">실제 값. 시스템 실패면 이유를 다음 줄에 붙인다</param>
/// <param name="IsPassed">통과 여부. 실패한 줄을 붉게 칠하는 데 쓴다</param>
public sealed record ResultRow(
    string CaseId,
    string CheckName,
    string Input,
    string Expected,
    string ActualDisplay,
    bool IsPassed)
{
    /// <summary>결과 칸에 보여 줄 말</summary>
    public string ResultLabel => IsPassed ? "통과" : "실패";

    /// <summary>
    /// 채점 결과 하나를 표의 한 줄로 바꾼다.
    /// </summary>
    public static ResultRow From(TestResult testResult)
    {
        var actualDisplay = testResult.IsSystemFailure
            ? $"{testResult.Actual}\n{testResult.ErrorMessage}"
            : testResult.Actual;

        return new ResultRow(
            testResult.CaseId,
            testResult.CheckName,
            testResult.Input,
            testResult.Expected,
            actualDisplay,
            testResult.IsPassed);
    }
}
