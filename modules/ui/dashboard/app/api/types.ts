export type TaskStatus = "Open" | "Archived";

export type TaskRunStatus =
  | "Queued"
  | "Preparing"
  | "Running"
  | "AwaitingInput"
  | "Completed"
  | "Failed"
  | "Cancelling"
  | "Cancelled";

export type TaskEventType =
  | "StatusChanged"
  | "StepStarted"
  | "StepEnded"
  | "StepFailed"
  | "TextDelta"
  | "ReasoningDelta"
  | "ToolCalled"
  | "ToolSuccess"
  | "ToolFailed"
  | "DiffUpdated"
  | "CheckExecuted"
  | "ApprovalRequested"
  | "ApprovalResolved"
  | "Error";

export type CapabilityCategory =
  | "WorktreeReadWrite"
  | "LocalGit"
  | "SandboxedCommand"
  | "PackageNetwork"
  | "ExternalNetwork"
  | "DestructiveWorkspace"
  | "SetupConfig"
  | "RemoteGitWrite"
  | "DraftDelivery"
  | "PlatformDenied";

export type ApprovalPolicySetting = "Allow" | "Ask" | "Deny";
export type ApprovalDecisionType = "AllowOnce" | "Deny";
export type ApprovalRecordStatus = "Pending" | "Approved" | "Rejected" | "Expired";
export type WorkspaceStatus = "Pending" | "Ready" | "Failed" | "Terminated";
export type GitHostType = "GitHub" | "GitLab";
export type CredentialScope = "Personal" | "Organization";
export type ModelProviderType = "Anthropic" | "OpenAI" | "OpenCode";

export interface User {
  id: string;
  externalSubjectId: string;
  email: string;
  isAllowlisted: boolean;
  createdAt: string;
}

export interface Organization {
  id: string;
  name: string;
  slug: string;
  createdAt: string;
  maxConcurrentRuns?: number;
}

export interface RepositoryConnection {
  id: string;
  organizationId: string;
  gitHost: GitHostType;
  externalAccountId: string;
  installationId?: string;
  repositoryId: string;
  repositoryFullName: string;
  defaultBranch: string;
  isActive: boolean;
  createdAt: string;
}

export interface Project {
  id: string;
  organizationId: string;
  repositoryConnectionId: string;
  name: string;
  defaultBaseBranch: string;
  setupCommands?: string;
  setupCommandsVersion: number;
  setupCommandsConfirmed: boolean;
  createdAt: string;
  repositoryConnection?: RepositoryConnection;
}

export interface OrganizationCredentialPolicy {
  allowedModels: string[];
  monthlySpendLimitUsd?: number | null;
  currentSpendUsd: number;
  adminOnly: boolean;
}

export interface RegisterOrganizationCredentialRequest {
  providerName: string;
  label: string;
  apiKey: string;
  policy?: OrganizationCredentialPolicy;
  allowedModels?: string[];
  monthlySpendLimitUsd?: number | null;
  adminOnly?: boolean;
}

export interface UpdateCredentialPolicyRequest {
  allowedModels: string[];
  monthlySpendLimitUsd?: number | null;
  adminOnly: boolean;
  currentSpendUsd?: number;
}

export interface ProviderCredentialReference {
  id: string;
  organizationId: string;
  owningUserId: string;
  providerName: string;
  label: string;
  scope: CredentialScope;
  isRevoked: boolean;
  createdAt: string;
  revokedAt?: string;
  policy?: OrganizationCredentialPolicy;
}

export interface TaskEntity {
  id: string;
  organizationId: string;
  projectId: string;
  title: string;
  status: TaskStatus;
  taskBranch: string;
  baseBranch: string;
  baseCommitSha?: string;
  createdByUserId: string;
  createdAt: string;
  archivedAt?: string;
  project?: Project;
  runs: TaskRun[];
  queuePosition?: number;
}

export interface TaskItem extends TaskEntity {
  queuePosition?: number;
}

