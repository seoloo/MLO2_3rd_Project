using QaRunner.Models;

namespace QaRunner.Verdicts;

/// <summary>
/// 백엔드 판정이 준비되기 전에 쓰는 연습용 판정기.
/// "예매 오픈 뒤 대기열 진입이 0.3초보다 빠른가"만 보고 판정하도록 일부러 엉성하게 만들었다.
/// 그래서 사람처럼 늦게 들어오는 봇 C(브라우저 흉내)와 봇 D(한 기기 다중 세션)를 놓치고,
/// 채점기가 이 놓침을 실패로 잡아내는지 확인할 수 있다.
/// </summary>
public sealed class FakeVerdictService : IVerdictService
{
    /// <summary>대기열 진입이 이 시간보다 빠르면 추가 확인으로 판정한다</summary>
    public const int FastEntryLimitMs = 300;

    public Task<string> GetVerdictAsync(SessionCase session, string runId, string sessionKey)
    {
        // 대기열 진입 행동이 없으면 첫 행동을 진입으로 본다
        var entrySteps = session.Steps.Where(step => step.Type == "queue_enter").ToList();
        var entryAtMs = (entrySteps.Count > 0 ? entrySteps : session.Steps).Min(step => step.AtMs);
        var verdict = entryAtMs < FastEntryLimitMs ? "추가 확인" : "정상";
        return Task.FromResult(verdict);
    }
}
