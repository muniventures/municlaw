using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MuniClaw.Core.Contracts.Harness;
using MuniClaw.Worker.Configuration;

namespace MuniClaw.Worker.Harness;

public sealed class ClaudeCodeSupervisor : IHarnessSupervisor, IDisposable
{
    private readonly ClaudeCodeHarnessAdapter _adapter;
    private readonly ILogger<ClaudeCodeSupervisor> _logger;
    private bool _isRunning;

    public string HarnessType => "ClaudeCode";
    public string PinnedVersion => "1.0.0";
    public bool IsRunning => _isRunning;
    public IHarnessAdapter Adapter => _adapter;
    public ClaudeCodeHarnessAdapter ClaudeAdapter => _adapter;

    public ClaudeCodeSupervisor(
        ClaudeCodeHarnessAdapter? adapter = null,
        ILogger<ClaudeCodeSupervisor>? logger = null)
    {
        _adapter = adapter ?? new ClaudeCodeHarnessAdapter();
        _logger = logger ?? NullLogger<ClaudeCodeSupervisor>.Instance;
    }

    public ClaudeCodeSupervisor(
        WorkerOptions options,
        ClaudeCodeHarnessAdapter? adapter = null,
        ILogger<ClaudeCodeSupervisor>? logger = null)
        : this(adapter, logger)
    {
    }

    public Task StartAsync(CancellationToken ct)
    {
        _isRunning = true;
        _logger.LogInformation("Started Claude Code supervisor (harness {Type}, pinned v{Version})",
            HarnessType, PinnedVersion);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _isRunning = false;
        _logger.LogInformation("Stopped Claude Code supervisor");
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _isRunning = false;
        _adapter.Dispose();
    }
}
