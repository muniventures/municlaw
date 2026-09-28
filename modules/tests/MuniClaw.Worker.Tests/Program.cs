using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using MuniClaw.Core.Contracts.Harness;
using MuniClaw.Core.Contracts.Tasks;
using MuniClaw.Core.Contracts.Worker;
using MuniClaw.Worker.Configuration;
using MuniClaw.Worker.Harness;
using MuniClaw.Worker.Http;
using MuniClaw.Worker.Sandbox;
using MuniClaw.Worker.Supervisor;

namespace MuniClaw.Worker.Tests;

public static class Program
{
    private static int _passed = 0;
    private static int _failed = 0;

    public static async Task<int> Main()
    {
        Console.WriteLine("=================================================");
        Console.WriteLine("   MuniClaw Worker & Sandbox Test Suite (T20)    ");
        Console.WriteLine("=================================================\n");

        // 1. Sandbox isolation & directory hierarchy
        await RunAsyncTest("T20: Sandbox: Directory layout and isolated worktree/home", async () =>
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "municlaw-test-sandbox-" + Guid.NewGuid().ToString("N"));
            try
            {
                var options = new WorkerOptions
                {
                    OrganizationId = Guid.NewGuid(),
                    TasksRootPath = tempRoot
                };

                var mgr = new TaskSandboxManager(options);
                var taskId = Guid.NewGuid();
                var runId = Guid.NewGuid();

                var sandbox = await mgr.CreateOrGetSandboxAsync(taskId, runId, default);

                Assert(Directory.Exists(sandbox.RootDirectory), "Root directory must exist");
                Assert(Directory.Exists(sandbox.WorktreeDirectory), "Worktree directory must exist");
                Assert(Directory.Exists(sandbox.HomeDirectory), "Home directory must exist");
                Assert(Directory.Exists(sandbox.TmpDirectory), "Tmp directory must exist");
                Assert(sandbox.IsActive, "Newly created sandbox must be active");

                var env = mgr.GetSanitizedEnvironment(sandbox);
                Assert(env["HOME"] == sandbox.HomeDirectory, "HOME must point to isolated home");
                Assert(env["TMPDIR"] == sandbox.TmpDirectory, "TMPDIR must point to isolated tmp");
                Assert(!env.ContainsKey("OPENCODE_SERVER_PASSWORD"), "OPENCODE_SERVER_PASSWORD must be stripped from sandbox env");
            }
            finally
            {
                if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
            }
        });

        // 2. Sandbox security policy: Docker socket denial
        RunTest("T20: Sandbox: Platform policy denies host Docker socket access", () =>
        {
            var tempRoot = "/tmp/municlaw/tasks/test-task";

            bool threw = false;
            try
            {
                SandboxPolicyEnforcer.ValidatePathAccess(tempRoot, "/var/run/docker.sock");
            }
            catch (UnauthorizedAccessException)
            {
                threw = true;
            }
            Assert(threw, "Access to /var/run/docker.sock must throw UnauthorizedAccessException");

            threw = false;
            try
            {
                SandboxPolicyEnforcer.ValidatePathAccess(tempRoot, "/run/docker.sock");
            }
            catch (UnauthorizedAccessException)
            {
                threw = true;
            }
            Assert(threw, "Access to /run/docker.sock must throw UnauthorizedAccessException");
        });

        // 3. Sandbox security policy: Directory traversal denial across tasks
        RunTest("T20: Sandbox: Path traversal outside task sandbox is strictly denied", () =>
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "municlaw-task-a");
            var targetOutside = Path.Combine(Path.GetTempPath(), "municlaw-task-b", "worktree", "secret.txt");

            bool threw = false;
            try
            {
                SandboxPolicyEnforcer.ValidatePathAccess(tempRoot, targetOutside);
            }
            catch (UnauthorizedAccessException)
            {
                threw = true;
            }
            Assert(threw, "Path outside sandbox root must throw UnauthorizedAccessException");
        });

        // 4. Sandbox security policy: Metadata service denial
        RunTest("T20: Sandbox: Cloud metadata and link-local network access strictly denied", () =>
        {
            var deniedTargets = new[]
            {
                "169.254.169.254",
                "169.254.10.20",
                "fd00:ec2::254",
                "metadata.google.internal",
                "instance-data"
            };

            foreach (var target in deniedTargets)
            {
                bool threw = false;
                try
                {
                    SandboxPolicyEnforcer.ValidateNetworkTarget(target);
                }
                catch (UnauthorizedAccessException)
                {
                    threw = true;
                }
                Assert(threw, $"Access to metadata target '{target}' must throw UnauthorizedAccessException");
            }

            // Valid external host should pass
            SandboxPolicyEnforcer.ValidateNetworkTarget("api.github.com");
            SandboxPolicyEnforcer.ValidateNetworkTarget("8.8.8.8");
        });

        // 5. Sandbox retention: 7-day cleanup and active work protection
        await RunAsyncTest("T20: Sandbox: 7-day retention cleanup protects active work and cleans expired", async () =>
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "municlaw-test-retention-" + Guid.NewGuid().ToString("N"));
            try
            {
                var options = new WorkerOptions
                {
                    OrganizationId = Guid.NewGuid(),
                    TasksRootPath = tempRoot,
                    RetentionDays = 7
                };

                var mgr = new TaskSandboxManager(options);

                // Task 1: Active task (started 10 days ago, but still active)
                var task1 = Guid.NewGuid();
                var s1 = await mgr.CreateOrGetSandboxAsync(task1, Guid.NewGuid(), default);

                // Task 2: Completed 10 days ago (> 7 days) -> EXPIRED
                var task2 = Guid.NewGuid();
                var s2 = await mgr.CreateOrGetSandboxAsync(task2, Guid.NewGuid(), default);
                await mgr.MarkRunCompletedAsync(task2, s2.RunId, isSuccess: true, DateTimeOffset.UtcNow.AddDays(-10), default);

                // Task 3: Completed 2 days ago (< 7 days) -> RETAINED
                var task3 = Guid.NewGuid();
                var s3 = await mgr.CreateOrGetSandboxAsync(task3, Guid.NewGuid(), default);
                await mgr.MarkRunCompletedAsync(task3, s3.RunId, isSuccess: true, DateTimeOffset.UtcNow.AddDays(-2), default);

                // Run cleanup as of today
                var cleaned = await mgr.CleanupExpiredSandboxesAsync(DateTimeOffset.UtcNow, default);

                Assert(cleaned.Count == 1 && cleaned.Contains(task2), "Only task 2 should be cleaned up");
                Assert(Directory.Exists(s1.RootDirectory), "Active task 1 must NEVER be cleaned up");
                Assert(!Directory.Exists(s2.RootDirectory), "Expired task 2 directory must be deleted");
                Assert(Directory.Exists(s3.RootDirectory), "Recent task 3 directory must be retained");
            }
            finally
            {
                if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
            }
        });

        // 6. Sandbox: Disk-pressure admission rejection
        await RunAsyncTest("T20: Sandbox: Disk-pressure admission rejection", async () =>
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "municlaw-test-disk-" + Guid.NewGuid().ToString("N"));
            try
            {
                var options = new WorkerOptions
                {
                    OrganizationId = Guid.NewGuid(),
                    TasksRootPath = tempRoot,
                    MinFreeDiskSpaceBytes = 10L * 1024 * 1024 * 1024 // 10 GB
                };

                // Provide a disk space provider that reports only 500 MB free (< 10 GB required)
                var mgr = new TaskSandboxManager(options, diskSpaceProvider: _ => 500L * 1024 * 1024);

                bool threw = false;
                try
                {
                    await mgr.CreateOrGetSandboxAsync(Guid.NewGuid(), Guid.NewGuid(), default);
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("Disk pressure admission rejected"))
                {
                    threw = true;
                }
                Assert(threw, "Should throw disk pressure admission rejection when storage is low");
            }
            finally
            {
                if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
            }
        });

        // 7. OpenCode supervisor: Loopback constraint & pinned version
        RunTest("T20: OpenCodeSupervisor: Enforces loopback interface and pinned v1.18.32", () =>
        {
            var validOptions = new WorkerOptions
            {
                OrganizationId = Guid.NewGuid(),
                OpenCodeHost = "127.0.0.1",
                OpenCodePort = 4096
            };
            using var supervisor = new OpenCodeSupervisor(validOptions);
            Assert(supervisor.PinnedVersion == "1.18.32", "Must pin OpenCode v1.18.32");
            Assert(supervisor.LoopbackHost == "127.0.0.1", "Must bind to loopback");

            // External IP must be strictly rejected
            var invalidOptions = new WorkerOptions
            {
                OrganizationId = Guid.NewGuid(),
                OpenCodeHost = "192.168.1.100"
            };

            bool threw = false;
            try
            {
                using var invalidSupervisor = new OpenCodeSupervisor(invalidOptions);
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }
            Assert(threw, "Binding OpenCode to non-loopback host must throw security exception");
        });

        // 8. OpenCode adapter: Abort SLA (< 100ms) & SSE event stream
        await RunAsyncTest("T20: OpenCodeAdapter: HTTP/SSE endpoints, prompt dispatch, and sub-100ms abort", async () =>
        {
            var mockHttp = new MockOpenCodeHttpHandler();
            using var httpClient = new HttpClient(mockHttp)
            {
                BaseAddress = new Uri("http://127.0.0.1:4096/")
            };

            var adapter = new OpenCodeHarnessAdapter(httpClient, "test-password");

            // Session creation
            var session = await adapter.CreateSessionAsync("/tmp/worktree", default);
            Assert(!string.IsNullOrEmpty(session.SessionId), "SessionId should be generated");
            Assert(session.Version == "1.18.32", "Version should be 1.18.32");

            // Prompt dispatch
            await adapter.SendPromptAsync(session.SessionId, new HarnessPrompt
            {
                Parts = new[] { new HarnessPromptPart { Type = "text", Text = "Run unit tests" } },
                Model = new HarnessModelSpec { ProviderId = "openai", ModelId = "gpt-4o" }
            }, default);

            // Abort within SLA (< 100ms)
            var abortResult = await adapter.AbortAsync(session.SessionId, default);
            Assert(abortResult, "Abort should succeed");
            Assert(adapter.LastMeasuredAbortLatency.TotalMilliseconds < 100,
                $"Abort latency {adapter.LastMeasuredAbortLatency.TotalMilliseconds:F2}ms must be under 100ms SLA");

            // Diff check
            var diff = await adapter.GetDiffAsync(session.SessionId, default);
            Assert(diff.Count == 1 && diff[0].Path == "README.md", "Diff should match");

            // SSE event subscription
            var events = new List<HarnessRawEvent>();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await foreach (var evt in adapter.SubscribeEventsAsync(cts.Token))
            {
                events.Add(evt);
                if (events.Count >= 2) break;
            }

            Assert(events.Count == 2, "Should stream 2 SSE events");
            Assert(events[0].EventType == "EventSessionNextStepStarted", "First event step started");
            Assert(HarnessEventMapper.MapToCanonical(events[0].EventType) == TaskEventType.StepStarted, "Map to StepStarted");
        });

        // 9. WorkerApiClient: 410 Gone lease expiry & 409 Conflict handling
        await RunAsyncTest("T20: WorkerApiClient: HTTP status codes and lease validity mapping", async () =>
        {
            var mockApi = new MockWorkerServerHttpHandler();
            using var httpClient = new HttpClient(mockApi)
            {
                BaseAddress = new Uri("http://localhost:5000/")
            };

            var client = new WorkerApiClient(httpClient);

            // Normal claim
            var claim = await client.ClaimWorkAsync(new WorkerClaimRequest
            {
                WorkerId = Guid.NewGuid(),
                OrganizationId = Guid.NewGuid(),
                SupportedHarnessVersion = "1.18.32"
            }, default);
            Assert(claim.HasWork, "Should have work");

            // Heartbeat valid lease
            var hb = await client.HeartbeatAsync(new WorkerHeartbeatRequest
            {
                WorkerId = claim.RunId!.Value,
                RunId = claim.RunId.Value,
                FencingToken = claim.FencingToken,
                CurrentLeaseToken = claim.LeaseToken!,
                LastAcknowledgedCommandCursor = 0
            }, default);
            Assert(hb.IsLeaseValid, "Lease should be valid");

            // Heartbeat expired (410 Gone)
            mockApi.SimulateLeaseExpired = true;
            var expiredHb = await client.HeartbeatAsync(new WorkerHeartbeatRequest
            {
                WorkerId = claim.RunId.Value,
                RunId = claim.RunId.Value,
                FencingToken = claim.FencingToken,
                CurrentLeaseToken = "old-lease",
                LastAcknowledgedCommandCursor = 0
            }, default);
            Assert(!expiredHb.IsLeaseValid, "410 Gone must map to IsLeaseValid = false");

            // Event upload fencing conflict (409 Conflict)
            mockApi.SimulateFencingConflict = true;
            bool threwConflict = false;
            try
            {
                await client.UploadEventsAsync(new WorkerEventBatchUpload
                {
                    WorkerId = Guid.NewGuid(),
                    RunId = claim.RunId.Value,
                    FencingToken = 999,
                    Events = Array.Empty<TaskEventDto>()
                }, default);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Fencing conflict"))
            {
                threwConflict = true;
            }
            Assert(threwConflict, "409 Conflict must throw InvalidOperationException");
        });

        // 10. Worker event buffer: Monotonic sequencing and ack flush
        await RunAsyncTest("T20: EventBuffer: Monotonic numbering, batch upload, and acknowledgment", async () =>
        {
            var buffer = new WorkerEventBuffer(maxCapacity: 100);
            var taskId = Guid.NewGuid();
            var runId = Guid.NewGuid();

            buffer.Enqueue(taskId, runId, TaskEventType.StatusChanged, "{\"status\":\"Running\"}");
            buffer.Enqueue(taskId, runId, TaskEventType.StepStarted, "{\"step\":1}");
            buffer.Enqueue(taskId, runId, TaskEventType.TextDelta, "{\"delta\":\"Working...\"}");

            Assert(buffer.CurrentSequenceNumber == 3, "Monotonic sequence number should be 3");
            Assert(buffer.PendingCount == 3, "Pending count should be 3");

            var mockClient = new MockWorkerApiClient();
            var uploadedCount = await buffer.FlushAsync(mockClient, Guid.NewGuid(), runId, 1, default);

            Assert(uploadedCount == 3, "Uploaded 3 events");
            Assert(buffer.PendingCount == 0, "Buffer acknowledged and emptied");
            Assert(mockClient.LastUploadedBatch != null && mockClient.LastUploadedBatch.Count == 3, "Batch received by client");
        });

        // 11. Worker Supervisor: Claim loop, heartbeat renewal, and command handling
        await RunAsyncTest("T20: Supervisor: Claim loop, heartbeat renewal, and command processing", async () =>
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "municlaw-test-sup-" + Guid.NewGuid().ToString("N"));
            try
            {
                var orgId = Guid.NewGuid();
                var runId = Guid.NewGuid();
                var taskId = Guid.NewGuid();

                var options = new WorkerOptions
                {
                    OrganizationId = orgId,
                    TasksRootPath = tempRoot,
                    HeartbeatInterval = TimeSpan.FromMilliseconds(50),
                    ClaimPollInterval = TimeSpan.FromMilliseconds(50)
                };

                var mockClient = new MockWorkerApiClient();
                mockClient.EnqueueClaim(new WorkerClaimResponse
                {
                    HasWork = true,
                    RunId = runId,
                    TaskId = taskId,
                    FencingToken = 10,
                    LeaseToken = "lease-token-1",
                    HarnessVersion = "1.18.32"
                });

                // Server will send an Abort command on the heartbeat
                mockClient.HeartbeatResponseFactory = req => new WorkerHeartbeatResponse
                {
                    IsLeaseValid = true,
                    ExtendedLeaseToken = "lease-token-extended",
                    PendingCommands = new[]
                    {
                        new WorkerCommand
                        {
                            CommandSequence = 1,
                            CommandType = WorkerCommandType.Abort,
                            PayloadJson = "{}"
                        }
                    }
                };

                var sandboxMgr = new TaskSandboxManager(options);
                var mockRunner = new MockOpenCodeProcessRunner();
                using var mockHarnessHttp = new HttpClient(new MockOpenCodeHttpHandler())
                {
                    BaseAddress = new Uri("http://127.0.0.1:4096/")
                };
                var harnessSupervisor = new OpenCodeSupervisor(options, mockRunner, mockHarnessHttp);

                var supervisor = new WorkerSupervisor(options, mockClient, sandboxMgr, harnessSupervisor);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var processed = await supervisor.PollAndProcessOnceAsync(cts.Token);

                Assert(processed, "PollAndProcessOnceAsync should have claimed and processed work");
                Assert(mockClient.HeartbeatsSent.Count > 0, "Heartbeats should have been dispatched");
                Assert(supervisor.LastCommandCursor == 1, "Last acknowledged command cursor should be 1");
            }
            finally
            {
                if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
            }
        });

        // 12. Worker Supervisor: Immediate termination on lease loss
        await RunAsyncTest("T20: Supervisor: Lease loss causes immediate execution termination", async () =>
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "municlaw-test-loss-" + Guid.NewGuid().ToString("N"));
            try
            {
                var orgId = Guid.NewGuid();
                var runId = Guid.NewGuid();
                var taskId = Guid.NewGuid();

                var options = new WorkerOptions
                {
                    OrganizationId = orgId,
                    TasksRootPath = tempRoot,
                    HeartbeatInterval = TimeSpan.FromMilliseconds(50),
                    ClaimPollInterval = TimeSpan.FromMilliseconds(50)
                };

                var mockClient = new MockWorkerApiClient();
                mockClient.EnqueueClaim(new WorkerClaimResponse
                {
                    HasWork = true,
                    RunId = runId,
                    TaskId = taskId,
                    FencingToken = 42,
                    LeaseToken = "lease-token-initial",
                    HarnessVersion = "1.18.32"
                });

                // Heartbeat returns lease lost (IsLeaseValid = false)
                mockClient.HeartbeatResponseFactory = _ => new WorkerHeartbeatResponse
                {
                    IsLeaseValid = false,
                    PendingCommands = Array.Empty<WorkerCommand>()
                };

                var sandboxMgr = new TaskSandboxManager(options);
                var mockRunner = new MockOpenCodeProcessRunner();
                using var mockHarnessHttp = new HttpClient(new MockOpenCodeHttpHandler())
                {
                    BaseAddress = new Uri("http://127.0.0.1:4096/")
                };
                var harnessSupervisor = new OpenCodeSupervisor(options, mockRunner, mockHarnessHttp);

                var supervisor = new WorkerSupervisor(options, mockClient, sandboxMgr, harnessSupervisor);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                var processed = await supervisor.PollAndProcessOnceAsync(cts.Token);

                Assert(processed, "Should process run");
                Assert(!supervisor.IsActive, "Supervisor must be inactive after lease loss");
                Assert(supervisor.CurrentRunId == null, "CurrentRunId must be cleared");
            }
            finally
            {
                if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
            }
        });

        Console.WriteLine("\n-------------------------------------------------");
        Console.WriteLine($"Test Run Summary: {_passed} Passed, {_failed} Failed");
        Console.WriteLine("-------------------------------------------------");

        return _failed > 0 ? 1 : 0;
    }

    private static void RunTest(string testName, Action testAction)
    {
        try
        {
            testAction();
            Console.WriteLine($"[PASS] {testName}");
            _passed++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] {testName}: {ex.Message}");
            _failed++;
        }
    }

    private static async Task RunAsyncTest(string testName, Func<Task> testAction)
    {
        try
        {
            await testAction();
            Console.WriteLine($"[PASS] {testName}");
            _passed++;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[FAIL] {testName}: {ex.Message}");
            _failed++;
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new Exception(message);
        }
    }
}

