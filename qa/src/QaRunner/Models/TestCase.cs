namespace QaRunner.Models;

/// <summary>
/// 시험 케이스 한 건. 입력값 파일(cases/rules.json)의 한 줄에 해당한다.
/// </summary>
/// <param name="CaseId">케이스 번호. 실패했을 때 어떤 케이스인지 찾는 데 쓴다. 예: R-001</param>
/// <param name="Message">판정 대상에 넣을 문자 내용 (입력값)</param>
/// <param name="ExpectedVerdict">사람이 미리 정해 둔 정답 판정. 예: 고위험</param>
public sealed record TestCase(string CaseId, string Message, string ExpectedVerdict);
