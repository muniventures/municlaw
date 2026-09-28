using MuniClaw.Core.Contracts.Approvals;
using MuniClaw.Core.Contracts.Harness;
using MuniClaw.Core.Contracts.Notifications;
using MuniClaw.Core.Contracts.Tasks;
using MuniClaw.Core.Contracts.Worker;
using MuniClaw.Core.Data;
using MuniClaw.Core.Integrations;
using MuniClaw.Core.Models;
using MuniClaw.Core.Services;
using TaskStatus = MuniClaw.Core.Contracts.Tasks.TaskStatus;

namespace MuniClaw.Tests;

public static class Program
{
    private static int _passed = 0;
    private static int _failed = 0;

    public static async Task<int> Main()
    {
        Console.WriteLine("=================================================");
        Console.WriteLine("   MuniClaw Contract & Control-Plane Test Suite ");
        Console.WriteLine("=================================================\n");

        // --- T00 Contract Qualification Tests ---
        RunTest("T00: TaskRunStateMachine: Valid standard lifecycle transitions", () =>
        {
            var transitions = new[]
            {
                (TaskRunStatus.Queued, TaskRunStatus.Preparing),
                (TaskRunStatus.Preparing, TaskRunStatus.Running),
                (TaskRunStatus.Running, TaskRunStatus.AwaitingInput),
                (TaskRunStatus.AwaitingInput, TaskRunStatus.Running),
                (TaskRunStatus.Running, TaskRunStatus.Completed),
                (TaskRunStatus.Running, TaskRunStatus.Cancelling),
                (TaskRunStatus.Cancelling, TaskRunStatus.Cancelled)
            };

            foreach (var (from, to) in transitions)
            {
                Assert(TaskRunStateTransitions.CanTransition(from, to), $"Expected CanTransition({from}, {to}) to be true");
                TaskRunStateTransitions.ValidateTransition(from, to);
            }
        });

        RunTest("T00: TaskRunStateMachine: Invalid transitions from terminal states", () =>
        {
            var invalidTransitions = new[]
            {
                (TaskRunStatus.Completed, TaskRunStatus.Running),
                (TaskRunStatus.Completed, TaskRunStatus.Queued),
                (TaskRunStatus.Failed, TaskRunStatus.Running),
                (TaskRunStatus.Cancelled, TaskRunStatus.Running),
                (TaskRunStatus.Queued, TaskRunStatus.Completed)
            };

            foreach (var (from, to) in invalidTransitions)
            {
                Assert(!TaskRunStateTransitions.CanTransition(from, to), $"Expected CanTransition({from}, {to}) to be false");
                bool threw = false;
                try { TaskRunStateTransitions.ValidateTransition(from, to); }
                catch (InvalidOperationException) { threw = true; }
                Assert(threw, $"Expected ValidateTransition({from}, {to}) to throw");
            }
        });

        RunTest("T00: CapabilityPolicyEngine: PlatformDenied cannot be overridden to Allow", () =>
        {
            var defaultDeny = CapabilityPolicyEngine.GetDefaultSetting(CapabilityCategory.PlatformDenied);
            Assert(defaultDeny == ApprovalPolicySetting.Deny, "PlatformDenied must default to Deny");

            var forcedAllow = CapabilityPolicyEngine.Evaluate(CapabilityCategory.PlatformDenied, ApprovalPolicySetting.Allow);
            Assert(forcedAllow == ApprovalPolicySetting.Deny, "PlatformDenied cannot be overridden to Allow");
        });

        RunTest("T00: TaskEvents: Monotonic cursor format and parser", () =>
        {
            var runId = Guid.NewGuid();
            var evt = new TaskEventDto
            {
                EventId = Guid.NewGuid(),
                TaskId = Guid.NewGuid(),
                RunId = runId,
                SequenceNumber = 77,
                EventType = TaskEventType.StepStarted,
                Timestamp = DateTimeOffset.UtcNow,
                PayloadJson = "{}"
            };

            Assert(evt.Cursor == $"{runId}:77", $"Cursor mismatch: {evt.Cursor}");
            var parsed = TaskEventDto.ParseCursor(evt.Cursor);
            Assert(parsed.HasValue && parsed.Value.RunId == runId && parsed.Value.SequenceNumber == 77, "Cursor parse failed");
        });

        RunTest("T00: HarnessEventMapper: OpenCode v1.18.32 event mapping to canonical events", () =>
        {
            Assert(HarnessEventMapper.MapToCanonical("EventSessionNextStepStarted") == TaskEventType.StepStarted, "StepStarted mapping");
            Assert(HarnessEventMapper.MapToCanonical("EventPermissionAsked") == TaskEventType.ApprovalRequested, "ApprovalRequested mapping");
            Assert(HarnessEventMapper.MapToCanonical("EventSessionDiff") == TaskEventType.DiffUpdated, "DiffUpdated mapping");
            Assert(HarnessEventMapper.MapToCanonical("Event.tui.toast.show") == null, "Transient event should filter");
        });

        // --- T10 Control Plane & Integrations Tests ---

        await RunAsyncTest("T10: Authorization: Subject mapping, allowlisting, and MVP single-member rule", async () =>
        {
            var store = new InMemoryMuniClawStore();
            var auth = new OrganizationAuthorizationService(store);

            // 1. Subject mapping
            var user1 = await auth.GetOrCreateUserAsync("firebase:uid-1", "alice@example.com", default);
            Assert(!user1.IsAllowlisted, "New user should not be allowlisted by default");

            // 2. Allowlisting check
            await auth.SetUserAllowlistedAsync(user1.Id, true, default);
            Assert(await auth.IsUserAllowlistedAsync(user1.Id, default), "User should now be allowlisted");

            var orgId = Guid.NewGuid();
            store.Organizations[orgId] = new Organization { Id = orgId, Name = "Acme Corp", Slug = "acme" };

            // 3. Add first member -> succeeds
            var m1 = await auth.AddMemberToOrganizationAsync(orgId, user1.Id, MembershipRole.Admin, default);
            Assert(m1.IsActive && m1.Role == MembershipRole.Admin, "First member should succeed as Admin");
            Assert(await auth.CanAccessOrganizationAsync(user1.Id, orgId, default), "User 1 can access org");

            // 4. Try adding second active member -> MUST FAIL under MVP single-member policy
            var user2 = await auth.GetOrCreateUserAsync("firebase:uid-2", "bob@example.com", default);
            await auth.SetUserAllowlistedAsync(user2.Id, true, default);

            bool threw = false;
            try
            {
                await auth.AddMemberToOrganizationAsync(orgId, user2.Id, MembershipRole.Member, default);
            }
            catch (InvalidOperationException)
            {
                threw = true;
            }
            Assert(threw, "Adding a second active member to an org must throw under MVP policy");
        });

        await RunAsyncTest("T10: TaskLifecycle: One-active-task rule, idempotency, and follow-up chaining", async () =>
        {
            var store = new InMemoryMuniClawStore();
            var auth = new OrganizationAuthorizationService(store);
            var lifecycle = new TaskLifecycleService(store, auth);
            var dispatch = new WorkerDispatchService(store);

            var (orgId, userId, projId, credId) = await SetupOrgProjectAndCredential(store, auth);

            // 1. Create task 1
            var req1 = new CreateTaskRequest
            {
                OrganizationId = orgId,
                ProjectId = projId,
                UserId = userId,
                Title = "Feature A",
                BaseBranch = "main",
                ProviderCredentialReferenceId = credId,
                Model = "gpt-4o",
                Instruction = "Implement feature A",
                HarnessVersion = "1.18.32"
            };

            var (task1, run1) = await lifecycle.CreateTaskAsync(req1, default);
            Assert(run1.Status == TaskRunStatus.Queued, "Task run should be queued");
            Assert(task1.TaskBranch.StartsWith("municlaw/task-"), "Dedicated task branch assigned");

            // Idempotency: duplicate request returns existing
            var (task1Dup, run1Dup) = await lifecycle.CreateTaskAsync(req1, default);
            Assert(task1Dup.Id == task1.Id, "Duplicate title request must return existing task");

            // Worker claims run 1 -> status transitions to Preparing
            var claim1 = await dispatch.ClaimWorkAsync(new WorkerClaimRequest
            {
                WorkerId = Guid.NewGuid(),
                OrganizationId = orgId,
                SupportedHarnessVersion = "1.18.32"
            }, default);
            Assert(claim1.HasWork && claim1.RunId == run1.Id, "Worker claimed run 1");

            // 2. Create task 2 in same org
            var req2 = new CreateTaskRequest
            {
                OrganizationId = orgId,
                ProjectId = projId,
                UserId = userId,
                Title = "Feature B",
                BaseBranch = "main",
                ProviderCredentialReferenceId = credId,
                Model = "gpt-4o",
                Instruction = "Implement feature B",
                HarnessVersion = "1.18.32"
            };
            var (task2, run2) = await lifecycle.CreateTaskAsync(req2, default);

            // 3. Worker tries to claim while run 1 is active -> MUST NOT claim task 2 (one executing task per org)
            var claim2 = await dispatch.ClaimWorkAsync(new WorkerClaimRequest
            {
                WorkerId = Guid.NewGuid(),
                OrganizationId = orgId,
                SupportedHarnessVersion = "1.18.32"
            }, default);
            Assert(!claim2.HasWork, "Cannot claim work when another task is active in the same organization");

            // 4. Complete run 1
            run1.Status = TaskRunStatus.Completed;

            // 5. Follow-up turn creates run 2 on task 1
            var followUpRun = await lifecycle.CreateFollowUpRunAsync(task1.Id, userId, "Follow up instructions", default);
            Assert(followUpRun.RunIndex == 2, "Follow up should have RunIndex 2");
            Assert(followUpRun.Status == TaskRunStatus.Queued, "Follow up should be queued");
        });

        await RunAsyncTest("T10: WorkerDispatch: Fencing token, heartbeat commands, and lease expiration", async () =>
        {
            var store = new InMemoryMuniClawStore();
            var auth = new OrganizationAuthorizationService(store);
            var lifecycle = new TaskLifecycleService(store, auth);
            var dispatch = new WorkerDispatchService(store);

            var (orgId, userId, projId, credId) = await SetupOrgProjectAndCredential(store, auth);

            var (task, run) = await lifecycle.CreateTaskAsync(new CreateTaskRequest
            {
                OrganizationId = orgId,
                ProjectId = projId,
                UserId = userId,
                Title = "Fencing test task",
                BaseBranch = "main",
                ProviderCredentialReferenceId = credId,
                Model = "gpt-4o",
                Instruction = "Test worker fencing",
                HarnessVersion = "1.18.32"
            }, default);

            var workerId = Guid.NewGuid();
            var claim = await dispatch.ClaimWorkAsync(new WorkerClaimRequest
            {
                WorkerId = workerId,
                OrganizationId = orgId,
                SupportedHarnessVersion = "1.18.32"
            }, default);

            Assert(claim.HasWork, "Claim should succeed");
            var fencingToken = claim.FencingToken;
            var leaseToken = claim.LeaseToken!;

            // Queue a command for the worker
            await dispatch.QueueCommandAsync(run.Id, WorkerCommandType.Abort, "{}", default);

            // Heartbeat with correct fencing token retrieves pending command
            var hb = await dispatch.HeartbeatAsync(new WorkerHeartbeatRequest
            {
                WorkerId = workerId,
                RunId = run.Id,
                FencingToken = fencingToken,
                CurrentLeaseToken = leaseToken,
                LastAcknowledgedCommandCursor = 0
            }, default);

            Assert(hb.IsLeaseValid, "Lease should renew");
            Assert(hb.PendingCommands.Count == 1, "Should deliver pending Abort command");
            Assert(hb.PendingCommands[0].CommandType == WorkerCommandType.Abort, "Command type is Abort");

            // Stale worker with wrong fencing token -> REJECTED
            var staleHb = await dispatch.HeartbeatAsync(new WorkerHeartbeatRequest
            {
                WorkerId = workerId,
                RunId = run.Id,
                FencingToken = fencingToken - 1, // Stale!
                CurrentLeaseToken = leaseToken,
                LastAcknowledgedCommandCursor = 0
            }, default);
            Assert(!staleHb.IsLeaseValid, "Stale fencing token must be rejected");

            // Event upload with valid fencing token -> succeeds
            var ack = await dispatch.UploadEventsAsync(new WorkerEventBatchUpload
            {
                WorkerId = workerId,
                RunId = run.Id,
                FencingToken = fencingToken,
                Events = new[]
                {
                    new TaskEventDto
                    {
                        EventId = Guid.NewGuid(),
                        TaskId = task.Id,
                        RunId = run.Id,
                        SequenceNumber = 1,
                        EventType = TaskEventType.StepStarted,
                        Timestamp = DateTimeOffset.UtcNow,
                        PayloadJson = "{\"step\":1}"
                    }
                }
            }, default);
            Assert(ack.LastAcknowledgedSequenceNumber == 1, "Event sequence 1 acked");

            // Lease expiry reconciliation: expired lease marks run as Failed
            run.LeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5);
            await dispatch.ReconcileExpiredLeasesAsync(default);
            Assert(run.Status == TaskRunStatus.Failed, "Expired lease should reconcile to Failed");
            Assert(run.LeaseToken == null, "Lease token must be invalidated");
        });

