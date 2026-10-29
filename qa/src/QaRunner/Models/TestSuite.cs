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

    /// <summary>부하 시험: 정해진 속도로 계속 보내면서 API 응답 시간·Gold 반영 시간(p95)을 잼</summary>
    Load,

    /// <summary>복구 시험: 보내는 도중 처리를 멈췄다 재가동하고, 다시 따라잡기까지 걸린 시간을 잼</summary>
    Recovery,

    /// <summary>QA 도구 점검: 답을 이미 아는 입력으로 QA 러너의 채점기·정답 계산기·보고서가 맞게 동작하는지 확인</summary>
    SelfCheck,

    /// <summary>가짜 판정 서버: 앱(A)이 붙을 가짜 판정 API를 켜 두고, 앱이 보낸 요청이 계약대로인지 기록</summary>
    MockApi,
}

public static class TestSuiteExtensions
{
    /// <summary>보고서와 콘솔에 보여 줄 한글 이름</summary>
    public static string ToKoreanName(this TestSuite testSuite) => testSuite switch
    {
        TestSuite.Verdict => "판정 시험",
        TestSuite.Pipeline => "파이프라인 시험",
        TestSuite.Load => "부하 시험",
        TestSuite.Recovery => "복구 시험",
        TestSuite.SelfCheck => "QA 도구 점검",
        TestSuite.MockApi => "가짜 판정 서버",
        _ => testSuite.ToString(),
    };

    /// <summary>이 시험이 속한 영역. QA 도구 점검은 QA 도구, 가짜 판정 서버는 앱, 나머지는 서비스 영역이다</summary>
    public static TestArea Area(this TestSuite testSuite) => testSuite switch
    {
        TestSuite.SelfCheck => TestArea.QaTool,
        TestSuite.MockApi => TestArea.App,
        _ => TestArea.Service,
    };

    /// <summary>
    /// --suite 옵션 값을 시험 종류로 바꾼다. 값이 없으면 판정 시험.
    /// 알 수 없는 값이면 ArgumentException을 던진다.
    /// </summary>
    public static TestSuite ParseOptionValue(string? suiteValue) => suiteValue?.ToLowerInvariant() switch
    {
        null or "verdict" => TestSuite.Verdict,
        "pipeline" => TestSuite.Pipeline,
        "load" => TestSuite.Load,
        "recovery" => TestSuite.Recovery,
        "selfcheck" => TestSuite.SelfCheck,
        "mockapi" => TestSuite.MockApi,
        _ => throw new ArgumentException($"--suite 값은 verdict, pipeline, load, recovery, selfcheck, mockapi 중 하나여야 합니다: {suiteValue}"),
    };

    /// <summary>부하·복구처럼 시간을 재는 시험인지</summary>
    public static bool IsMeasurementSuite(this TestSuite testSuite) =>
        testSuite is TestSuite.Load or TestSuite.Recovery;

    /// <summary>입력값 파일이나 시나리오 폴더를 쓰지 않는 시험인지 (복구 시험, QA 도구 점검, 가짜 판정 서버)</summary>
    public static bool UsesNoCasesPath(this TestSuite testSuite) =>
        testSuite is TestSuite.Recovery or TestSuite.SelfCheck or TestSuite.MockApi;
}
