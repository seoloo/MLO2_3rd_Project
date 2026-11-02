using System.Text.Json.Serialization;

namespace QaRunner.Contracts;

/// <summary>AI가 제안한 라벨과 사람이 검수한 라벨을 구분한다. 모델의 확신도와는 다르다.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LabelReviewState>))]
public enum LabelReviewState { AiSuggested, HumanReviewed }

/// <summary>생성된 세션 안의 행동 하나. 이벤트 종류와 화면은 연결 계약 4-2장을 따른다.</summary>
public sealed record GeneratedStep(string Type, int AtMs, string? Screen = null, int? X = null, int? Y = null);

/// <summary>
/// AI가 생성하거나 검수자가 확정한 시험 세션과 예상 정답 (예: 새 유형 봇 시나리오).
/// 서비스 API에는 Steps(행동)만 보내고, 행위자와 정답은 QA 안에서만 쓴다.
/// </summary>
public sealed record GeneratedCase(
    string CaseId, string Actor, string Description, string ExpectedVerdict, IReadOnlyList<GeneratedStep> Steps,
    string Rationale, LabelReviewState ReviewState = LabelReviewState.AiSuggested)
{
    /// <summary>같은 기기에서 동시에 같은 행동을 하는 세션 수 (봇 D 같은 다중 세션)</summary>
    public int ParallelSessions { get; init; } = 1;
}

/// <summary>생성 결과를 고정해 다시 시험할 수 있도록 묶은 사례 집합. 판정 코드(NORMAL·WATCH·VERIFY·HOLD)는 연결 계약 2장을 따른다.</summary>
public sealed record GeneratedCaseSet(
    string DatasetId, string Generator, string PromptVersion, IReadOnlyList<GeneratedCase> Cases)
{
    /// <summary>검수되지 않은 라벨이 섞이면 공식 정확도가 아닌 탐색 시험으로 표시한다.</summary>
    public bool IsProvisional => Cases.Any(testCase => testCase.ReviewState != LabelReviewState.HumanReviewed);

    /// <summary>잘못된 AI 출력을 시험에 넣기 전에 확인한다. 정답 자체의 정확성은 보장하지 않는다.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(DatasetId) || string.IsNullOrWhiteSpace(Generator)
            || string.IsNullOrWhiteSpace(PromptVersion) || Cases is null || Cases.Count == 0)
            throw new ArgumentException("사례 집합에는 ID·생성기·프롬프트 버전과 사례가 필요합니다.");

        var caseIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var testCase in Cases)
        {
            if (testCase is null || string.IsNullOrWhiteSpace(testCase.CaseId)
                || string.IsNullOrWhiteSpace(testCase.Actor) || testCase.Steps is null || testCase.Steps.Count == 0
                || testCase.Steps.Any(step => step is null || string.IsNullOrWhiteSpace(step.Type) || step.AtMs < 0)
                || testCase.ParallelSessions is < 1 or > 100
                || !caseIds.Add(testCase.CaseId) || !Enum.IsDefined(testCase.ReviewState)
                || testCase.ExpectedVerdict is not ("NORMAL" or "WATCH" or "VERIFY" or "HOLD"))
                throw new ArgumentException("사례 ID는 고유해야 하며 행위자·행동·유효한 판정 코드·검수 상태가 필요합니다.");
        }
    }
}

/// <summary>향후 Runpod에 보낼 생성 조건 (예: "사람처럼 망설이는 새 유형 봇"). 정답 기준은 버전이 있는 프롬프트로 별도 관리한다.</summary>
public sealed record CaseGenerationRequest(string Scenario, int Count = 4);
