namespace QaRunner.Contracts;

/// <summary>Runpod과 예제 생성기를 교체하는 경계. AI 라벨은 기본적으로 검수 전 제안이다.</summary>
public interface ICaseGenerator
{
    Task<GeneratedCaseSet> GenerateAsync(CaseGenerationRequest request, CancellationToken cancellationToken = default);
}
