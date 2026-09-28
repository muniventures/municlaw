namespace MuniClaw.Core.Models;

public enum NotificationChannelType
{
    Slack,
    Discord,
    GenericWebhook
}

public enum NotificationEventType
{
    ApprovalRequested,
    TaskCompleted,
    TaskFailed,
    DeliveryPublished
}

public sealed class NotificationChannel
{
    public required Guid Id { get; set; }
    public required Guid OrganizationId { get; set; }
    public required NotificationChannelType ChannelType { get; set; }
    public required string Name { get; set; }
    public required string WebhookUrl { get; set; }
    public string? SigningSecret { get; set; }
    public List<NotificationEventType> SubscribedEvents { get; set; } = new();
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastDispatchedAt { get; set; }
    public string? LastDispatchStatus { get; set; }
}

public sealed class TaskLifecycleNotification
{
    public NotificationEventType EventType { get; set; }
    public Guid TaskId { get; set; }
    public Guid RunId { get; set; }
    public Guid OrganizationId { get; set; }
    public string TaskTitle { get; set; } = string.Empty;
    public string RepositoryName { get; set; } = string.Empty;
    public string Branch { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string Summary { get; set; } = string.Empty;
    public string ConsoleUrl { get; set; } = string.Empty;
}