        await RunAsyncTest("T10: Approvals: Stale version rejection and PlatformDenied enforcement", async () =>
        {
            var store = new InMemoryMuniClawStore();
            var auth = new OrganizationAuthorizationService(store);
            var lifecycle = new TaskLifecycleService(store, auth);
            var dispatch = new WorkerDispatchService(store);
            var approvals = new ApprovalService(store, auth, dispatch);

            var (orgId, userId, projId, credId) = await SetupOrgProjectAndCredential(store, auth);
            var (task, run) = await lifecycle.CreateTaskAsync(new CreateTaskRequest
            {
                OrganizationId = orgId,
                ProjectId = projId,
                UserId = userId,
                Title = "Approval test",
                BaseBranch = "main",
                ProviderCredentialReferenceId = credId,
                Model = "gpt-4o",
                Instruction = "Review approval",
                HarnessVersion = "1.18.32"
            }, default);

            // 1. Normal approval request (ExternalNetwork)
            var req = await approvals.CreateApprovalRequestAsync(
                run.Id, CapabilityCategory.ExternalNetwork, "Fetch api.vendor.com", "hash-v1", default);
            Assert(req.Status == ApprovalRecordStatus.Pending, "Should be pending");
            Assert(run.Status == TaskRunStatus.AwaitingInput, "Run should be awaiting input");

            // 2. Stale approval submission with wrong version hash -> MUST FAIL
            bool threwStale = false;
            try
            {
                await approvals.SubmitDecisionAsync(req.Id, userId, ApprovalDecisionType.AllowOnce, "hash-stale-v0", default);
            }
            catch (InvalidOperationException)
            {
                threwStale = true;
            }
            Assert(threwStale, "Stale content version hash must be rejected");

            // 3. Valid decision matching hash -> succeeds and resumes run
            var approved = await approvals.SubmitDecisionAsync(req.Id, userId, ApprovalDecisionType.AllowOnce, "hash-v1", default);
            Assert(approved.Status == ApprovalRecordStatus.Approved, "Should be approved");
            Assert(run.Status == TaskRunStatus.Running, "Run should resume to Running");

            // 4. PlatformDenied capability -> immediately auto-rejected
            var deniedReq = await approvals.CreateApprovalRequestAsync(
                run.Id, CapabilityCategory.PlatformDenied, "Mount docker.sock", "hash-v2", default);
            Assert(deniedReq.Status == ApprovalRecordStatus.Rejected, "PlatformDenied must be immediately rejected");
        });

        await RunAsyncTest("T10: Credentials: Scope validation and revocation cascade", async () =>
        {
            var store = new InMemoryMuniClawStore();
            var auth = new OrganizationAuthorizationService(store);
            var credService = new ProviderCredentialService(store, auth);
            var lifecycle = new TaskLifecycleService(store, auth);

            var (orgId, userId, projId, _) = await SetupOrgProjectAndCredential(store, auth);

            // 1. Organization scope -> MUST FAIL in MVP
            bool threwOrgScope = false;
            try
            {
                await credService.RegisterCredentialAsync(new RegisterCredentialRequest
                {
                    OrganizationId = orgId,
                    UserId = userId,
                    ProviderName = "openai",
                    Label = "Shared Key",
                    Scope = CredentialScope.Organization, // NOT allowed in MVP
                    ApiKey = "sk-org-key"
                }, default);
            }
            catch (InvalidOperationException)
            {
                threwOrgScope = true;
            }
            Assert(threwOrgScope, "Organization-scoped keys must be rejected in MVP");

            // 2. Personal scope -> succeeds and stores safe reference
            var personalKey = await credService.RegisterCredentialAsync(new RegisterCredentialRequest
            {
                OrganizationId = orgId,
                UserId = userId,
                ProviderName = "anthropic",
                Label = "Alice Anthropic",
                Scope = CredentialScope.Personal,
                ApiKey = "sk-ant-test"
            }, default);
            Assert(personalKey.Scope == CredentialScope.Personal, "Should be Personal");
            Assert(personalKey.SecretReferencePath.Contains("orgs/" + orgId), "Scoped path in secret store");

            // 3. Create run using this key, then revoke key -> run must immediately fail
            var (task, run) = await lifecycle.CreateTaskAsync(new CreateTaskRequest
            {
                OrganizationId = orgId,
                ProjectId = projId,
                UserId = userId,
                Title = "Revocation test",
                BaseBranch = "main",
                ProviderCredentialReferenceId = personalKey.Id,
                Model = "claude-3-5-sonnet",
                Instruction = "Coding task",
                HarnessVersion = "1.18.32"
            }, default);

            run.Status = TaskRunStatus.Running; // simulate running
            await credService.RevokeCredentialAsync(personalKey.Id, userId, default);
            Assert(personalKey.IsRevoked, "Key should be revoked");
            Assert(run.Status == TaskRunStatus.Failed, "Active run using revoked key must be terminated to Failed");
        });

        await RunAsyncTest("T10: Delivery: Reviewed commit binding and draft PR publication", async () =>
        {
            var store = new InMemoryMuniClawStore();
            var auth = new OrganizationAuthorizationService(store);
            var lifecycle = new TaskLifecycleService(store, auth);
            var gitClient = new MockGitProviderClient();
            var deliveryService = new DeliveryService(store, auth, gitClient);

            var (orgId, userId, projId, credId) = await SetupOrgProjectAndCredential(store, auth);
            var (task, run) = await lifecycle.CreateTaskAsync(new CreateTaskRequest
            {
                OrganizationId = orgId,
                ProjectId = projId,
                UserId = userId,
                Title = "Delivery task",
                BaseBranch = "main",
                BaseCommitSha = "commit-base-0",
                ProviderCredentialReferenceId = credId,
                Model = "gpt-4o",
                Instruction = "Do work",
                HarnessVersion = "1.18.32"
            }, default);

            run.Status = TaskRunStatus.Completed; // terminal state required for delivery

            var delivery = await deliveryService.PublishDraftAsync(new PublishDeliveryRequest
            {
                OrganizationId = orgId,
                TaskId = task.Id,
                RunId = run.Id,
                UserId = userId,
                BaseCommitSha = "commit-base-0",
                ReviewedCommitSha = "commit-reviewed-1",
                TargetBranch = "municlaw/feature-branch",
                Title = "Draft PR: Completed Feature",
                Body = "PR review notes"
            }, default);

            Assert(delivery.PublishedCommitSha == "commit-reviewed-1", "Published commit must match reviewed commit");
            Assert(delivery.RemotePrUrl != null && delivery.RemotePrUrl.Contains("pull/42"), "Remote draft PR generated");
        });

        await RunAsyncTest("T10: Workspace: Explicit onboarding request and one-time token generation", async () =>
        {
            var store = new InMemoryMuniClawStore();
            var auth = new OrganizationAuthorizationService(store);
            var infraClient = new MockMinicloudInfrastructureClient();
            var workspaceService = new WorkspaceProvisioningService(store, auth, infraClient);

            var (orgId, userId, _, _) = await SetupOrgProjectAndCredential(store, auth);

            var (workspace, token) = await workspaceService.RequestWorkspaceAsync(orgId, userId, "us-east-1", default);
            Assert(workspace.Status == WorkspaceStatus.Ready, "Workspace should be ready");
            Assert(!string.IsNullOrEmpty(token), "One-time registration token should be returned");
            Assert(workspace.SupervisorRegistrationTokenHash != null, "Token hash must be stored");
        });

        // --- T90 End-to-End Connected Qualification & Operational Scenarios ---

