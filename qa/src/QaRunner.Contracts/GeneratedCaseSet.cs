using System.Text.Json.Serialization;

namespace QaRunner.Contracts;

/// <summary>AI가 제안한 라벨과 사람이 검수한 라벨을 구분한다. 모델의 확신도와는 다르다.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LabelReviewState>))]
public enum LabelReviewState { AiSuggested, HumanReviewed }

/// <summary>AI가 생성하거나 검수자가 확정한 시험 문자와 예상 정답. 서비스 API에는 Message만 보낸다.</summary>
public sealed record GeneratedCase(
    string CaseId, string Message, string ExpectedVerdict,
    string Rationale, LabelReviewState ReviewState = LabelReviewState.AiSuggested);

/// <summary>생성 결과를 고정해 다시 시험할 수 있도록 묶은 사례 집합. 판정 코드는 연결 계약 2장을 따른다.</summary>
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
                || string.IsNullOrWhiteSpace(testCase.Message) || !caseIds.Add(testCase.CaseId)
                || !Enum.IsDefined(testCase.ReviewState)
                || testCase.ExpectedVerdict is not ("HIGH_RISK" or "CAUTION" or "NO_SIGNAL" or "UNDECIDED"))
                throw new ArgumentException("사례 ID는 고유해야 하며 문자·유효한 판정 코드·검수 상태가 필요합니다.");
        }
    }
}

/// <summary>향후 Runpod에 보낼 생성 조건. 정답 기준은 버전이 있는 프롬프트로 별도 관리한다.</summary>
public sealed record CaseGenerationRequest(string Scenario, int Count = 4);
