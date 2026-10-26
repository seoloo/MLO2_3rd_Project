namespace QaRunner.Verdicts;

/// <summary>
/// 문자를 받아 판정을 돌려주는 대상, 즉 시험받는 쪽이다.
/// 지금은 FakeVerdictService를 쓰고, B의 FastAPI가 준비되면
/// API를 호출하는 구현을 새로 만들어 바꿔 끼운다.
/// </summary>
public interface IVerdictService
{
    /// <summary>
    /// 문자 하나를 판정한다.
    /// </summary>
    /// <param name="message">판정할 문자 내용</param>
    /// <returns>판정 결과. 고위험 / 주의 / 신호 미발견 / 판단 보류 중 하나</returns>
    Task<string> GetVerdictAsync(string message);
}