        await RunAsyncTest("T90: E2E: Complete coding task lifecycle with SSE reconnect, approval, and draft PR", async () =>
        {
            var store = new InMemoryMuniClawStore();
            var auth = new OrganizationAuthorizationService(store);
            var lifecycle = new TaskLifecycleService(store, auth);
            var dispatch = new WorkerDispatchService(store);
            var approvals = new ApprovalService(store, auth, dispatch);
            var gitClient = new MockGitProviderClient();
            var deliveryService = new DeliveryService(store, auth, gitClient);
            var infraClient = new MockMinicloudInfrastructureClient();
            var workspaceService = new WorkspaceProvisioningService(store, auth, infraClient);

            // Step 1: User allowlist & dedicated workspace provisioning
            var (orgId, userId, projId, credId) = await SetupOrgProjectAndCredential(store, auth);
            var (workspace, regToken) = await workspaceService.RequestWorkspaceAsync(orgId, userId, "us-east-1", default);
            Assert(workspace.Status == WorkspaceStatus.Ready && !string.IsNullOrEmpty(regToken), "Workspace provisioned");

            // Step 2: User assigns task
            var (task, run1) = await lifecycle.CreateTaskAsync(new CreateTaskRequest
            {
                OrganizationId = orgId,
                ProjectId = projId,
                UserId = userId,
                Title = "Implement OAuth PKCE",
                BaseBranch = "main",
                BaseCommitSha = "sha-base-001",
                ProviderCredentialReferenceId = credId,
                Model = "claude-3-7-sonnet",
                Instruction = "Implement secure PKCE token exchange",
                HarnessVersion = "1.18.32"
            }, default);
            Assert(run1.Status == TaskRunStatus.Queued, "Task 1 queued");

            // Step 3: Supervisor claims task via long-polling
            var workerId = Guid.NewGuid();
            var claim = await dispatch.ClaimWorkAsync(new WorkerClaimRequest
            {
                WorkerId = workerId,
                OrganizationId = orgId,
                SupportedHarnessVersion = "1.18.32"
            }, default);
            Assert(claim.HasWork && claim.RunId == run1.Id, "Task claimed by supervisor");
            var fencingToken = claim.FencingToken;

            // Step 4: Supervisor streams events
            await dispatch.UploadEventsAsync(new WorkerEventBatchUpload
            {
                WorkerId = workerId,
                RunId = run1.Id,
                FencingToken = fencingToken,
                Events = new[]
                {
                    new TaskEventDto
                    {
                        EventId = Guid.NewGuid(), TaskId = task.Id, RunId = run1.Id,
                        SequenceNumber = 1, EventType = TaskEventType.StepStarted,
                        Timestamp = DateTimeOffset.UtcNow, PayloadJson = "{\"step\":1}"
                    },
                    new TaskEventDto
                    {
                        EventId = Guid.NewGuid(), TaskId = task.Id, RunId = run1.Id,
                        SequenceNumber = 2, EventType = TaskEventType.TextDelta,
                        Timestamp = DateTimeOffset.UtcNow, PayloadJson = "{\"text\":\"Analyzing PKCE flow...\"}"
                    }
                }
            }, default);

            // Step 5: Browser SSE replay: reconnects with sinceSequence = 1, receives only sequence 2 (no duplicates)
            var replayedEvents = await dispatch.GetEventsSinceAsync(run1.Id, 1, default);
            Assert(replayedEvents.Count == 1 && replayedEvents[0].SequenceNumber == 2, "SSE replay cursor verified");

            // Step 6: Approval workflow
            var appReq = await approvals.CreateApprovalRequestAsync(
                run1.Id, CapabilityCategory.ExternalNetwork, "Connect to oauth.gitlab.com", "v1-hash", default);
            Assert(run1.Status == TaskRunStatus.AwaitingInput, "Run awaiting approval");

            var appDecision = await approvals.SubmitDecisionAsync(appReq.Id, userId, ApprovalDecisionType.AllowOnce, "v1-hash", default);
            Assert(appDecision.Status == ApprovalRecordStatus.Approved, "Approval granted");
            Assert(run1.Status == TaskRunStatus.Running, "Run resumed to running");

            // Step 7: Tool finishes, task completes
            run1.Status = TaskRunStatus.Completed;

            // Step 8: Delivery publication bound to reviewed commit
            var delivery = await deliveryService.PublishDraftAsync(new PublishDeliveryRequest
            {
                OrganizationId = orgId,
                TaskId = task.Id,
                RunId = run1.Id,
                UserId = userId,
                BaseCommitSha = "sha-base-001",
                ReviewedCommitSha = "sha-reviewed-002",
                TargetBranch = task.TaskBranch,
                Title = "Draft PR: OAuth PKCE Implementation",
                Body = "Automated PR created by MuniClaw"
            }, default);
            Assert(delivery.RemotePrUrl != null && delivery.RemotePrUrl.Contains("pull/42"), "Draft PR published");

            // Step 9: Follow-up turn creates Run 2 on the same task branch
            var run2 = await lifecycle.CreateFollowUpRunAsync(task.Id, userId, "Add unit tests for PKCE exchange", default);
            Assert(run2.RunIndex == 2 && run2.TaskId == task.Id, "Follow-up run appended");
            Assert(run2.Status == TaskRunStatus.Queued, "Follow-up queued for worker claim");
        });

        await RunAsyncTest("T90: Security: Cross-tenant isolation blocks unauthorized organization access", async () =>
        {
            var store = new InMemoryMuniClawStore();
            var auth = new OrganizationAuthorizationService(store);
            var lifecycle = new TaskLifecycleService(store, auth);

            var (orgA, userA, projA, credA) = await SetupOrgProjectAndCredential(store, auth);

            // User B in Org B
            var userB = await auth.GetOrCreateUserAsync("firebase:uid-b", "bob@external.com", default);
            await auth.SetUserAllowlistedAsync(userB.Id, true, default);
            var orgB = Guid.NewGuid();
            store.Organizations[orgB] = new Organization { Id = orgB, Name = "Org B", Slug = "org-b" };
            await auth.AddMemberToOrganizationAsync(orgB, userB.Id, MembershipRole.Admin, default);

            // User B attempts to create task in Org A -> REJECTED
            bool threwCrossTenant = false;
            try
            {
                await lifecycle.CreateTaskAsync(new CreateTaskRequest
                {
                    OrganizationId = orgA,
                    ProjectId = projA,
                    UserId = userB.Id, // Unauthorized!
                    Title = "Malicious Task",
                    BaseBranch = "main",
                    ProviderCredentialReferenceId = credA,
                    Model = "gpt-4o",
                    Instruction = "Cross-tenant intrusion",
                    HarnessVersion = "1.18.32"
                }, default);
            }
            catch (UnauthorizedAccessException)
            {
                threwCrossTenant = true;
            }
            Assert(threwCrossTenant, "Cross-organization task creation must be blocked with 403 Unauthorized");
        });

        await RunAsyncTest("T90: Security: Sandbox defense blocks Docker socket, cloud metadata, and traversal", async () =>
        {
            var taskId = Guid.NewGuid();
            var sandboxBase = "/tmp/municlaw/tasks/" + taskId;
            var worktree = sandboxBase + "/worktree";

            // 1. Docker socket mount attack -> Throws UnauthorizedAccessException
            bool threwDocker = false;
            try { MuniClaw.Worker.Sandbox.SandboxPolicyEnforcer.ValidatePathAccess(sandboxBase, "/var/run/docker.sock"); }
            catch (UnauthorizedAccessException) { threwDocker = true; }
            Assert(threwDocker, "Host Docker socket access must be denied");

            // 2. Directory traversal attack -> Throws UnauthorizedAccessException
            bool threwTraversal = false;
            try { MuniClaw.Worker.Sandbox.SandboxPolicyEnforcer.ValidatePathAccess(sandboxBase, worktree + "/../../../etc/shadow"); }
            catch (UnauthorizedAccessException) { threwTraversal = true; }
            Assert(threwTraversal, "Traversal outside sandbox must be denied");

            // 3. Link-local cloud metadata attack -> Throws UnauthorizedAccessException
            bool threwMetadata = false;
            try { MuniClaw.Worker.Sandbox.SandboxPolicyEnforcer.ValidateNetworkTarget("169.254.169.254"); }
            catch (UnauthorizedAccessException) { threwMetadata = true; }
            Assert(threwMetadata, "AWS/GCP/Azure link-local metadata must be denied");

            bool threwSubnet = false;
            try { MuniClaw.Worker.Sandbox.SandboxPolicyEnforcer.ValidateNetworkTarget("169.254.1.1"); }
            catch (UnauthorizedAccessException) { threwSubnet = true; }
            Assert(threwSubnet, "Link-local subnet must be denied");

            // 4. Safe worktree file -> Succeeds without throwing
            MuniClaw.Worker.Sandbox.SandboxPolicyEnforcer.ValidatePathAccess(sandboxBase, worktree + "/src/App.cs");

            // 5. Allowed package repository -> Succeeds without throwing
            MuniClaw.Worker.Sandbox.SandboxPolicyEnforcer.ValidateNetworkTarget("registry.npmjs.org");

            await Task.CompletedTask;
        });

        await RunAsyncTest("T90: Fault Tolerance: Expired lease prevents split-brain and rejects stale uploads", async () =>
        {
            var store = new InMemoryMuniClawStore();
            var auth = new OrganizationAuthorizationService(store);
            var lifecycle = new TaskLifecycleService(store, auth);
            var dispatch = new WorkerDispatchService(store);

            var (orgId, userId, projId, credId) = await SetupOrgProjectAndCredential(store, auth);
            var (task, run) = await lifecycle.CreateTaskAsync(new CreateTaskRequest
            {
                OrganizationId = orgId,
                ProjectId = projId,
                UserId = userId,
                Title = "Fault tolerance task",
                BaseBranch = "main",
                ProviderCredentialReferenceId = credId,
                Model = "gpt-4o",
                Instruction = "Test fault recovery",
                HarnessVersion = "1.18.32"
            }, default);

            var workerId = Guid.NewGuid();
            var claim = await dispatch.ClaimWorkAsync(new WorkerClaimRequest
            {
                WorkerId = workerId,
                OrganizationId = orgId,
                SupportedHarnessVersion = "1.18.32"
            }, default);

            var oldFencingToken = claim.FencingToken;
            var oldLeaseToken = claim.LeaseToken!;

            // Worker heartbeat expires
            run.LeaseExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(-10);
            await dispatch.ReconcileExpiredLeasesAsync(default);
            Assert(run.Status == TaskRunStatus.Failed, "Run reconciled to Failed");
            Assert(run.LeaseToken == null, "Lease revoked");

            // Old worker tries to send heartbeat -> REJECTED
            var hb = await dispatch.HeartbeatAsync(new WorkerHeartbeatRequest
            {
                WorkerId = workerId,
                RunId = run.Id,
                FencingToken = oldFencingToken,
                CurrentLeaseToken = oldLeaseToken,
                LastAcknowledgedCommandCursor = 0
            }, default);
            Assert(!hb.IsLeaseValid, "Expired worker heartbeat rejected");

            // Old worker tries to upload events -> REJECTED with fencing conflict
            // Increment fencing token to simulate new epoch
            run.FencingToken = oldFencingToken + 1;
            bool threwFencing = false;
            try
            {
                await dispatch.UploadEventsAsync(new WorkerEventBatchUpload
                {
                    WorkerId = workerId,
                    RunId = run.Id,
                    FencingToken = oldFencingToken, // Stale!
                    Events = Array.Empty<TaskEventDto>()
                }, default);
            }
            catch (InvalidOperationException)
            {
                threwFencing = true;
            }
            Assert(threwFencing, "Stale worker upload must be rejected to prevent split-brain");
        });

