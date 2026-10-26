namespace QaRunner.Models;

/// <summary>
/// 확인 항목 하나를 채점한 결과. 판정 시험과 파이프라인 시험이 같은 모양을 써서
/// 콘솔 출력과 보고서를 한 가지 방식으로 만든다.
/// </summary>
/// <param name="CaseId">채점한 케이스 번호. 실행 전체를 보는 항목(Gold 구간 집계)은 "전체"</param>
/// <param name="CheckName">무엇을 확인했는지. 예: 판정, Silver 고유 건수, Gold 14:30 구간 건수</param>
/// <param name="Input">무엇을 넣었는지. 판정 시험은 문자, 파이프라인 시험은 시나리오 설명</param>
/// <param name="Expected">정답 (입력값 파일에 적힌 값, 또는 정답 계산기가 계산한 값)</param>
/// <param name="Actual">시험 대상이 실제로 낸 값. 값을 못 받았으면 "시스템 실패"</param>
/// <param name="ErrorMessage">값을 못 받은 이유 (API 연결 실패, 시간 초과 등). 정상이면 null</param>
public sealed record TestResult(
    string CaseId,
    string CheckName,
    string Input,
    string Expected,
    string Actual,
    string? ErrorMessage = null)
{
    /// <summary>
    /// 시험 대상이 값을 돌려주지 못했을 때 실제 값 자리에 넣는 말.
    /// 실행안대로 판정 결과(고위험·주의 등)와 섞이지 않게 따로 표시한다.
    /// </summary>
    public const string SystemFailureValue = "시스템 실패";

    /// <summary>실행 전체를 대상으로 하는 확인 항목의 케이스 번호</summary>
    public const string WholeRunCaseId = "전체";

    /// <summary>정답과 실제 값이 같으면 통과다. 시스템 실패는 항상 실패다.</summary>
    public bool IsPassed => ErrorMessage is null && Expected == Actual;

    /// <summary>시험 대상이 값을 돌려주지 못한 경우인지</summary>
    public bool IsSystemFailure => ErrorMessage is not null;

    /// <summary>
    /// 값을 못 받은 경우의 결과를 만든다.
    /// </summary>
    public static TestResult SystemFailure(string caseId, string checkName, string input, string expected, string errorMessage) =>
        new(caseId, checkName, input, expected, SystemFailureValue, errorMessage);
}
