namespace QaRunner.Verdicts;

/// <summary>
/// FastAPI가 준비되기 전에 쓰는 임시 판정 대상.
/// "인증번호"라는 단어만 보고 판정하도록 일부러 엉성하게 만들어서,
/// 채점기가 틀린 판정을 제대로 잡아내는지 확인하는 데 쓴다.
/// </summary>
public sealed class FakeVerdictService : IVerdictService
{
    public Task<string> GetVerdictAsync(string message)
    {
        var verdict = message.Contains("인증번호") ? "고위험" : "신호 미발견";
        return Task.FromResult(verdict);
    }
}
