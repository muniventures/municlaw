using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MuniClaw.Worker.Harness;

public interface IOpenCodeProcessRunner
{
    bool IsRunning { get; }
    Task<bool> StartServerAsync(string executablePath, string host, int port, string serverPassword, CancellationToken ct);
    Task StopServerAsync(CancellationToken ct);
}

public sealed class DefaultOpenCodeProcessRunner : IOpenCodeProcessRunner
{
    private Process? _process;
    private readonly ILogger<DefaultOpenCodeProcessRunner> _logger;

    public bool IsRunning => _process != null && !_process.HasExited;

    public DefaultOpenCodeProcessRunner(ILogger<DefaultOpenCodeProcessRunner>? logger = null)
    {
        _logger = logger ?? NullLogger<DefaultOpenCodeProcessRunner>.Instance;
    }

    public Task<bool> StartServerAsync(string executablePath, string host, int port, string serverPassword, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = $"serve --port {port} --hostname {host}",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        psi.EnvironmentVariables["OPENCODE_SERVER_PASSWORD"] = serverPassword;

        try
        {
            _process = Process.Start(psi);
            if (_process == null)
            {
                _logger.LogError("Failed to start OpenCode process with executable {Path}", executablePath);
                return Task.FromResult(false);
            }

            _logger.LogInformation("Started OpenCode process (PID {Pid}) on {Host}:{Port}", _process.Id, host, port);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to launch OpenCode process {Path}", executablePath);
            return Task.FromResult(false);
        }
    }

    public Task StopServerAsync(CancellationToken ct)
    {
        if (_process != null && !_process.HasExited)
        {
            try
            {
                _logger.LogInformation("Stopping OpenCode process (PID {Pid})", _process.Id);
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(3000);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Exception while stopping OpenCode process");
            }
            finally
            {
                _process.Dispose();
                _process = null;
            }
        }

        return Task.CompletedTask;
    }
}