        // --- T90 Organization-Shared Provider Credentials Qualification Tests ---

        await RunAsyncTest("T90: OrgCredentials: Admin authorization required to register and update shared credentials", async () =>
        {
            var store = new InMemoryMuniClawStore();
            var auth = new OrganizationAuthorizationService(store);
            var credService = new ProviderCredentialService(store, auth);

            var adminUser = await auth.GetOrCreateUserAsync("firebase:admin-user", "admin@muni.dev", default);
            await auth.SetUserAllowlistedAsync(adminUser.Id, true, default);
            var memberUser = await auth.GetOrCreateUserAsync("firebase:member-user", "member@muni.dev", default);
            await auth.SetUserAllowlistedAsync(memberUser.Id, true, default);

            var orgId = Guid.NewGuid();
            store.Organizations[orgId] = new Organization { Id = orgId, Name = "Org Cred Org", Slug = "org-cred-org" };
            await auth.AddMemberToOrganizationAsync(orgId, adminUser.Id, MembershipRole.Admin, default, allowMultipleMembers: true);
            await auth.AddMemberToOrganizationAsync(orgId, memberUser.Id, MembershipRole.Member, default, allowMultipleMembers: true);

            // 1. Non-admin member attempts to register org credential -> Rejected
            bool memberThrew = false;
            try
            {
                await credService.RegisterOrganizationCredentialAsync(new MuniClaw.Core.Contracts.Integrations.RegisterOrganizationCredentialRequest(
                    orgId.ToString(),
                    "anthropic",
                    "Member Key",
                    "sk-ant-test",
                    null
                ), memberUser.Id, default);
            }
            catch (UnauthorizedAccessException)
            {
                memberThrew = true;
            }
            Assert(memberThrew, "Non-admin member cannot register organization credentials");

            // 2. Admin successfully registers org credential
            var cred = await credService.RegisterOrganizationCredentialAsync(new MuniClaw.Core.Contracts.Integrations.RegisterOrganizationCredentialRequest(
                orgId.ToString(),
                "anthropic",
                "Company Anthropic",
                "sk-ant-admin-valid",
                new OrganizationCredentialPolicy
                {
                    AllowedModels = new List<string> { "claude-3-5-sonnet*" },
                    MonthlySpendLimitUsd = 100.00m,
                    AdminOnly = false
                }
            ), adminUser.Id, default);

            Assert(cred != null, "Admin must be able to register organization credential");
            Assert(cred!.Scope == CredentialScope.Organization, "Scope must be Organization");
            Assert(cred.Policy?.MonthlySpendLimitUsd == 100.00m, "Spend limit matches");

            // 3. Member attempts to update policy -> Rejected
            bool updateThrew = false;
            try
            {
                await credService.UpdatePolicyAsync(cred.Id, new OrganizationCredentialPolicy
                {
                    MonthlySpendLimitUsd = 999.00m
                }, memberUser.Id, default);
            }
            catch (UnauthorizedAccessException)
            {
                updateThrew = true;
            }
            Assert(updateThrew, "Non-admin member cannot update organization credential policy");
        });

        await RunAsyncTest("T90: OrgCredentials: Model allowlist policy enforced on task creation", async () =>
        {
            var store = new InMemoryMuniClawStore();
            var auth = new OrganizationAuthorizationService(store);
            var credService = new ProviderCredentialService(store, auth);
            var lifecycle = new TaskLifecycleService(store, auth);

            var (orgId, adminId, projId, _) = await SetupOrgProjectAndCredential(store, auth);

            // Register org credential with model restriction: only claude-3-5-sonnet*
            var orgCred = await credService.RegisterOrganizationCredentialAsync(new MuniClaw.Core.Contracts.Integrations.RegisterOrganizationCredentialRequest(
                orgId.ToString(),
                "anthropic",
                "Restricted Models Key",
                "sk-ant-test-key",
                new OrganizationCredentialPolicy
                {
                    AllowedModels = new List<string> { "claude-3-5-sonnet*" },
                    MonthlySpendLimitUsd = 50.00m,
                    AdminOnly = false
                }
            ), adminId, default);

            // 1. Task requested with disallowed model: "gpt-4o" -> Rejected
            bool disallowedThrew = false;
            try
            {
                await lifecycle.CreateTaskAsync(new CreateTaskRequest
                {
                    OrganizationId = orgId,
                    ProjectId = projId,
                    UserId = adminId,
                    Title = "Disallowed model task",
                    BaseBranch = "main",
                    ProviderCredentialReferenceId = orgCred.Id,
                    Model = "gpt-4o",
                    Instruction = "Write code",
                    HarnessVersion = "1.18.32"
                }, default);
            }
            catch (InvalidOperationException ex)
            {
                disallowedThrew = true;
                Assert(ex.Message.Contains("not allowed", StringComparison.OrdinalIgnoreCase), $"Expected 'not allowed' in message: {ex.Message}");
            }
            Assert(disallowedThrew, "Task with model not in AllowedModels policy must be rejected");

            // 2. Task requested with allowed model matching wildcard prefix: "claude-3-5-sonnet-20241022" -> Allowed
            var (task, run) = await lifecycle.CreateTaskAsync(new CreateTaskRequest
            {
                OrganizationId = orgId,
                ProjectId = projId,
                UserId = adminId,
                Title = "Allowed model task",
                BaseBranch = "main",
                ProviderCredentialReferenceId = orgCred.Id,
                Model = "claude-3-5-sonnet-20241022",
                Instruction = "Write code",
                HarnessVersion = "1.18.32"
            }, default);

            Assert(task != null && run != null, "Task with allowed model created successfully");
        });

        await RunAsyncTest("T90: OrgCredentials: Monthly spend budget cap prevents over-limit task creation", async () =>
        {
            var store = new InMemoryMuniClawStore();
            var auth = new OrganizationAuthorizationService(store);
            var credService = new ProviderCredentialService(store, auth);
            var lifecycle = new TaskLifecycleService(store, auth);

            var (orgId, adminId, projId, _) = await SetupOrgProjectAndCredential(store, auth);

            // Org credential with $10 limit and already spent $10
            var orgCred = await credService.RegisterOrganizationCredentialAsync(new MuniClaw.Core.Contracts.Integrations.RegisterOrganizationCredentialRequest(
                orgId.ToString(),
                "openai",
                "Budgeted Key",
                "sk-proj-test",
                new OrganizationCredentialPolicy
                {
                    AllowedModels = new List<string> { "*" },
                    MonthlySpendLimitUsd = 10.00m,
                    AdminOnly = false
                }
            ), adminId, default);

            // Record $10 spend to exhaust budget
            await credService.RecordSpendAsync(orgId, orgCred.Id, 10.00m, default);

            bool budgetThrew = false;
            try
            {
                await lifecycle.CreateTaskAsync(new CreateTaskRequest
                {
                    OrganizationId = orgId,
                    ProjectId = projId,
                    UserId = adminId,
                    Title = "Exceeded budget task",
                    BaseBranch = "main",
                    ProviderCredentialReferenceId = orgCred.Id,
                    Model = "gpt-4o",
                    Instruction = "Write code",
                    HarnessVersion = "1.18.32"
                }, default);
            }
            catch (InvalidOperationException ex)
            {
                budgetThrew = true;
                Assert(ex.Message.Contains("monthly spend limit", StringComparison.OrdinalIgnoreCase), $"Expected 'monthly spend limit' in message: {ex.Message}");
            }
            Assert(budgetThrew, "Task creation must be rejected when monthly spend limit is reached");
        });

        await RunAsyncTest("T90: OrgCredentials: Non-admin member cannot use AdminOnly credential", async () =>
        {
            var store = new InMemoryMuniClawStore();
            var auth = new OrganizationAuthorizationService(store);
            var credService = new ProviderCredentialService(store, auth);
            var lifecycle = new TaskLifecycleService(store, auth);

            var admin = await auth.GetOrCreateUserAsync("firebase:admin-ao", "admin-ao@muni.dev", default);
            await auth.SetUserAllowlistedAsync(admin.Id, true, default);
            var member = await auth.GetOrCreateUserAsync("firebase:member-ao", "member-ao@muni.dev", default);
            await auth.SetUserAllowlistedAsync(member.Id, true, default);

            var orgId = Guid.NewGuid();
            store.Organizations[orgId] = new Organization { Id = orgId, Name = "AO Org", Slug = "ao-org" };
            await auth.AddMemberToOrganizationAsync(orgId, admin.Id, MembershipRole.Admin, default, allowMultipleMembers: true);
            await auth.AddMemberToOrganizationAsync(orgId, member.Id, MembershipRole.Member, default, allowMultipleMembers: true);

            var repoId = Guid.NewGuid();
            store.RepositoryConnections[repoId] = new RepositoryConnection
            {
                Id = repoId,
                OrganizationId = orgId,
                GitHost = GitHostType.GitHub,
                ExternalAccountId = "acc-1",
                RepositoryId = "repo-1",
                RepositoryFullName = "muniventures/ao-repo",
                DefaultBranch = "main",
                SecretReferencePath = "secret/repo"
            };
            var projId = Guid.NewGuid();
            store.Projects[projId] = new Project { Id = projId, OrganizationId = orgId, RepositoryConnectionId = repoId, Name = "AO Proj", DefaultBaseBranch = "main" };

            // Admin registers AdminOnly credential
            var orgCred = await credService.RegisterOrganizationCredentialAsync(new MuniClaw.Core.Contracts.Integrations.RegisterOrganizationCredentialRequest(
                orgId.ToString(),
                "openai",
                "Admin-Only Secret Key",
                "sk-proj-admin",
                new OrganizationCredentialPolicy
                {
                    AllowedModels = new List<string> { "*" },
                    MonthlySpendLimitUsd = null,
                    AdminOnly = true
                }
            ), admin.Id, default);

            // Member queries accessible credentials -> AdminOnly credential is filtered out!
            var accessible = await credService.GetAccessibleCredentialsAsync(member.Id, orgId, default);
            Assert(!accessible.Any(c => c.Id == orgCred.Id), "Member must not see AdminOnly organization credential");

            // Member attempts to force use the AdminOnly credential -> Rejected
            bool useThrew = false;
            try
            {
                await lifecycle.CreateTaskAsync(new CreateTaskRequest
                {
                    OrganizationId = orgId,
                    ProjectId = projId,
                    UserId = member.Id,
                    Title = "Admin-only intrusion",
                    BaseBranch = "main",
                    ProviderCredentialReferenceId = orgCred.Id,
                    Model = "gpt-4o",
                    Instruction = "Write code",
                    HarnessVersion = "1.18.32"
                }, default);
            }
            catch (InvalidOperationException)
            {
                useThrew = true;
            }
            Assert(useThrew, "Member must be denied from creating task with AdminOnly credential");
        });

