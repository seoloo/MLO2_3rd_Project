using QaRunner.Contracts;

namespace QaRunner.Collaboration;

/// <summary>외부 AI 없이 화면과 데이터 흐름을 확인하는 합성 사례 생성기. 라벨은 검수 전으로 표시한다.</summary>
public sealed class DemoCaseGenerator : ICaseGenerator
{
    public Task<GeneratedCaseSet> GenerateAsync(CaseGenerationRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(request.Scenario) || request.Count is < 1 or > 4)
            throw new ArgumentException("예제 생성은 시나리오와 1~4개의 사례 수를 받습니다.");

        GeneratedCase[] examples =
        [
            new("DEMO-001", "상담원에게 인증번호를 알려주세요.", "HIGH_RISK", "인증번호 타인 전달 요구"),
            new("DEMO-002", "인증번호는 누구에게도 알려주지 마세요.", "NO_SIGNAL", "전달 요구가 아닌 예방 안내"),
            new("DEMO-003", "환불을 받으려면 링크에서 보안 앱을 설치하세요.", "HIGH_RISK", "외부 앱 설치 요구"),
            new("DEMO-004", "본인 확인이 필요합니다.", "UNDECIDED", "추가 상황이 없는 정보 부족 예제"),
        ];
        return Task.FromResult(new GeneratedCaseSet(
            Guid.NewGuid().ToString("N"), "DemoCaseGenerator (AI 호출 없음)", "demo-v1",
            examples.Take(request.Count).ToArray()));
    }
}