// --- Mocks and Test Doubles ---

internal sealed class MockWorkerApiClient : IWorkerApiClient
{
    private readonly Queue<WorkerClaimResponse> _claims = new();
    public List<WorkerHeartbeatRequest> HeartbeatsSent { get; } = new();
    public IReadOnlyList<TaskEventDto>? LastUploadedBatch { get; private set; }
    public Func<WorkerHeartbeatRequest, WorkerHeartbeatResponse>? HeartbeatResponseFactory { get; set; }

    public void EnqueueClaim(WorkerClaimResponse claim) => _claims.Enqueue(claim);

    public Task<WorkerClaimResponse> ClaimWorkAsync(WorkerClaimRequest request, CancellationToken ct)
    {
        if (_claims.TryDequeue(out var claim))
        {
            return Task.FromResult(claim);
        }

        return Task.FromResult(new WorkerClaimResponse { HasWork = false, FencingToken = 0 });
    }

    public Task<WorkerHeartbeatResponse> HeartbeatAsync(WorkerHeartbeatRequest request, CancellationToken ct)
    {
        HeartbeatsSent.Add(request);
        if (HeartbeatResponseFactory != null)
        {
            return Task.FromResult(HeartbeatResponseFactory(request));
        }

        return Task.FromResult(new WorkerHeartbeatResponse
        {
            IsLeaseValid = true,
            ExtendedLeaseToken = request.CurrentLeaseToken + "-ext",
            PendingCommands = Array.Empty<WorkerCommand>()
        });
    }

