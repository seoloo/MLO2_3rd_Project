namespace QaRunner.Models;

/// <summary>
/// 시험 케이스 한 건을 채점한 결과.
/// </summary>
/// <param name="CaseId">채점한 케이스 번호</param>
/// <param name="ExpectedVerdict">입력값 파일에 적혀 있던 정답 판정</param>
/// <param name="ActualVerdict">판정 대상이 실제로 돌려준 판정</param>
public sealed record TestResult(string CaseId, string ExpectedVerdict, string ActualVerdict)
{
    /// <summary>정답과 실제 판정이 같으면 통과다.</summary>
    public bool IsPassed => ExpectedVerdict == ActualVerdict;
}
