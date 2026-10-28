using QaRunner.Contracts;

namespace QaRunner.Collaboration;

/// <summary>향후 모델·프롬프트를 확정한 뒤 연결할 Runpod 생성기 자리. 현재 네트워크 요청은 하지 않는다.</summary>
public sealed class RunpodCaseGenerator : ICaseGenerator
{
    public Task<GeneratedCaseSet> GenerateAsync(CaseGenerationRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // TODO: 환경 변수로 API 키 읽기 → 생성 작업 제출 → 완료 조회 → JSON 파싱·검증.
        // TODO: 모델·프롬프트 버전 기록, 중복 제거, 재시도 한도·비용 한도, 합성 입력 정책 확정.
        // AI 출력은 HumanReviewed로 자동 승격하지 않는다. 검수 기준과 승인 경로는 별도 구현한다.
        throw new NotSupportedException("Runpod 연결은 준비 단계입니다. 현재는 DemoCaseGenerator로 구조만 확인하세요.");
    }
}
