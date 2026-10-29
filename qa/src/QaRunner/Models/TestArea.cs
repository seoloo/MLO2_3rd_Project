namespace QaRunner.Models;

/// <summary>
/// 무엇을 점검하는 시험인지 나누는 큰 영역. 시험 종류(TestSuite)는 모두 이 중 한 영역에 속한다.
/// 보고서·대시보드·데스크탑 앱이 이 영역으로 시험을 묶어 보여 준다.
/// </summary>
public enum TestArea
{
    /// <summary>서비스: 판정 API와 Kafka·Spark·MongoDB 파이프라인 (판정·파이프라인·부하·복구 시험)</summary>
    Service,

    /// <summary>앱: React Native 앱. 지금은 가짜 판정 서버로 앱의 요청 형식과 오류·지연 처리를 확인한다</summary>
    App,

    /// <summary>QA 도구: 이 QA 러너 자체가 정답을 맞게 계산하고 틀린 결과를 잡아내는지</summary>
    QaTool,
}

public static class TestAreaExtensions
{
    /// <summary>보고서와 화면에 보여 줄 한글 이름</summary>
    public static string ToKoreanName(this TestArea testArea) => testArea switch
    {
        TestArea.Service => "서비스",
        TestArea.App => "앱",
        TestArea.QaTool => "QA 도구",
        _ => testArea.ToString(),
    };

    /// <summary>이 영역에서 무엇을 확인하는지 한 줄 설명</summary>
    public static string Describe(this TestArea testArea) => testArea switch
    {
        TestArea.Service => "판정 API와 데이터 파이프라인이 정확하고 빠르고 멈춰도 회복하는지 확인해요.",
        TestArea.App => "앱을 가짜 판정 서버에 연결해서, 앱이 계약대로 요청하고 오류·지연 응답에도 화면이 제대로 버티는지 확인해요.",
        TestArea.QaTool => "이 QA 도구가 정답을 맞게 계산하고, 틀린 결과를 빠짐없이 잡아내는지 확인해요.",
        _ => "",
    };

    /// <summary>지금 실행할 수 있는 시험이 있는 영역인지</summary>
    public static bool HasRunnableSuites(this TestArea testArea) =>
        Enum.GetValues<TestSuite>().Any(testSuite => testSuite.Area() == testArea);
}