export interface TaskRun {
  id: string;
  taskId: string;
  organizationId: string;
  runIndex: number;
  status: TaskRunStatus;
  queuePosition?: number;
  providerCredentialReferenceId: string;
  resolvedModel: string;
  instruction: string;
  harnessVersion: string;
  maxBudgetUsd?: number;
  createdAt: string;
  startedAt?: string;
  completedAt?: string;
  failureReason?: string;
  events?: TaskEventDto[];
  approvals?: ApprovalRequestRecord[];
}

export interface TaskQueueStatus {
  queuePosition?: number | null;
  activeRuns: number;
  maxConcurrentRuns: number;
}

export interface TaskEventDto {
  eventId: string;
  taskId: string;
  runId: string;
  sequenceNumber: number;
  cursor: string; // e.g. "{runId}:{sequenceNumber}"
  eventType: TaskEventType;
  timestamp: string;
  payloadJson: string;
  isTruncated: boolean;
}

export interface ApprovalRequestRecord {
  id: string;
  organizationId: string;
  taskId: string;
  runId: string;
  capability: CapabilityCategory;
  actionDescription: string;
  contentVersionHash: string;
  status: ApprovalRecordStatus;
  decidedByUserId?: string;
  decision?: ApprovalDecisionType;
  createdAt: string;
  decidedAt?: string;
}

export interface ApprovalRequestDto {
  approvalRequestId: string;
  organizationId: string;
  taskId: string;
  runId: string;
  capability: CapabilityCategory;
  actionDescription: string;
  contentVersionHash: string;
  createdAt: string;
  expiredAt?: string;
}

export interface ApprovalDecisionDto {
  approvalRequestId: string;
  decidedByUserId: string;
  decision: ApprovalDecisionType;
  validatedContentVersionHash: string;
  decidedAt: string;
}

export interface DeliveryRecord {
  id: string;
  organizationId: string;
  taskId: string;
  runId: string;
  gitHost: GitHostType;
  repositoryId: string;
  baseCommitSha: string;
  reviewedCommitSha: string;
  targetBranch: string;
  remotePrNumber?: string;
  remotePrUrl?: string;
  publishedCommitSha?: string;
  createdAt: string;
}

export interface CodingWorkspace {
  id: string;
  organizationId: string;
  minicloudServerId?: string;
  status: WorkspaceStatus;
  vpsIpAddress?: string;
  registrationToken?: string;
  createdAt: string;
  updatedAt?: string;
  failureReason?: string;
}

export interface UsageRecordEntity {
  id: string;
  organizationId: string;
  taskId: string;
  runId: string;
  provider: ModelProviderType;
  modelName: string;
  promptTokens: number;
  completionTokens: number;
  estimatedCostUsd?: number;
  priceProvenance: string;
  recordedAt: string;
}

// Check execution payload
export interface CheckResultPayload {
  command: string;
  exitCode: number;
  output: string;
  passed: boolean;
  durationMs: number;
}

// Diff payload
export interface DiffFileChange {
  path: string;
  status: "added" | "modified" | "deleted";
  additions: number;
  deletions: number;
  patch?: string;
}

export interface DiffPayload {
  baseCommitSha: string;
  headCommitSha: string;
  files: DiffFileChange[];
}

// Notification Channels and Webhooks
export type NotificationChannelType = "Slack" | "Discord" | "GenericWebhook";

export type NotificationEventType =
  | "ApprovalRequested"
  | "TaskCompleted"
  | "TaskFailed"
  | "DeliveryPublished";

export interface NotificationChannel {
  id: string;
  organizationId: string;
  channelType: NotificationChannelType;
  name: string;
  maskedWebhookUrl: string;
  subscribedEvents: NotificationEventType[];
  isEnabled: boolean;
  createdAt: string;
  lastDispatchedAt?: string;
  lastDispatchStatus?: string;
  hasSigningSecret?: boolean;
}

export interface CreateNotificationChannelRequest {
  name: string;
  channelType: NotificationChannelType;
  webhookUrl: string;
  subscribedEvents: NotificationEventType[];
  isEnabled?: boolean;
  signingSecret?: string;
}

export interface UpdateNotificationChannelRequest {
  name?: string;
  channelType?: NotificationChannelType;
  webhookUrl?: string;
  subscribedEvents?: NotificationEventType[];
  isEnabled?: boolean;
  signingSecret?: string;
}