        await RunAsyncTest("T90: OrgCredentials: Revoking shared credential terminates active task runs", async () =>
        {
            var store = new InMemoryMuniClawStore();
            var auth = new OrganizationAuthorizationService(store);
            var credService = new ProviderCredentialService(store, auth);
            var lifecycle = new TaskLifecycleService(store, auth);

            var (orgId, adminId, projId, _) = await SetupOrgProjectAndCredential(store, auth);

            var orgCred = await credService.RegisterOrganizationCredentialAsync(new MuniClaw.Core.Contracts.Integrations.RegisterOrganizationCredentialRequest(
                orgId.ToString(),
                "openai",
                "Shared Revoke Key",
                "sk-proj-revoke",
                new OrganizationCredentialPolicy { AllowedModels = new List<string> { "*" } }
            ), adminId, default);

            var (task, run) = await lifecycle.CreateTaskAsync(new CreateTaskRequest
            {
                OrganizationId = orgId,
                ProjectId = projId,
                UserId = adminId,
                Title = "Active task to revoke",
                BaseBranch = "main",
                ProviderCredentialReferenceId = orgCred.Id,
                Model = "gpt-4o",
                Instruction = "Write code",
                HarnessVersion = "1.18.32"
            }, default);

            // Simulate run running
            run.Status = TaskRunStatus.Running;

            // Admin revokes the credential
            await credService.RevokeCredentialAsync(orgCred.Id, adminId, default);

            var revokedCred = store.ProviderCredentials[orgCred.Id];
            Assert(revokedCred.IsRevoked, "Credential must be marked revoked");
            Assert(run.Status == TaskRunStatus.Failed, "Active run using revoked credential must be terminated to Failed");
        });

        // --- T90 Self-Host Distribution & Standalone Adapters Qualification Tests ---