    public Task<WorkerEventBatchAck> UploadEventsAsync(WorkerEventBatchUpload upload, CancellationToken ct)
    {
        LastUploadedBatch = upload.Events;
        var maxSeq = upload.Events.Count > 0 ? upload.Events.Max(e => e.SequenceNumber) : 0;
        return Task.FromResult(new WorkerEventBatchAck
        {
            RunId = upload.RunId,
            LastAcknowledgedSequenceNumber = maxSeq
        });
    }
}

internal sealed class MockWorkerServerHttpHandler : HttpMessageHandler
{
    public bool SimulateLeaseExpired { get; set; }
    public bool SimulateFencingConflict { get; set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;

        if (path.EndsWith("/claim"))
        {
            var json = JsonSerializer.Serialize(new WorkerClaimResponse
            {
                HasWork = true,
                RunId = Guid.NewGuid(),
                TaskId = Guid.NewGuid(),
                FencingToken = 1,
                LeaseToken = "lease-1"
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }

        if (path.EndsWith("/heartbeat"))
        {
            if (SimulateLeaseExpired)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Gone)
                {
                    Content = new StringContent("{\"error\":\"lease_expired\"}", Encoding.UTF8, "application/json")
                });
            }

            var json = JsonSerializer.Serialize(new WorkerHeartbeatResponse
            {
                IsLeaseValid = true,
                ExtendedLeaseToken = "lease-renewed",
                PendingCommands = Array.Empty<WorkerCommand>()
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }

        if (path.EndsWith("/events"))
        {
            if (SimulateFencingConflict)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict)
                {
                    Content = new StringContent("{\"error\":\"fencing_conflict\"}", Encoding.UTF8, "application/json")
                });
            }

