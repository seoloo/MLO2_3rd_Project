using QaRunner.Models;

namespace QaRunner.Desktop;

/// <summary>
/// 결과 표의 한 줄. 채점 결과(TestResult)를 화면에 보여 주기 좋은 모양으로 바꾼 것이다.
/// </summary>
/// <param name="CaseId">케이스 번호</param>
/// <param name="CheckName">확인 항목</param>
/// <param name="Input">넣은 것 (세션 또는 시나리오 설명)</param>
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
    public bool IsSystemFailure { get; init; }
    public bool HasThreshold { get; init; }
    public string ResultLabel => IsSystemFailure ? "응답 없음" : IsPassed ? "통과" : HasThreshold ? "기준 미달" : "불일치";

    /// <summary>결과 표에서 한눈에 비교하도록 "예상 → 실제"로 줄인 말. 실제 값은 첫 줄만 쓴다</summary>
    public string ComparisonText => $"{Expected} → {ActualDisplay.Split('\n')[0]}";
    public string CheckDescription => CheckName.Contains("p95") ? "95%의 요청이나 이벤트가 이 시간 안에 끝났다는 뜻이에요. 나머지 5%는 더 오래 걸릴 수 있어요."
        : CheckName.Contains("오류율") ? "전체 요청 중 오류가 난 비율이에요. 낮을수록 좋아요."
        : CheckName.Contains("Silver") ? "중복을 제외하고 남은 고유 이벤트 건수예요."
        : CheckName.Contains("Bronze") ? "처음 받은 원본 이벤트를 보관한 건수예요."
        : CheckName.Contains("격리") ? "조건에 맞지 않아 정상 집계에서 따로 뺀 이벤트 건수예요."
        : CheckName == QaRunner.AppMock.MockRequestGrader.RequestCheckName
            ? "앱이 보낸 요청이 연결 계약 3~5장(경로·요청 방식·Content-Type: application/json·필수 필드)과 같은지 봤어요."
        : CheckName == QaRunner.AppMock.MockRequestGrader.NoRequestCheckName
            ? "서버를 켜 둔 동안 앱이 요청을 한 번도 보내지 않았어요. 앱의 API 주소와 포트를 확인해 주세요."
        : CheckName.Contains("탐지율") ? "봇 세션 중 추가 확인 이상(추가 확인·보류)으로 판정된 비율이에요. 높을수록 좋아요."
        : CheckName.Contains("오탐률") ? "사람 세션을 추가 확인 이상으로 잘못 판정한 비율이에요. 이 시스템에서 가장 피해야 할 실수라 낮을수록 좋아요."
        : "";

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
            testResult.IsPassed)
        {
            IsSystemFailure = testResult.IsSystemFailure,
            // 판정은 묶음으로 채점하려고 MeetsTarget을 쓰지만 "기준 미달"이 아니라 "불일치"로 보여 준다
            HasThreshold = testResult.MeetsTarget is not null && testResult.CheckName != VerdictChecker.CheckName,
        };
    }
}
