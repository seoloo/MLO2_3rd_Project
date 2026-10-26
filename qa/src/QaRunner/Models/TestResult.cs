namespace QaRunner.Models;

/// <summary>
/// 시험 케이스 한 건을 채점한 결과.
/// </summary>
/// <param name="CaseId">채점한 케이스 번호</param>
/// <param name="Message">판정 대상에 넣었던 문자. 실패했을 때 어떤 입력이었는지 보고서에서 바로 확인하는 용도</param>
/// <param name="ExpectedVerdict">입력값 파일에 적혀 있던 정답 판정</param>
/// <param name="ActualVerdict">판정 대상이 실제로 돌려준 판정. 판정을 못 받았으면 "시스템 실패"</param>
/// <param name="ErrorMessage">판정을 못 받은 이유 (API 연결 실패, 시간 초과 등). 정상이면 null</param>
public sealed record TestResult(
    string CaseId,
    string Message,
    string ExpectedVerdict,
    string ActualVerdict,
    string? ErrorMessage = null)
{
    /// <summary>
    /// 판정 대상이 판정을 돌려주지 못했을 때 실제 판정 자리에 넣는 값.
    /// 실행안대로 판정 결과(고위험·주의 등)와 섞이지 않게 따로 표시한다.
    /// </summary>
    public const string SystemFailureVerdict = "시스템 실패";

    /// <summary>정답과 실제 판정이 같으면 통과다. 시스템 실패는 항상 실패다.</summary>
    public bool IsPassed => ErrorMessage is null && ExpectedVerdict == ActualVerdict;

    /// <summary>판정 대상이 판정을 돌려주지 못한 경우인지</summary>
    public bool IsSystemFailure => ErrorMessage is not null;
}
