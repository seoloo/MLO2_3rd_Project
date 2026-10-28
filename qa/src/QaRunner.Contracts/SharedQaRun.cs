using System.Text.Json.Serialization;

namespace QaRunner.Contracts;

/// <summary>시험 실행 상태. 판정 불일치와 실행 오류를 서로 구분한다.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SharedRunState>))]
public enum SharedRunState { Running, Completed, Error }

/// <summary>팀 화면에 보여 줄 확인 항목. 실제 값이 없는 시스템 실패는 Error에 이유를 남긴다.</summary>
public sealed record SharedCheckResult(string CaseId, string Expected, string Actual, string? Error = null)
{
    public bool IsPassed => Error is null && Expected == Actual;
}

/// <summary>여러 PC가 공유하는 실행 스냅샷. RunId는 PC·시각에 관계없이 고유한 UUID다.</summary>
public sealed record SharedQaRun(
    Guid RunId, string ActorName, string EnvironmentName, string CodeVersion,
    SharedRunState State, DateTimeOffset StartedAt, DateTimeOffset UpdatedAt,
    string TargetName, GeneratedCaseSet CaseSet, IReadOnlyList<SharedCheckResult> Results,
    string? Error = null)
{
    public bool IsProvisional => CaseSet.IsProvisional;
    public int PassedCount => Results.Count(result => result.IsPassed);

    /// <summary>공유 경계에서 기본 형식을 검사한다. 인증·소유권 검증은 실제 서버를 연결할 때 추가한다.</summary>
    public void Validate()
    {
        if (RunId == Guid.Empty || string.IsNullOrWhiteSpace(ActorName)
            || string.IsNullOrWhiteSpace(EnvironmentName) || string.IsNullOrWhiteSpace(CodeVersion)
            || string.IsNullOrWhiteSpace(TargetName) || !Enum.IsDefined(State)
            || CaseSet is null || Results is null || Results.Any(result => result is null)
            || UpdatedAt < StartedAt)
            throw new ArgumentException("실행 ID·실행자·환경·버전·대상·상태와 유효한 시각이 필요합니다.");
        CaseSet.Validate();
    }
}
