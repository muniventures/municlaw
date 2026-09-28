using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MuniClaw.Core.Contracts.Harness;
using MuniClaw.Core.Contracts.Tasks;

namespace MuniClaw.Worker.Harness;

public sealed class ClaudeCodeHarnessAdapter : IHarnessAdapter, IDisposable
{
    private sealed class SessionContext
    {
        public required string SessionId { get; init; }
        public required string WorkingDirectory { get; init; }
        public Process? Process { get; set; }
        public CancellationTokenSource Cts { get; } = new();
    }

    private readonly string? _executablePath;
    private readonly ILogger<ClaudeCodeHarnessAdapter> _logger;
    private readonly ConcurrentDictionary<string, SessionContext> _sessions = new();
    private readonly ConcurrentDictionary<string, IReadOnlyList<HarnessDiffItem>> _mockDiffs = new();
    private readonly Channel<HarnessRawEvent> _eventChannel = Channel.CreateUnbounded<HarnessRawEvent>();

    public TimeSpan LastMeasuredAbortLatency { get; private set; } = TimeSpan.Zero;
    public string PinnedVersion => "1.0.0";

    public ClaudeCodeHarnessAdapter(
        string? executablePath = null,
        ILogger<ClaudeCodeHarnessAdapter>? logger = null)
    {
        _executablePath = executablePath;
        _logger = logger ?? NullLogger<ClaudeCodeHarnessAdapter>.Instance;
    }

    public Task<HarnessSessionInfo> CreateSessionAsync(string workingDirectory, CancellationToken ct)
    {
        var sessionId = Guid.NewGuid().ToString("N");
        var session = new SessionContext
        {
            SessionId = sessionId,
            WorkingDirectory = workingDirectory
        };

        _sessions[sessionId] = session;

        _logger.LogInformation("Created Claude Code session {SessionId} bound to {Directory}",
            sessionId, workingDirectory);

        return Task.FromResult(new HarnessSessionInfo
        {
            SessionId = sessionId,
            Slug = $"claude-{sessionId[..8]}",
            Version = PinnedVersion,
            Directory = workingDirectory,
            CreatedAt = DateTimeOffset.UtcNow
        });
    }

