namespace QaRunner.Models;

/// <summary>
/// 어떤 종류의 시험을 돌릴지. --suite 옵션으로 고른다.
/// </summary>
public enum TestSuite
{
    /// <summary>판정 시험: 문자를 넣고 판정(고위험·주의 등)을 정답과 비교</summary>
    Verdict,

    /// <summary>파이프라인 시험: 이벤트를 넣고 Bronze·Silver·Gold 집계를 정답과 비교</summary>
    Pipeline,
}

public static class TestSuiteExtensions
{
    /// <summary>보고서와 콘솔에 보여 줄 한글 이름</summary>
    public static string ToKoreanName(this TestSuite testSuite) => testSuite switch
    {
        TestSuite.Verdict => "판정 시험",
        TestSuite.Pipeline => "파이프라인 시험",
        _ => testSuite.ToString(),
    };
}
