using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MuniClaw.Core.Contracts.Harness;
using MuniClaw.Worker.Configuration;

namespace MuniClaw.Worker.Harness;

public sealed class OpenCodeSupervisor : IOpenCodeSupervisor, IDisposable
{
    private readonly WorkerOptions _options;
    private readonly IOpenCodeProcessRunner _processRunner;
    private readonly ILogger<OpenCodeSupervisor> _logger;
    private readonly HttpClient _httpClient;
    private readonly OpenCodeHarnessAdapter _adapter;

    public string HarnessType => "OpenCode";
    public string PinnedVersion => "1.18.32";
    public string LoopbackHost => _options.OpenCodeHost;
    public int Port => _options.OpenCodePort;
    public bool IsRunning => _processRunner.IsRunning;
    public IHarnessAdapter Adapter => _adapter;

    public OpenCodeSupervisor(
        WorkerOptions options,
        IOpenCodeProcessRunner? processRunner = null,
        HttpClient? customHttpClient = null,
        ILogger<OpenCodeSupervisor>? logger = null)
    {
        _options = options;
        _logger = logger ?? NullLogger<OpenCodeSupervisor>.Instance;
        _processRunner = processRunner ?? new DefaultOpenCodeProcessRunner();

        // Enforce security rule: OpenCode must ONLY bind to loopback interface
        if (!IsLoopback(options.OpenCodeHost))
        {
            throw new InvalidOperationException(
                $"Security policy violation: OpenCode host must be loopback ('127.0.0.1' or 'localhost'), but got '{options.OpenCodeHost}'.");
        }

        _httpClient = customHttpClient ?? new HttpClient
        {
            BaseAddress = new Uri($"http://{_options.OpenCodeHost}:{_options.OpenCodePort}/")
        };

        _adapter = new OpenCodeHarnessAdapter(_httpClient, _options.OpenCodePassword);
    }

    public async Task StartAsync(CancellationToken ct)
    {
        _logger.LogInformation("Starting OpenCode supervisor (pinned v{Version}) on {Host}:{Port}",
            PinnedVersion, LoopbackHost, Port);

        var started = await _processRunner.StartServerAsync(
            _options.OpenCodeExecutablePath,
            _options.OpenCodeHost,
            _options.OpenCodePort,
            _options.OpenCodePassword,
            ct);

        if (!started)
        {
            _logger.LogWarning("OpenCode server failed to start or binary not present. Server status: not running.");
        }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        _logger.LogInformation("Stopping OpenCode supervisor...");
        await _processRunner.StopServerAsync(ct);
    }

    private static bool IsLoopback(string host)
    {
        if (string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
