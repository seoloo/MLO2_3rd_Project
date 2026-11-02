using QaRunner.Models;
using QaRunner.Pipeline;

namespace QaRunner.Verdicts;

/// <summary>
/// 세션 사례 하나를 계약 형식의 행동 이벤트들로 펼친 결과.
/// </summary>
/// <param name="PrimarySessionHash">판정을 조회할 세션(첫 세션)의 해시</param>
/// <param name="Events">보낼 이벤트 전부 (다중 세션이면 세션마다 같은 행동이 들어 있다)</param>
public sealed record SessionEvents(string PrimarySessionHash, IReadOnlyList<ContractEvent> Events);

/// <summary>
/// 세션 사례를 수집 API·Kafka에 보낼 이벤트로 만든다 (docs/contract.md 4장).
/// 사례 파일의 시각은 "오픈 후 몇 밀리초"라서, 오픈 시각을 정해 실제 시각으로 바꾼다.
/// </summary>
public static class SessionEventBuilder
{
    /// <summary>다중 세션을 만들 때 세션마다 이만큼씩 시각을 어긋나게 한다 (같은 기기에서 거의 동시에 들어온 모양)</summary>
    public const int ParallelSessionGapMs = 7;

    /// <summary>
    /// 세션 사례를 이벤트로 펼친다. 세션 해시·기기 해시·이벤트 번호는 sessionKey로 만들어 실행마다 겹치지 않는다.
    /// </summary>
    /// <param name="session">펼칠 세션 사례</param>
    /// <param name="runId">QA 실행 번호</param>
    /// <param name="sessionKey">이번 요청의 고유 값</param>
    /// <param name="openAt">예매 오픈 시각. 행동 시각은 이 시각 + atMs</param>
    public static SessionEvents Build(SessionCase session, string runId, string sessionKey, DateTimeOffset openAt)
    {
        var deviceHash = $"qa-device-{sessionKey}";
        var sessionCount = Math.Max(1, session.ParallelSessions);
        var events = new List<ContractEvent>();
        string? primarySessionHash = null;

        for (var sessionIndex = 0; sessionIndex < sessionCount; sessionIndex++)
        {
            var sessionHash = sessionCount == 1 ? $"qa-{sessionKey}" : $"qa-{sessionKey}-{sessionIndex + 1:00}";
            primarySessionHash ??= sessionHash;

            var orderedSteps = session.Steps.OrderBy(step => step.AtMs).ToList();
            for (var stepIndex = 0; stepIndex < orderedSteps.Count; stepIndex++)
            {
                var step = orderedSteps[stepIndex];
                events.Add(new ContractEvent(
                    EventId: $"{sessionHash}-{stepIndex + 1:000}",
                    Type: step.Type,
                    SessionHash: sessionHash,
                    DeviceHash: deviceHash,
                    ShowId: ContractEventFormat.QaShowId,
                    OccurredAt: openAt.AddMilliseconds(step.AtMs + sessionIndex * ParallelSessionGapMs),
                    RunId: runId,
                    CaseId: session.CaseId,
                    Screen: step.Screen,
                    X: step.X,
                    Y: step.Y));
            }
        }

        return new SessionEvents(primarySessionHash!, events);
    }

    /// <summary>
    /// 지금 보내도 모든 행동이 이미 일어난 과거가 되도록 오픈 시각을 정한다.
    /// 마지막 행동이 1초 전에 끝난 것으로 맞추며, 세션이 5분보다 짧으면 늦은 도착 격리에도 걸리지 않는다.
    /// </summary>
    public static DateTimeOffset OpenTimeEndingNow(SessionCase session, DateTimeOffset now) =>
        now.AddMilliseconds(-(session.Steps.Max(step => step.AtMs) + 1000));
}
