using QaRunner.Models;

namespace QaRunner.Verdicts;

/// <summary>
/// 예매 세션의 행동을 받아 판정을 돌려주는 대상, 즉 시험받는 쪽이다.
/// 백엔드가 준비되기 전에는 FakeVerdictService(연습용 판정기)를 쓰고,
/// 준비되면 수집 API와 판정 조회 API를 부르는 ApiVerdictService로 바꿔 끼운다.
/// </summary>
public interface IVerdictService
{
    /// <summary>
    /// 세션 하나의 행동을 보내고 판정을 받는다. 정답(사람인지 봇인지)은 보내지 않는다.
    /// </summary>
    /// <param name="session">판정받을 세션 사례</param>
    /// <param name="runId">QA 실행 번호. 이벤트에 붙여 이번 실행 것만 골라낼 수 있게 한다</param>
    /// <param name="sessionKey">이번 요청에만 쓰는 고유 값. 세션 해시를 만든다 (같은 사례를 여러 번 보내도 겹치지 않게)</param>
    /// <returns>판정 결과. 정상 / 관찰 / 추가 확인 / 보류 중 하나</returns>
    Task<string> GetVerdictAsync(SessionCase session, string runId, string sessionKey);
}