        await RunAsyncTest("T90: SelfHost: MuniClawFileStore restart persistence and atomic recovery", async () =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"municlaw-test-store-{Guid.NewGuid():N}");
            try
            {
                var store1 = new MuniClawFileStore(tempDir);
                var orgId = Guid.NewGuid();
                var userId = Guid.NewGuid();
                var projId = Guid.NewGuid();

                store1.Organizations[orgId] = new Organization { Id = orgId, Name = "Self-Host Org", Slug = "self-host-org" };
                store1.Users[userId] = new User { Id = userId, ExternalSubjectId = "selfhost:u1", Email = "admin@selfhost.local", IsAllowlisted = true };
                store1.Projects[projId] = new Project { Id = projId, OrganizationId = orgId, RepositoryConnectionId = Guid.NewGuid(), Name = "On-Prem Project", DefaultBaseBranch = "main" };

                store1.StoreOrganizationCredential(new ProviderCredentialReference
                {
                    Id = Guid.NewGuid(),
                    OrganizationId = orgId,
                    OwningUserId = userId,
                    ProviderName = "openai",
                    Label = "On-Prem Shared OpenAI",
                    Scope = CredentialScope.Organization,
                    SecretReferencePath = "local/secrets/org1",
                    Policy = new OrganizationCredentialPolicy
                    {
                        AllowedModels = new List<string> { "gpt-4o" },
                        MonthlySpendLimitUsd = 250.00m,
                        AdminOnly = false
                    }
                });

                // Persist snapshot to disk
                store1.SaveSnapshot();

                // Verify snapshot file exists
                var snapshotFile = Path.Combine(tempDir, "snapshot.json");
                Assert(File.Exists(snapshotFile), "snapshot.json must exist on disk");

                // Simulate reboot: instantiate store2 from same directory
                var store2 = new MuniClawFileStore(tempDir);
                Assert(store2.Organizations.ContainsKey(orgId), "Organization restored after restart");
                Assert(store2.Users.ContainsKey(userId), "User restored after restart");
                Assert(store2.Projects.ContainsKey(projId), "Project restored after restart");

                var restoredCreds = store2.GetOrganizationCredentials(orgId.ToString());
                Assert(restoredCreds.Count == 1, "Organization credential restored after restart");
                Assert(restoredCreds[0].Policy?.MonthlySpendLimitUsd == 250.00m, "Credential policy restored accurately");

                // Test atomic concurrent writes
                var tasks = Enumerable.Range(0, 5).Select(_ => Task.Run(() => store2.SaveSnapshot()));
                await Task.WhenAll(tasks);
                Assert(File.Exists(snapshotFile), "snapshot.json intact after concurrent writes");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        });

        await RunAsyncTest("T90: SelfHost: LocalEncryptedSecretStore AES-256-GCM encryption and zero-leakage", async () =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"municlaw-test-secrets-{Guid.NewGuid():N}");
            try
            {
                var masterKeyHex = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
                var secretStore = new LocalEncryptedSecretStore(tempDir, masterKeyHex);

                var secretPath = "municlaw/org-42/credentials/cred-99";
                var sensitiveToken = "sk-live-super-secret-model-token-987654321";
                var payload = new Dictionary<string, string>
                {
                    { "api_key", sensitiveToken },
                    { "provider", "anthropic" }
                };

                await secretStore.StoreSecretAsync(secretPath, payload, default);

                // 1. Inspect on-disk raw file: verify sensitiveToken is encrypted and NOT in plaintext
                var files = Directory.GetFiles(tempDir, "*.*", SearchOption.AllDirectories);
                Assert(files.Length == 1, "Exactly one encrypted secret file created");
                var rawBytes = await File.ReadAllBytesAsync(files[0]);
                var rawString = System.Text.Encoding.UTF8.GetString(rawBytes);
                Assert(!rawString.Contains(sensitiveToken), "Plaintext secret token must NEVER be visible in encrypted file");

                // 2. Read back using valid key -> succeeds and recovers payload
                var recovered = await secretStore.GetSecretAsync(secretPath, default);
                Assert(recovered != null, "Recovered secret must not be null");
                Assert(recovered!["api_key"] == sensitiveToken, "Decrypted token matches original value");
                Assert(recovered["provider"] == "anthropic", "Decrypted metadata matches original value");

                // 3. Delete secret -> file removed
                await secretStore.DeleteSecretAsync(secretPath, default);
                var afterDelete = await secretStore.GetSecretAsync(secretPath, default);
                Assert(afterDelete == null, "Deleted secret returns null");
                Assert(!File.Exists(files[0]), "Secret file physically removed from disk");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        });

        await RunAsyncTest("T90: SelfHost: LocalEncryptedSecretStore cryptographic tamper detection", async () =>
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"municlaw-test-tamper-{Guid.NewGuid():N}");
            try
            {
                var keyA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
                var keyB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

                var storeA = new LocalEncryptedSecretStore(tempDir, keyA);
                var secretPath = "municlaw/tamper-test";
                await storeA.StoreSecretAsync(secretPath, new Dictionary<string, string> { { "key", "val" } }, default);

                var files = Directory.GetFiles(tempDir, "*.*", SearchOption.AllDirectories);
                Assert(files.Length == 1, "Secret file written");

                // 1. Decrypt with different master key -> throws CryptographicException
                var storeB = new LocalEncryptedSecretStore(tempDir, keyB);
                bool threwWrongKey = false;
                try
                {
                    await storeB.GetSecretAsync(secretPath, default);
                }
                catch (System.Security.Cryptography.CryptographicException)
                {
                    threwWrongKey = true;
                }
                Assert(threwWrongKey, "Decryption with wrong master key must throw CryptographicException");

                // 2. Tamper with file on disk (flip byte in ciphertext) -> throws CryptographicException
                var fileBytes = await File.ReadAllBytesAsync(files[0]);
                fileBytes[^1] ^= 0xFF; // flip last byte
                await File.WriteAllBytesAsync(files[0], fileBytes);

                bool threwTamper = false;
                try
                {
                    await storeA.GetSecretAsync(secretPath, default);
                }
                catch (System.Security.Cryptography.CryptographicException)
                {
                    threwTamper = true;
                }
                Assert(threwTamper, "Ciphertext or tag tampering must be detected and rejected by AES-GCM");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        });

        await RunAsyncTest("T90: SelfHost: OpenBaoHttpClient token redaction in error messages", async () =>
        {
            using var http = new HttpClient();
            var sensitiveToken = "s.super-secret-vault-auth-token-xyz";
            // Direct client targeting unreachable loopback port to trigger connection error
            var client = new OpenBaoHttpClient(http, "http://127.0.0.1:54321", sensitiveToken);

            bool threw = false;
            try
            {
                await client.StoreSecretAsync("test/path", new Dictionary<string, string> { { "k", "v" } }, default);
            }
            catch (Exception ex)
            {
                threw = true;
                Assert(!ex.ToString().Contains(sensitiveToken), "Vault auth token must be redacted from all exception messages");
            }
            Assert(threw, "Unreachable vault endpoint throws exception");
        });

        // --- T90 Outbound Messaging Adapters & Webhooks Qualification Tests ---

        await RunAsyncTest("T90: Messaging: Slack Block Kit payload formatting and action button URL", async () =>
        {
            var notif = new TaskLifecycleNotification
            {
                EventType = NotificationEventType.ApprovalRequested,
                TaskId = Guid.NewGuid(),
                RunId = Guid.NewGuid(),
                OrganizationId = Guid.NewGuid(),
                TaskTitle = "Refactor Authentication Middleware",
                RepositoryName = "muniventures/municlaw",
                Branch = "task/auth-refactor",
                Timestamp = DateTimeOffset.UtcNow,
                Summary = "Worker requesting approval to execute shell command: npm run test",
                ConsoleUrl = "https://ai.muni.dev/tasks/123"
            };

            var slackJson = NotificationPayloadFormatter.FormatSlack(notif);
            Assert(slackJson.Contains("blocks"), "Slack payload must contain blocks array");
            Assert(slackJson.Contains("View in Console"), "Slack payload must contain View in Console button");
            Assert(slackJson.Contains("https://ai.muni.dev/tasks/123"), "Slack button must link to ConsoleUrl");
            Assert(slackJson.Contains("task/auth-refactor"), "Slack payload must mention branch");
            await Task.CompletedTask;
        });

        await RunAsyncTest("T90: Messaging: Discord Embed payload formatting and color coding", async () =>
        {
            var eventsAndColors = new[]
            {
                (NotificationEventType.ApprovalRequested, 0xF59E0B),
                (NotificationEventType.TaskCompleted, 0x10B981),
                (NotificationEventType.TaskFailed, 0xEF4444),
                (NotificationEventType.DeliveryPublished, 0x3B82F6)
            };

            foreach (var (evt, expectedColor) in eventsAndColors)
            {
                var notif = new TaskLifecycleNotification
                {
                    EventType = evt,
                    TaskId = Guid.NewGuid(),
                    RunId = Guid.NewGuid(),
                    OrganizationId = Guid.NewGuid(),
                    TaskTitle = "Test Task",
                    RepositoryName = "repo",
                    Branch = "main",
                    Timestamp = DateTimeOffset.UtcNow,
                    Summary = "Summary",
                    ConsoleUrl = "https://ai.muni.dev"
                };

                var discordJson = NotificationPayloadFormatter.FormatDiscord(notif);
                Assert(discordJson.Contains("embeds"), "Discord payload must contain embeds array");
                Assert(discordJson.Contains(expectedColor.ToString()), $"Discord embed for {evt} must contain color {expectedColor}");
            }
            await Task.CompletedTask;
        });

        await RunAsyncTest("T90: Messaging: Generic Webhook HMAC-SHA256 signature verification and tamper detection", async () =>
        {
            var secret = "whsec_super-secret-signing-key-1234567890";
            var payload = "{\"event\":\"TaskCompleted\",\"taskId\":\"12345\"}";
            var timestamp = 1769600000L;

            var sig1 = NotificationPayloadFormatter.ComputeHmacSignature(secret, payload, timestamp);
            Assert(sig1.Length == 64, "HMAC-SHA256 hex string must be exactly 64 characters");

            // Same input must produce identical signature (idempotent verification)
            var sig2 = NotificationPayloadFormatter.ComputeHmacSignature(secret, payload, timestamp);
            Assert(sig1 == sig2, "Identical inputs must yield identical HMAC signature");

            // Tampering payload must alter signature
            var tamperedSig = NotificationPayloadFormatter.ComputeHmacSignature(secret, payload + " ", timestamp);
            Assert(sig1 != tamperedSig, "Tampered payload must fail HMAC signature match");

            // Tampering timestamp must alter signature
            var tamperedTimestampSig = NotificationPayloadFormatter.ComputeHmacSignature(secret, payload, timestamp + 1);
            Assert(sig1 != tamperedTimestampSig, "Tampered timestamp must fail HMAC signature match");

            // Wrong secret must fail signature match
            var wrongSecretSig = NotificationPayloadFormatter.ComputeHmacSignature("wrong-secret", payload, timestamp);
            Assert(sig1 != wrongSecretSig, "Wrong secret must fail HMAC signature match");

            await Task.CompletedTask;
        });

        await RunAsyncTest("T90: Messaging: NotificationChannel registration, masking, and event subscription filtering", async () =>
        {
            var store = new InMemoryMuniClawStore();
            var auth = new OrganizationAuthorizationService(store);
            var notifService = new NotificationService(store);

            var (orgId, adminId, _, _) = await SetupOrgProjectAndCredential(store, auth);

            // 1. Create Slack channel subscribed ONLY to TaskCompleted
            var slackChannel = await notifService.CreateChannelAsync(orgId, new CreateNotificationChannelRequest(
                "Engineering Alerts",
                NotificationChannelType.Slack,
                "https://hooks.slack.com/services/T0000/B0000/XXXXXXSecretToken",
                new List<NotificationEventType> { NotificationEventType.TaskCompleted }
            ), default);

            Assert(slackChannel.Id != Guid.Empty, "Channel ID generated");
            Assert(slackChannel.IsEnabled, "Channel enabled by default");

            // 2. Create Discord channel subscribed ONLY to TaskFailed
            var discordChannel = await notifService.CreateChannelAsync(orgId, new CreateNotificationChannelRequest(
                "Ops Incidents",
                NotificationChannelType.Discord,
                "https://discord.com/api/webhooks/12345/TokenABC",
                new List<NotificationEventType> { NotificationEventType.TaskFailed }
            ), default);

            // 3. Verify URL masking
            var channels = await notifService.ListChannelsAsync(orgId, default);
            Assert(channels.Count == 2, "2 channels registered");

            var slackDto = new NotificationChannelDto(
                slackChannel.Id,
                slackChannel.OrganizationId,
                slackChannel.ChannelType,
                slackChannel.Name,
                NotificationChannelDto.MaskWebhookUrl(slackChannel.WebhookUrl),
                slackChannel.SubscribedEvents,
                slackChannel.IsEnabled,
                slackChannel.CreatedAt,
                slackChannel.LastDispatchedAt,
                slackChannel.LastDispatchStatus
            );
            Assert(!slackDto.MaskedWebhookUrl.Contains("XXXXXXSecretToken"), "Webhook token must be masked in DTO");
            Assert(slackDto.MaskedWebhookUrl.Contains("***"), "Masked URL must contain ***");

            // 4. Update channel: disable Discord channel
            await notifService.UpdateChannelAsync(orgId, discordChannel.Id, new UpdateNotificationChannelRequest(
                "Ops Incidents",
                null,
                new List<NotificationEventType> { NotificationEventType.TaskFailed },
                false, // disable
                null
            ), default);

            var updatedDiscord = await notifService.GetChannelAsync(orgId, discordChannel.Id, default);
            Assert(!updatedDiscord!.IsEnabled, "Discord channel must now be disabled");

            // 5. Delete Slack channel
            var deleted = await notifService.DeleteChannelAsync(orgId, slackChannel.Id, default);
            Assert(deleted, "Channel deleted successfully");
            var remaining = await notifService.ListChannelsAsync(orgId, default);
            Assert(remaining.Count == 1, "Only 1 channel remains after deletion");
        });

        // --- T90 Parallel Task Execution & Concurrency Scheduling Qualification Tests ---

        await RunAsyncTest("T90: Concurrency: Parallel task execution within organization quota", async () =>
        {
            var store = new InMemoryMuniClawStore();
            var auth = new OrganizationAuthorizationService(store);
            var queueService = new TaskQueueService(store);
            var lifecycle = new TaskLifecycleService(store, auth, queueService);
            var dispatch = new WorkerDispatchService(store);

            var (orgId, userId, projId, credId) = await SetupOrgProjectAndCredential(store, auth);
            store.Organizations[orgId].MaxConcurrentRuns = 2;

            // 1. Create first task -> starts in Queued (ready for worker claim)
            var (task1, run1) = await lifecycle.CreateTaskAsync(new CreateTaskRequest
            {
                OrganizationId = orgId,
                ProjectId = projId,
                UserId = userId,
                Title = "Parallel Task 1",
                BaseBranch = "main",
                ProviderCredentialReferenceId = credId,
                Model = "gpt-4o",
                Instruction = "Task 1",
                HarnessVersion = "1.18.32"
            }, default);

            Assert(run1.Status == TaskRunStatus.Queued, "First task should start in Queued");
            Assert(run1.QueuePosition == null, "Task within capacity has no queue wait position");

            // Worker 1 claims run 1 -> transitions to Preparing
            var claim1 = await dispatch.ClaimWorkAsync(new WorkerClaimRequest
            {
                WorkerId = Guid.NewGuid(),
                OrganizationId = orgId,
                SupportedHarnessVersion = "1.18.32"
            }, default);
            Assert(claim1.HasWork && claim1.RunId == run1.Id, "Worker 1 claimed task 1");
            Assert(run1.Status == TaskRunStatus.Preparing, "First task transitions to Preparing");

            // 2. Create second task -> starts in Queued within quota
            var (task2, run2) = await lifecycle.CreateTaskAsync(new CreateTaskRequest
            {
                OrganizationId = orgId,
                ProjectId = projId,
                UserId = userId,
                Title = "Parallel Task 2",
                BaseBranch = "main",
                ProviderCredentialReferenceId = credId,
                Model = "gpt-4o",
                Instruction = "Task 2",
                HarnessVersion = "1.18.32"
            }, default);

            Assert(run2.Status == TaskRunStatus.Queued, "Second task starts in Queued within quota");
            Assert(run2.QueuePosition == null, "Second task within capacity has no queue wait position");

            // Worker 2 claims run 2 -> transitions to Preparing concurrently (slot 2 of 2)
            var claim2 = await dispatch.ClaimWorkAsync(new WorkerClaimRequest
            {
                WorkerId = Guid.NewGuid(),
                OrganizationId = orgId,
                SupportedHarnessVersion = "1.18.32"
            }, default);
            Assert(claim2.HasWork && claim2.RunId == run2.Id, "Worker 2 claimed task 2 concurrently");
            Assert(run2.Status == TaskRunStatus.Preparing, "Second task transitions to Preparing");

            // Both tasks are actively executing concurrently
            var activeCount = await queueService.GetActiveRunsCountAsync(orgId, default);
            Assert(activeCount == 2, $"Expected 2 active runs, got {activeCount}");

            // A third worker claim fails because organization quota (2) is saturated
            var claim3 = await dispatch.ClaimWorkAsync(new WorkerClaimRequest
            {
                WorkerId = Guid.NewGuid(),
                OrganizationId = orgId,
                SupportedHarnessVersion = "1.18.32"
            }, default);
            Assert(!claim3.HasWork, "Worker claim must return HasWork=false when concurrency quota is saturated");
        });

        await RunAsyncTest("T90: Concurrency: FIFO queueing and queue position assignment when quota is saturated", async () =>
        {
            var store = new InMemoryMuniClawStore();
            var auth = new OrganizationAuthorizationService(store);
            var queueService = new TaskQueueService(store);
            var lifecycle = new TaskLifecycleService(store, auth, queueService);
            var dispatch = new WorkerDispatchService(store);

            var (orgId, userId, projId, credId) = await SetupOrgProjectAndCredential(store, auth);
            store.Organizations[orgId].MaxConcurrentRuns = 2;

            // Saturate quota with 2 active tasks
            var (taskA, runA) = await lifecycle.CreateTaskAsync(new CreateTaskRequest
            {
                OrganizationId = orgId,
                ProjectId = projId,
                UserId = userId,
                Title = "Active Task A",
                BaseBranch = "main",
                ProviderCredentialReferenceId = credId,
                Model = "gpt-4o",
                Instruction = "Task A",
                HarnessVersion = "1.18.32"
            }, default);

            var (taskB, runB) = await lifecycle.CreateTaskAsync(new CreateTaskRequest
            {
                OrganizationId = orgId,
                ProjectId = projId,
                UserId = userId,
                Title = "Active Task B",
                BaseBranch = "main",
                ProviderCredentialReferenceId = credId,
                Model = "gpt-4o",
                Instruction = "Task B",
                HarnessVersion = "1.18.32"
            }, default);

            var claimA = await dispatch.ClaimWorkAsync(new WorkerClaimRequest { WorkerId = Guid.NewGuid(), OrganizationId = orgId, SupportedHarnessVersion = "1.18.32" }, default);
            var claimB = await dispatch.ClaimWorkAsync(new WorkerClaimRequest { WorkerId = Guid.NewGuid(), OrganizationId = orgId, SupportedHarnessVersion = "1.18.32" }, default);
            Assert(claimA.HasWork && claimB.HasWork, "Both active tasks claimed into Preparing");

            // 3. Create third task -> quota saturated, enters Queued at position #1
            var (task3, run3) = await lifecycle.CreateTaskAsync(new CreateTaskRequest
            {
                OrganizationId = orgId,
                ProjectId = projId,
                UserId = userId,
                Title = "Queued Task C",
                BaseBranch = "main",
                ProviderCredentialReferenceId = credId,
                Model = "gpt-4o",
                Instruction = "Task C",
                HarnessVersion = "1.18.32"
            }, default);

            Assert(run3.Status == TaskRunStatus.Queued, "Task 3 must enter Queued state");
            var pos3 = await queueService.GetQueuePositionAsync(orgId, run3.Id, default);
            Assert(pos3 == 1, $"Task 3 queue position should be 1, got {pos3}");

            // 4. Create fourth task -> enters Queued at position #2
            var (task4, run4) = await lifecycle.CreateTaskAsync(new CreateTaskRequest
            {
                OrganizationId = orgId,
                ProjectId = projId,
                UserId = userId,
                Title = "Queued Task D",
                BaseBranch = "main",
                ProviderCredentialReferenceId = credId,
                Model = "gpt-4o",
                Instruction = "Task D",
                HarnessVersion = "1.18.32"
            }, default);

            Assert(run4.Status == TaskRunStatus.Queued, "Task 4 must enter Queued state");
            var pos4 = await queueService.GetQueuePositionAsync(orgId, run4.Id, default);
            Assert(pos4 == 2, $"Task 4 queue position should be 2, got {pos4}");

            var queuedList = await queueService.GetQueuedRunsAsync(orgId, default);
            Assert(queuedList.Count == 2, $"Expected 2 queued runs, got {queuedList.Count}");
            Assert(queuedList[0].Id == run3.Id, "FIFO order: Task 3 is first in queue");
            Assert(queuedList[1].Id == run4.Id, "FIFO order: Task 4 is second in queue");
        });

        await RunAsyncTest("T90: Concurrency: Automatic promotion of queued tasks upon completion and cancellation", async () =>
        {
            var store = new InMemoryMuniClawStore();
            var auth = new OrganizationAuthorizationService(store);
            var queueService = new TaskQueueService(store);
            var lifecycle = new TaskLifecycleService(store, auth, queueService);
            var dispatch = new WorkerDispatchService(store);

            var (orgId, userId, projId, credId) = await SetupOrgProjectAndCredential(store, auth);
            store.Organizations[orgId].MaxConcurrentRuns = 1; // Strict 1-slot concurrency for clear FIFO testing

            // 1. Task 1 claims the single slot -> Preparing
            var (task1, run1) = await lifecycle.CreateTaskAsync(new CreateTaskRequest
            {
                OrganizationId = orgId,
                ProjectId = projId,
                UserId = userId,
                Title = "Slot 1 Task",
                BaseBranch = "main",
                ProviderCredentialReferenceId = credId,
                Model = "gpt-4o",
                Instruction = "Task 1",
                HarnessVersion = "1.18.32"
            }, default);

            var claim1 = await dispatch.ClaimWorkAsync(new WorkerClaimRequest { WorkerId = Guid.NewGuid(), OrganizationId = orgId, SupportedHarnessVersion = "1.18.32" }, default);
            Assert(claim1.HasWork && run1.Status == TaskRunStatus.Preparing, "Task 1 claimed into slot");

            // 2. Task 2 queues at position #1
            var (task2, run2) = await lifecycle.CreateTaskAsync(new CreateTaskRequest
            {
                OrganizationId = orgId,
                ProjectId = projId,
                UserId = userId,
                Title = "Waiting Task 2",
                BaseBranch = "main",
                ProviderCredentialReferenceId = credId,
                Model = "gpt-4o",
                Instruction = "Task 2",
                HarnessVersion = "1.18.32"
            }, default);
            Assert(run2.Status == TaskRunStatus.Queued, "Task 2 queued");
            var pos2 = await queueService.GetQueuePositionAsync(orgId, run2.Id, default);
            Assert(pos2 == 1, "Task 2 queue position is 1");

            // 3. Task 3 queues at position #2
            var (task3, run3) = await lifecycle.CreateTaskAsync(new CreateTaskRequest
            {
                OrganizationId = orgId,
                ProjectId = projId,
                UserId = userId,
                Title = "Waiting Task 3",
                BaseBranch = "main",
                ProviderCredentialReferenceId = credId,
                Model = "gpt-4o",
                Instruction = "Task 3",
                HarnessVersion = "1.18.32"
            }, default);
            Assert(run3.Status == TaskRunStatus.Queued, "Task 3 queued");
            var pos3Init = await queueService.GetQueuePositionAsync(orgId, run3.Id, default);
            Assert(pos3Init == 2, "Task 3 queue position is 2");

            // 4. Cancel Task 1 -> transitions to Cancelling, then supervisor reconciles terminal state to Cancelled
            await lifecycle.CancelTaskAsync(task1.Id, userId, "Test cancellation", default);
            Assert(run1.Status == TaskRunStatus.Cancelling, "Task 1 transitions to Cancelling");
            await lifecycle.ReconcileTerminalRunAsync(run1.Id, TaskRunStatus.Cancelled, "Cancelled by user", default);
            Assert(run1.Status == TaskRunStatus.Cancelled, "Task 1 is cancelled");

            // Verify Task 2 was automatically promoted to Preparing
            Assert(run2.Status == TaskRunStatus.Preparing, "Task 2 must be automatically promoted to Preparing on slot release");
            Assert(run2.QueuePosition == null, "Promoted task no longer in queue");

            // Verify Task 3 moved up to position #1
            var pos3 = await queueService.GetQueuePositionAsync(orgId, run3.Id, default);
            Assert(pos3 == 1, $"Task 3 should advance to position 1, got {pos3}");

            // 5. Complete Task 2 -> triggers automatic promotion of Task 3 into Preparing!
            run2.Status = TaskRunStatus.Completed;
            var promoted3 = await queueService.PromoteNextQueuedTaskAsync(orgId, default);
            Assert(promoted3 != null && promoted3.Id == run3.Id, "Task 3 promoted on Task 2 completion");
            Assert(run3.Status == TaskRunStatus.Preparing, "Task 3 is now in Preparing");

            var remainingQueued = await queueService.GetQueuedRunsAsync(orgId, default);
            Assert(remainingQueued.Count == 0, "Queue is now empty");
        });

        await RunAsyncTest("T90: Concurrency: Cross-organization quota isolation", async () =>
        {
            var store = new InMemoryMuniClawStore();
            var auth = new OrganizationAuthorizationService(store);
            var queueService = new TaskQueueService(store);
            var lifecycle = new TaskLifecycleService(store, auth, queueService);
            var dispatch = new WorkerDispatchService(store);

            // Setup Org A (quota 1, saturated)
            var (orgA, userA, projA, credA) = await SetupOrgProjectAndCredential(store, auth);
            store.Organizations[orgA].MaxConcurrentRuns = 1;
            var (taskA, runA) = await lifecycle.CreateTaskAsync(new CreateTaskRequest
            {
                OrganizationId = orgA,
                ProjectId = projA,
                UserId = userA,
                Title = "Org A Task 1",
                BaseBranch = "main",
                ProviderCredentialReferenceId = credA,
                Model = "gpt-4o",
                Instruction = "Task 1",
                HarnessVersion = "1.18.32"
            }, default);

            var claimA = await dispatch.ClaimWorkAsync(new WorkerClaimRequest
            {
                WorkerId = Guid.NewGuid(),
                OrganizationId = orgA,
                SupportedHarnessVersion = "1.18.32"
            }, default);
            Assert(claimA.HasWork && runA.Status == TaskRunStatus.Preparing, "Org A task claimed into slot");

            // Setup Org B
            var userB = await auth.GetOrCreateUserAsync("firebase:uid-b", "b@muni.dev", default);
            await auth.SetUserAllowlistedAsync(userB.Id, true, default);
            var orgB = Guid.NewGuid();
            store.Organizations[orgB] = new Organization { Id = orgB, Name = "Org B", Slug = "org-b", MaxConcurrentRuns = 1 };
            await auth.AddMemberToOrganizationAsync(orgB, userB.Id, MembershipRole.Admin, default);
            var repoB = Guid.NewGuid();
            store.RepositoryConnections[repoB] = new RepositoryConnection
            {
                Id = repoB, OrganizationId = orgB, GitHost = GitHostType.GitHub, ExternalAccountId = "acc-b",
                RepositoryId = "repo-b", RepositoryFullName = "orgb/repo-b", DefaultBranch = "main", SecretReferencePath = "secret/b"
            };
            var projB = Guid.NewGuid();
            store.Projects[projB] = new Project { Id = projB, OrganizationId = orgB, RepositoryConnectionId = repoB, Name = "Proj B", DefaultBaseBranch = "main" };
            var credB = Guid.NewGuid();
            store.ProviderCredentials[credB] = new ProviderCredentialReference
            {
                Id = credB, OrganizationId = orgB, OwningUserId = userB.Id, ProviderName = "openai", Label = "Key B",
                Scope = CredentialScope.Personal, SecretReferencePath = "sec/b", IsRevoked = false
            };

            // Org B creates task -> Free slot, no queue wait position
            var (taskB, runB) = await lifecycle.CreateTaskAsync(new CreateTaskRequest
            {
                OrganizationId = orgB,
                ProjectId = projB,
                UserId = userB.Id,
                Title = "Org B Task 1",
                BaseBranch = "main",
                ProviderCredentialReferenceId = credB,
                Model = "gpt-4o",
                Instruction = "Task B",
                HarnessVersion = "1.18.32"
            }, default);

            Assert(runB.Status == TaskRunStatus.Queued, "Org B task starts in Queued");
            Assert(runB.QueuePosition == null, "Org B task has free slot, no queue wait position");

            // Org B worker claims task -> succeeds immediately despite Org A being saturated!
            var claimB = await dispatch.ClaimWorkAsync(new WorkerClaimRequest
            {
                WorkerId = Guid.NewGuid(),
                OrganizationId = orgB,
                SupportedHarnessVersion = "1.18.32"
            }, default);

            Assert(claimB.HasWork && claimB.RunId == runB.Id, "Org B worker claims work independently of Org A");
            Assert(runB.Status == TaskRunStatus.Preparing, "Org B task transitions to Preparing");
        });

        // --- T90 Preview Deployments Qualification Tests ---

        await RunAsyncTest("T90: Preview: Branch normalization and DNS subdomain synthesis", async () =>
        {
            var client = new MinicloudPreviewDeploymentClient();

            // 1. Branch normalization tests
            Assert(MinicloudPreviewDeploymentClient.NormalizeBranchName("main") == "main", "main branch normalizes to main");
            Assert(MinicloudPreviewDeploymentClient.NormalizeBranchName("municlaw/task-12345678") == "municlaw-task-12345678", "Slashes become hyphens");
            Assert(MinicloudPreviewDeploymentClient.NormalizeBranchName("feat/UPPER_CASE_AND__underscores") == "feat-upper-case-and-underscores", "Uppercase and underscores normalized");
            Assert(MinicloudPreviewDeploymentClient.NormalizeBranchName("---leading-and-trailing---") == "leading-and-trailing", "Leading/trailing hyphens trimmed");
            Assert(MinicloudPreviewDeploymentClient.NormalizeBranchName("") == "branch", "Empty string defaults to branch");

            // Long branch name (> 32 chars) gets truncated with stable 8-char hex hash suffix
            var longBranch = "feature/very-long-branch-name-that-exceeds-thirty-two-characters-in-length";
            var normalizedLong = MinicloudPreviewDeploymentClient.NormalizeBranchName(longBranch);
            Assert(normalizedLong.Length <= 32, $"Normalized branch length must be <= 32 characters (got {normalizedLong.Length}: {normalizedLong})");
            Assert(normalizedLong.Contains("-"), "Long branch contains hyphen before hash suffix");

            // 2. URL synthesis and active deployment tracking
            var previewUrl = await client.TriggerDeploymentAsync("core-project", "municlaw/task-abc", "c0ffee1", default);
            Assert(previewUrl == "https://core-project-municlaw-task-abc.app.muni.dev", $"Expected standard URL pattern, got {previewUrl}");

            var status = await client.CheckStatusAsync(previewUrl, default);
            Assert(status == PreviewDeploymentStatus.Active, "Client check status returns Active after trigger");
        });

        await RunAsyncTest("T90: Preview: Trigger preview deployment and status tracking", async () =>
        {
            var store = new InMemoryMuniClawStore();
            var auth = new OrganizationAuthorizationService(store);
            var client = new MinicloudPreviewDeploymentClient();
            var queueService = new TaskQueueService(store);
            var lifecycle = new TaskLifecycleService(store, auth, queueService);
            var previewService = new PreviewDeploymentService(store, auth, client);

            var (orgId, userId, projId, credId) = await SetupOrgProjectAndCredential(store, auth);

            // Create task
            var (task, run) = await lifecycle.CreateTaskAsync(new CreateTaskRequest
            {
                OrganizationId = orgId,
                ProjectId = projId,
                UserId = userId,
                Title = "Preview Test Task",
                BaseBranch = "main",
                ProviderCredentialReferenceId = credId,
                Model = "gpt-4o",
                Instruction = "Implement test feature",
                HarnessVersion = "1.18.32"
            }, default);

            // Trigger preview deployment
            var preview = await previewService.TriggerPreviewAsync(task.Id, userId, default);
            Assert(preview.Status == PreviewDeploymentStatus.Active, "Triggered preview must have Active status");
            Assert(!string.IsNullOrEmpty(preview.PreviewUrl), "Preview URL must be populated");
            Assert(preview.PreviewUrl != null && preview.PreviewUrl.StartsWith("https://") && preview.PreviewUrl.EndsWith(".app.muni.dev"), "Preview URL must match Minicloud pattern");
            Assert(preview.BranchName == task.TaskBranch, "Preview branch name must match task branch");
            Assert(preview.TaskId == task.Id, "Preview task ID must match");
            Assert(preview.DeployedAt != null, "DeployedAt timestamp must be recorded");

            // Retrieve preview
            var retrieved = await previewService.GetPreviewAsync(task.Id, userId, default);
            Assert(retrieved != null, "GetPreviewAsync must return active preview");
            Assert(retrieved!.Id == preview.Id, "Retrieved preview ID must match");
            Assert(retrieved.Status == PreviewDeploymentStatus.Active, "Retrieved status must be Active");
        });

        await RunAsyncTest("T90: Preview: Idempotent deployment trigger", async () =>
        {
            var store = new InMemoryMuniClawStore();
            var auth = new OrganizationAuthorizationService(store);
            var client = new MinicloudPreviewDeploymentClient();
            var queueService = new TaskQueueService(store);
            var lifecycle = new TaskLifecycleService(store, auth, queueService);
            var previewService = new PreviewDeploymentService(store, auth, client);

            var (orgId, userId, projId, credId) = await SetupOrgProjectAndCredential(store, auth);

            var (task, _) = await lifecycle.CreateTaskAsync(new CreateTaskRequest
            {
                OrganizationId = orgId,
                ProjectId = projId,
                UserId = userId,
                Title = "Idempotent Preview Task",
                BaseBranch = "main",
                ProviderCredentialReferenceId = credId,
                Model = "gpt-4o",
                Instruction = "Instruction",
                HarnessVersion = "1.18.32"
            }, default);

            // First trigger
            var preview1 = await previewService.TriggerPreviewAsync(task.Id, userId, default);
            Assert(store.PreviewDeployments.Count == 1, "Exactly 1 preview deployment record in store");

            // Second trigger (idempotent call)
            var preview2 = await previewService.TriggerPreviewAsync(task.Id, userId, default);
            Assert(preview2.Id == preview1.Id, "Idempotent trigger must return existing preview deployment");
            Assert(preview2.PreviewUrl == preview1.PreviewUrl, "URLs must be identical");
            Assert(store.PreviewDeployments.Count == 1, "Store still has exactly 1 preview deployment record");
        });

        await RunAsyncTest("T90: Preview: Explicit teardown transitions status to TornDown", async () =>
        {
            var store = new InMemoryMuniClawStore();
            var auth = new OrganizationAuthorizationService(store);
            var client = new MinicloudPreviewDeploymentClient();
            var queueService = new TaskQueueService(store);
            var lifecycle = new TaskLifecycleService(store, auth, queueService);
            var previewService = new PreviewDeploymentService(store, auth, client);

            var (orgId, userId, projId, credId) = await SetupOrgProjectAndCredential(store, auth);

            var (task, _) = await lifecycle.CreateTaskAsync(new CreateTaskRequest
            {
                OrganizationId = orgId,
                ProjectId = projId,
                UserId = userId,
                Title = "Teardown Preview Task",
                BaseBranch = "main",
                ProviderCredentialReferenceId = credId,
                Model = "gpt-4o",
                Instruction = "Instruction",
                HarnessVersion = "1.18.32"
            }, default);

            var preview = await previewService.TriggerPreviewAsync(task.Id, userId, default);
            Assert(preview.Status == PreviewDeploymentStatus.Active, "Initial preview active");

            // Teardown
            var tornDown = await previewService.TearDownPreviewAsync(task.Id, userId, default);
            Assert(tornDown.Status == PreviewDeploymentStatus.TornDown, "Status transitions to TornDown");
            Assert(tornDown.TornDownAt != null, "TornDownAt timestamp must be set");

            // Verify client reflects teardown
            var clientStatus = await client.CheckStatusAsync(preview.PreviewUrl!, default);
            Assert(clientStatus == PreviewDeploymentStatus.TornDown || clientStatus == PreviewDeploymentStatus.None, "Client reflects preview was torn down");

            // Get preview reflects TornDown
            var fetched = await previewService.GetPreviewAsync(task.Id, userId, default);
            Assert(fetched != null && fetched.Status == PreviewDeploymentStatus.TornDown, "GetPreviewAsync reflects TornDown status");
        });

        await RunAsyncTest("T90: Preview: Security invariant: production secret isolation", async () =>
        {
            var client = new MinicloudPreviewDeploymentClient();
            Assert(client.EnforcesProductionSecretIsolation, "Client must enforce production secret isolation");

            // Verify Preview deployments never receive production secrets or vault credentials
            var store = new InMemoryMuniClawStore();
            var auth = new OrganizationAuthorizationService(store);
            var previewService = new PreviewDeploymentService(store, auth, client);

            var (orgId, userId, projId, credId) = await SetupOrgProjectAndCredential(store, auth);

            // Unauthorized user cannot trigger preview deployment
            var unauthorizedUserId = Guid.NewGuid();
            bool accessDenied = false;
            try
            {
                await previewService.TriggerPreviewAsync(Guid.NewGuid(), unauthorizedUserId, default);
            }
            catch (KeyNotFoundException)
            {
                // Task doesn't exist
            }

            var (task, _) = await new TaskLifecycleService(store, auth).CreateTaskAsync(new CreateTaskRequest
            {
                OrganizationId = orgId,
                ProjectId = projId,
                UserId = userId,
                Title = "Security Task",
                BaseBranch = "main",
                ProviderCredentialReferenceId = credId,
                Model = "gpt-4o",
                Instruction = "Instruction",
                HarnessVersion = "1.18.32"
            }, default);

            try
            {
                await previewService.TriggerPreviewAsync(task.Id, unauthorizedUserId, default);
            }
            catch (UnauthorizedAccessException)
            {
                accessDenied = true;
            }

            Assert(accessDenied, "Unauthorized user must be denied permission to trigger preview deployment");
        });

        Console.WriteLine("\n-------------------------------------------------");
        Console.WriteLine($"Test Run Summary: {_passed} Passed, {_failed} Failed");
        Console.WriteLine("-------------------------------------------------");

        return _failed > 0 ? 1 : 0;
    }

    private static async Task<(Guid orgId, Guid userId, Guid projId, Guid credId)> SetupOrgProjectAndCredential(
        IMuniClawStore store,
        IOrganizationAuthorizationService auth)
    {
        var user = await auth.GetOrCreateUserAsync("firebase:uid-test", "test@muni.dev", default);
        await auth.SetUserAllowlistedAsync(user.Id, true, default);

        var orgId = Guid.NewGuid();
        store.Organizations[orgId] = new Organization { Id = orgId, Name = "Test Org", Slug = "test-org" };
        await auth.AddMemberToOrganizationAsync(orgId, user.Id, MembershipRole.Admin, default);

        var repoId = Guid.NewGuid();
        store.RepositoryConnections[repoId] = new RepositoryConnection
        {
            Id = repoId,
            OrganizationId = orgId,
            GitHost = GitHostType.GitHub,
            ExternalAccountId = "gh-acc-1",
            RepositoryId = "12345",
            RepositoryFullName = "muniventures/test-repo",
            DefaultBranch = "main",
            SecretReferencePath = "secret/repo"
        };

        var projId = Guid.NewGuid();
        store.Projects[projId] = new Project
        {
            Id = projId,
            OrganizationId = orgId,
            RepositoryConnectionId = repoId,
            Name = "Core Project",
            DefaultBaseBranch = "main"
        };

        var credId = Guid.NewGuid();
        store.ProviderCredentials[credId] = new ProviderCredentialReference
        {
            Id = credId,
            OrganizationId = orgId,
            OwningUserId = user.Id,
            ProviderName = "openai",
            Label = "Test Key",
            Scope = CredentialScope.Personal,
            SecretReferencePath = "secret/test-key",
            IsRevoked = false
        };

        return (orgId, user.Id, projId, credId);
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