    public async Task SendPromptAsync(string sessionId, HarnessPrompt prompt, CancellationToken ct)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            throw new KeyNotFoundException($"Claude Code session {sessionId} was not found.");
        }

        var promptText = string.Join("\n", prompt.Parts.Select(p => p.Text));

        if (!string.IsNullOrEmpty(_executablePath) && File.Exists(_executablePath))
        {
            await RunCliProcessAsync(session, promptText, ct);
        }
        else
        {
            await EmitMockEventsAsync(session, promptText, ct);
        }
    }

    public async Task<IReadOnlyList<HarnessDiffItem>> GetDiffAsync(string sessionId, CancellationToken ct)
    {
        if (_mockDiffs.TryGetValue(sessionId, out var mockDiff))
        {
            return mockDiff;
        }

        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            return Array.Empty<HarnessDiffItem>();
        }

        var workingDir = session.WorkingDirectory;
        if (!Directory.Exists(workingDir))
        {
            return Array.Empty<HarnessDiffItem>();
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "diff --numstat",
                WorkingDirectory = workingDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc != null)
            {
                var output = await proc.StandardOutput.ReadToEndAsync(ct);
                await proc.WaitForExitAsync(ct);

                if (proc.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    var items = new List<HarnessDiffItem>();
                    var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        var parts = line.Split('\t', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 3)
                        {
                            int.TryParse(parts[0], out var adds);
                            int.TryParse(parts[1], out var dels);
                            var path = parts[2].Trim();
                            var changeType = (adds > 0 && dels == 0) ? "added" : (adds == 0 && dels > 0) ? "deleted" : "modified";
                            items.Add(new HarnessDiffItem
                            {
                                Path = path,
                                ChangeType = changeType,
                                Additions = adds,
                                Deletions = dels
                            });
                        }
                    }

                    if (items.Count > 0)
                    {
                        return items;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to run git diff in {Directory}", workingDir);
        }

        return Array.Empty<HarnessDiffItem>();
    }

    public Task<bool> AbortAsync(string sessionId, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                session.Cts.Cancel();

                if (session.Process != null && !session.Process.HasExited)
                {
                    try
                    {
                        session.Process.Kill(entireProcessTree: true);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to kill Claude Code process for session {SessionId}", sessionId);
                    }
                }
            }

            return Task.FromResult(true);
        }
        finally
        {
            sw.Stop();
            LastMeasuredAbortLatency = sw.Elapsed;
            _logger.LogInformation("Claude Code abort call for session {SessionId} completed in {LatencyMs:F2}ms (bound: <100ms)",
                sessionId, LastMeasuredAbortLatency.TotalMilliseconds);
        }
    }

    public async IAsyncEnumerable<HarnessRawEvent> SubscribeEventsAsync([EnumeratorCancellation] CancellationToken ct)
    {
        while (await _eventChannel.Reader.WaitToReadAsync(ct))
        {
            while (_eventChannel.Reader.TryRead(out var evt))
            {
                yield return evt;
            }
        }
    }

    public void EnqueueRawEvent(string rawEventType, string payloadJson, string? sessionId = null)
    {
        var canonical = ClaudeCodeEventMapper.MapToCanonical(rawEventType);
        var rawEvt = new HarnessRawEvent
        {
            EventType = rawEventType,
            SessionId = sessionId,
            PayloadJson = payloadJson,
            Timestamp = DateTimeOffset.UtcNow
        };
        _eventChannel.Writer.TryWrite(rawEvt);
    }

    public void SetMockDiff(string sessionId, IReadOnlyList<HarnessDiffItem> diffItems)
    {
        _mockDiffs[sessionId] = diffItems;
    }

    private async Task RunCliProcessAsync(SessionContext session, string promptText, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = _executablePath!,
            Arguments = $"--non-interactive -p \"{promptText.Replace("\"", "\\\"")}\"",
            WorkingDirectory = session.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, session.Cts.Token);

        try
        {
            var proc = Process.Start(psi);
            if (proc == null)
            {
                _logger.LogError("Failed to launch Claude Code process {Path}", _executablePath);
                return;
            }

            session.Process = proc;

            _ = Task.Run(async () =>
            {
                using var reader = proc.StandardOutput;
                while (!linkedCts.Token.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(linkedCts.Token);
                    if (line == null) break;
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    var rawEvt = new HarnessRawEvent
                    {
                        EventType = "text_delta",
                        SessionId = session.SessionId,
                        PayloadJson = $"{{\"text\":{System.Text.Json.JsonSerializer.Serialize(line)}}}",
                        Timestamp = DateTimeOffset.UtcNow
                    };
                    _eventChannel.Writer.TryWrite(rawEvt);
                }
            }, linkedCts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception running Claude Code CLI process");
        }
    }

    private async Task EmitMockEventsAsync(SessionContext session, string promptText, CancellationToken ct)
    {
        var events = new (string EventType, string Payload)[]
        {
            ("thought_delta", $"{{\"thought\":\"Analyzing requirement: {promptText.Replace("\"", "\\\"")}\"}}"),
            ("text_delta", $"{{\"text\":\"Executing instructions in {session.WorkingDirectory.Replace("\\", "/")}...\"}}"),
            ("tool_call", "{\"tool\":\"bash\",\"input\":\"git status\"}"),
            ("tool_result", "{\"output\":\"Working tree clean\"}"),
            ("diff", $"{{\"summary\":\"Generated changes for session {session.SessionId}\"}}")
        };

        foreach (var (evtType, payload) in events)
        {
            if (ct.IsCancellationRequested || session.Cts.IsCancellationRequested)
            {
                break;
            }

            var rawEvt = new HarnessRawEvent
            {
                EventType = evtType,
                SessionId = session.SessionId,
                PayloadJson = payload,
                Timestamp = DateTimeOffset.UtcNow
            };
            _eventChannel.Writer.TryWrite(rawEvt);
            await Task.Yield();
        }
    }

    public void Dispose()
    {
        foreach (var session in _sessions.Values)
        {
            session.Cts.Cancel();
            if (session.Process != null && !session.Process.HasExited)
            {
                try
                {
                    session.Process.Kill(entireProcessTree: true);
                }
                catch { }
                session.Process.Dispose();
            }
            session.Cts.Dispose();
        }
        _sessions.Clear();
        _eventChannel.Writer.TryComplete();
    }
}
