using System.ComponentModel;
using System.Diagnostics;

namespace QaRunner.Pipeline;

/// <summary>
/// 복구 시험에서 Spark 컨테이너를 docker stop / docker start로 멈췄다 켠다 (docs/contract.md 6장).
/// QA 러너를 실행하는 PC에서 docker 명령을 쓸 수 있어야 한다.
/// 아직 실제 Spark 컨테이너로 확인하지 못한 코드다.
/// </summary>
/// <param name="containerName">멈췄다 켤 컨테이너 이름. 예: spark</param>
public sealed class DockerProcessingControl(string containerName) : IProcessingControl
{
    /// <summary>
    /// docker stop은 컨테이너가 스스로 멈추기를 최대 10초 기다린 뒤 강제로 끈다.
    /// 그래서 실제 중단 시작은 이 시간만큼 늦어질 수 있다.
    /// </summary>
    public Task StopProcessingAsync(CancellationToken cancellationToken) => RunDockerAsync("stop", cancellationToken);

    public Task StartProcessingAsync(CancellationToken cancellationToken) => RunDockerAsync("start", cancellationToken);

    // docker 명령을 실행하고, 실패하면 ProcessingControlException으로 알린다
    private async Task RunDockerAsync(string dockerCommand, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo("docker")
        {
            ArgumentList = { dockerCommand, containerName },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        try
        {
            using var dockerProcess = Process.Start(startInfo)
                ?? throw new ProcessingControlException("docker 명령을 시작하지 못했습니다.");

            var errorOutput = await dockerProcess.StandardError.ReadToEndAsync(cancellationToken);
            await dockerProcess.WaitForExitAsync(cancellationToken);

            if (dockerProcess.ExitCode != 0)
                throw new ProcessingControlException($"docker {dockerCommand} {containerName} 실패: {errorOutput.Trim()}");
        }
        catch (Win32Exception exception)
        {
            // docker가 설치되어 있지 않거나 PATH에 없을 때
            throw new ProcessingControlException("docker 명령을 찾을 수 없습니다. Docker가 설치되어 있는지 확인하세요.", exception);
        }
    }
}
