using QaRunner.Sharing.Api;

// 로컬에서 데이터 계약을 확인하는 서버 뼈대. EC2에는 아직 배포하지 않는다.
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5085");
builder.Services.AddSingleton<ITeamRunStore, InMemoryTeamRunStore>();

var app = builder.Build();
app.MapGet("/health", () => Results.Ok(new { mode = "local-prototype", storage = "memory" }));
app.MapTeamRunEndpoints();
app.Run();

// TODO: EC2 배포 전 HTTPS·인증·권한·영구 저장·페이지 조회·보존 정책·로그 마스킹을 구현한다.
// TODO: 실제 공유는 실행자별 접근과 시험 환경 격리를 설계한 뒤 루프백 바인딩을 변경한다.