            var json = JsonSerializer.Serialize(new WorkerEventBatchAck
            {
                RunId = Guid.NewGuid(),
                LastAcknowledgedSequenceNumber = 5
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

internal sealed class MockOpenCodeHttpHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? string.Empty;

        if (path.EndsWith("/session") && request.Method == HttpMethod.Post)
        {
            var resp = "{\"sessionId\":\"mock-sess-123\",\"slug\":\"mock-slug\",\"version\":\"1.18.32\",\"directory\":\"/tmp/worktree\"}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(resp, Encoding.UTF8, "application/json")
            });
        }

        if (path.Contains("/prompt_async"))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }

        if (path.EndsWith("/diff"))
        {
            var resp = "[{\"path\":\"README.md\",\"changeType\":\"modified\",\"additions\":2,\"deletions\":0}]";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(resp, Encoding.UTF8, "application/json")
            });
        }

        if (path.EndsWith("/abort"))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"aborted\":true}", Encoding.UTF8, "application/json")
            });
        }

        if (path.EndsWith("/event"))
        {
            var sse = "event: EventSessionNextStepStarted\ndata: {\"type\":\"EventSessionNextStepStarted\",\"sessionId\":\"mock-sess-123\"}\n\n" +
                      "event: EventSessionNextStepEnded\ndata: {\"type\":\"EventSessionNextStepEnded\",\"sessionId\":\"mock-sess-123\"}\n\n";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(sse, Encoding.UTF8, "text/event-stream")
            });
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

internal sealed class MockOpenCodeProcessRunner : IOpenCodeProcessRunner
{
    public bool IsRunning { get; private set; }

    public Task<bool> StartServerAsync(string executablePath, string host, int port, string serverPassword, CancellationToken ct)
    {
        IsRunning = true;
        return Task.FromResult(true);
    }

    public Task StopServerAsync(CancellationToken ct)
    {
        IsRunning = false;
        return Task.CompletedTask;
    }
}
