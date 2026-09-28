using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MuniClaw.Core.Contracts.Notifications;
using MuniClaw.Core.Data;
using MuniClaw.Core.Models;

namespace MuniClaw.Core.Services;

public static class NotificationPayloadFormatter
{
    public static string FormatSlack(TaskLifecycleNotification n)
    {
        var titleText = $"MuniClaw: {n.EventType} - {n.TaskTitle}";
        if (titleText.Length > 150)
        {
            titleText = titleText[..147] + "...";
        }

        var payload = new
        {
            text = $"MuniClaw: {n.EventType} - {n.TaskTitle}",
            blocks = new object[]
            {
                new
                {
                    type = "header",
                    text = new
                    {
                        type = "plain_text",
                        text = titleText,
                        emoji = true
                    }
                },
                new
                {
                    type = "section",
                    fields = new object[]
                    {
                        new { type = "mrkdwn", text = $"*Repository:*\n{n.RepositoryName}" },
                        new { type = "mrkdwn", text = $"*Branch:*\n{n.Branch}" },
                        new { type = "mrkdwn", text = $"*Event:*\n{n.EventType}" },
                        new { type = "mrkdwn", text = $"*Time:*\n{n.Timestamp:yyyy-MM-dd HH:mm:ss} UTC" }
                    }
                },
                new
                {
                    type = "section",
                    text = new
                    {
                        type = "mrkdwn",
                        text = $"*Summary:*\n{n.Summary}"
                    }
                },
                new
                {
                    type = "actions",
                    elements = new object[]
                    {
                        new
                        {
                            type = "button",
                            text = new
                            {
                                type = "plain_text",
                                text = "View in Console",
                                emoji = true
                            },
                            url = n.ConsoleUrl,
                            style = "primary"
                        }
                    }
                }
            }
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    public static string FormatDiscord(TaskLifecycleNotification n)
    {
        var color = n.EventType switch
        {
            NotificationEventType.ApprovalRequested => 0xF59E0B,
            NotificationEventType.TaskCompleted => 0x10B981,
            NotificationEventType.TaskFailed => 0xEF4444,
            NotificationEventType.DeliveryPublished => 0x3B82F6,
            _ => 0x6B7280
        };

        var payload = new
        {
            embeds = new object[]
            {
                new
                {
                    title = $"MuniClaw: {n.EventType} - {n.TaskTitle}",
                    description = n.Summary,
                    url = n.ConsoleUrl,
                    color = color,
                    fields = new object[]
                    {
                        new { name = "Repository", value = n.RepositoryName, @inline = true },
                        new { name = "Branch", value = n.Branch, @inline = true },
                        new { name = "Event", value = n.EventType.ToString(), @inline = true },
                        new { name = "Summary", value = n.Summary, @inline = false }
                    },
                    timestamp = n.Timestamp.ToString("o")
                }
            }
        };

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        });
    }

    public static string FormatGeneric(TaskLifecycleNotification n)
    {
        return JsonSerializer.Serialize(n, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
    }

    public static string ComputeHmacSignature(string secret, string payload, long timestamp)
    {
        if (string.IsNullOrEmpty(secret))
        {
            return string.Empty;
        }

        var message = $"{timestamp}.{payload}";
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var messageBytes = Encoding.UTF8.GetBytes(message);
        using var hmac = new HMACSHA256(keyBytes);
        var hashBytes = hmac.ComputeHash(messageBytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    public static string ComputeHmacSignature(string secret, string payload)
    {
        return ComputeHmacSignature(secret, payload, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    }

    public static string MaskWebhookUrl(string? url)
    {
        return NotificationChannelDto.MaskWebhookUrl(url);
    }
}

public interface INotificationService
{
    Task<NotificationChannel> CreateChannelAsync(Guid organizationId, CreateNotificationChannelRequest request, CancellationToken ct = default);
    Task<NotificationChannel> CreateChannelAsync(CreateNotificationChannelRequest request, CancellationToken ct = default);
    Task<NotificationChannel> UpdateChannelAsync(Guid organizationId, Guid channelId, UpdateNotificationChannelRequest request, CancellationToken ct = default);
    Task<NotificationChannel> UpdateChannelAsync(Guid channelId, UpdateNotificationChannelRequest request, CancellationToken ct = default);
    Task<bool> DeleteChannelAsync(Guid organizationId, Guid channelId, CancellationToken ct = default);
    Task<bool> DeleteChannelAsync(Guid channelId, CancellationToken ct = default);
    Task<IReadOnlyList<NotificationChannel>> ListChannelsAsync(Guid organizationId, CancellationToken ct = default);
    Task<NotificationChannel?> GetChannelAsync(Guid organizationId, Guid channelId, CancellationToken ct = default);
    Task<NotificationChannel?> GetChannelAsync(Guid channelId, CancellationToken ct = default);
    Task DispatchNotificationAsync(TaskLifecycleNotification notification, CancellationToken ct = default);
    Task<bool> SendTestPingAsync(Guid orgId, Guid channelId, Guid actorUserId, CancellationToken ct = default);
    Task<bool> SendTestPingAsync(Guid channelId, Guid actorUserId, CancellationToken ct = default);
}

public sealed class NotificationService : INotificationService
{
    private static readonly HttpClient DefaultHttpClient = new();
    private readonly IMuniClawStore _store;
    private readonly HttpClient _httpClient;

    public NotificationService(IMuniClawStore store, HttpClient? httpClient = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _httpClient = httpClient ?? DefaultHttpClient;
    }

    public Task<NotificationChannel> CreateChannelAsync(Guid organizationId, CreateNotificationChannelRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WebhookUrl);

        if (!Uri.TryCreate(request.WebhookUrl, UriKind.Absolute, out _))
        {
            throw new ArgumentException("WebhookUrl must be a valid absolute URI.", nameof(request));
        }

        var events = request.SubscribedEvents is { Count: > 0 }
            ? new List<NotificationEventType>(request.SubscribedEvents)
            : new List<NotificationEventType>
            {
                NotificationEventType.ApprovalRequested,
                NotificationEventType.TaskCompleted,
                NotificationEventType.TaskFailed,
                NotificationEventType.DeliveryPublished
            };

        string? signingSecret = request.SigningSecret;
        if (request.ChannelType == NotificationChannelType.GenericWebhook && string.IsNullOrWhiteSpace(signingSecret))
        {
            signingSecret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        }

        var channel = new NotificationChannel
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ChannelType = request.ChannelType,
            Name = request.Name.Trim(),
            WebhookUrl = request.WebhookUrl.Trim(),
            SigningSecret = signingSecret,
            SubscribedEvents = events,
            IsEnabled = true,
            CreatedAt = DateTimeOffset.UtcNow,
            LastDispatchedAt = null,
            LastDispatchStatus = null
        };

        _store.NotificationChannels[channel.Id] = channel;
        PersistSnapshot();

        return Task.FromResult(channel);
    }

    public Task<NotificationChannel> CreateChannelAsync(CreateNotificationChannelRequest request, CancellationToken ct = default)
    {
        var orgId = request.OrganizationId ?? _store.Organizations.Keys.FirstOrDefault();
        return CreateChannelAsync(orgId, request, ct);
    }

    public Task<NotificationChannel> UpdateChannelAsync(Guid organizationId, Guid channelId, UpdateNotificationChannelRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);

        if (!_store.NotificationChannels.TryGetValue(channelId, out var channel))
        {
            throw new KeyNotFoundException($"Notification channel {channelId} was not found.");
        }

        if (organizationId != Guid.Empty && channel.OrganizationId != organizationId)
        {
            throw new KeyNotFoundException($"Notification channel {channelId} not found for organization {organizationId}.");
        }

        if (!string.IsNullOrWhiteSpace(request.WebhookUrl))
        {
            if (!Uri.TryCreate(request.WebhookUrl, UriKind.Absolute, out _))
            {
                throw new ArgumentException("WebhookUrl must be a valid absolute URI.", nameof(request));
            }
            channel.WebhookUrl = request.WebhookUrl.Trim();
        }

        channel.Name = request.Name.Trim();
        if (request.SubscribedEvents != null)
        {
            channel.SubscribedEvents = new List<NotificationEventType>(request.SubscribedEvents);
        }
        channel.IsEnabled = request.IsEnabled;
        if (request.SigningSecret != null)
        {
            channel.SigningSecret = request.SigningSecret;
        }

        PersistSnapshot();
        return Task.FromResult(channel);
    }

    public Task<NotificationChannel> UpdateChannelAsync(Guid channelId, UpdateNotificationChannelRequest request, CancellationToken ct = default)
    {
        return UpdateChannelAsync(Guid.Empty, channelId, request, ct);
    }

    public Task<bool> DeleteChannelAsync(Guid organizationId, Guid channelId, CancellationToken ct = default)
    {
        if (!_store.NotificationChannels.TryGetValue(channelId, out var channel))
        {
            return Task.FromResult(false);
        }

        if (organizationId != Guid.Empty && channel.OrganizationId != organizationId)
        {
            return Task.FromResult(false);
        }

        var removed = _store.NotificationChannels.TryRemove(channelId, out _);
        if (removed)
        {
            PersistSnapshot();
        }

        return Task.FromResult(removed);
    }

    public Task<bool> DeleteChannelAsync(Guid channelId, CancellationToken ct = default)
    {
        return DeleteChannelAsync(Guid.Empty, channelId, ct);
    }

    public Task<IReadOnlyList<NotificationChannel>> ListChannelsAsync(Guid organizationId, CancellationToken ct = default)
    {
        IReadOnlyList<NotificationChannel> list = _store.NotificationChannels.Values
            .Where(c => organizationId == Guid.Empty || c.OrganizationId == organizationId)
            .OrderByDescending(c => c.CreatedAt)
            .ToList();

        return Task.FromResult(list);
    }

    public Task<NotificationChannel?> GetChannelAsync(Guid organizationId, Guid channelId, CancellationToken ct = default)
    {
        if (_store.NotificationChannels.TryGetValue(channelId, out var channel))
        {
            if (organizationId == Guid.Empty || channel.OrganizationId == organizationId)
            {
                return Task.FromResult<NotificationChannel?>(channel);
            }
        }

        return Task.FromResult<NotificationChannel?>(null);
    }

    public Task<NotificationChannel?> GetChannelAsync(Guid channelId, CancellationToken ct = default)
    {
        return GetChannelAsync(Guid.Empty, channelId, ct);
    }

    public async Task DispatchNotificationAsync(TaskLifecycleNotification notification, CancellationToken ct = default)
    {
        if (notification == null)
        {
            return;
        }

        var matchingChannels = _store.NotificationChannels.Values
            .Where(c => c.IsEnabled &&
                        (c.OrganizationId == Guid.Empty || c.OrganizationId == notification.OrganizationId) &&
                        c.SubscribedEvents.Contains(notification.EventType))
            .ToList();

        if (matchingChannels.Count == 0)
        {
            return;
        }

        var tasks = matchingChannels.Select(channel => DispatchToChannelAsync(channel, notification, ct));
        await Task.WhenAll(tasks);
    }

    public async Task<bool> SendTestPingAsync(Guid orgId, Guid channelId, Guid actorUserId, CancellationToken ct = default)
    {
        if (!_store.NotificationChannels.TryGetValue(channelId, out var channel))
        {
            throw new KeyNotFoundException($"Notification channel {channelId} was not found.");
        }

        if (orgId != Guid.Empty && channel.OrganizationId != orgId)
        {
            throw new UnauthorizedAccessException($"Channel {channelId} does not belong to organization {orgId}.");
        }

        var testNotification = new TaskLifecycleNotification
        {
            EventType = NotificationEventType.TaskCompleted,
            TaskId = Guid.NewGuid(),
            RunId = Guid.NewGuid(),
            OrganizationId = channel.OrganizationId,
            TaskTitle = "MuniClaw Test Ping",
            RepositoryName = "municlaw/verification",
            Branch = "main",
            Timestamp = DateTimeOffset.UtcNow,
            Summary = $"Webhook test ping dispatched by user {actorUserId}.",
            ConsoleUrl = "https://municlaw.local"
        };

        await DispatchToChannelAsync(channel, testNotification, ct);
        return channel.LastDispatchStatus == "Success";
    }

    public Task<bool> SendTestPingAsync(Guid channelId, Guid actorUserId, CancellationToken ct = default)
    {
        if (!_store.NotificationChannels.TryGetValue(channelId, out var channel))
        {
            throw new KeyNotFoundException($"Notification channel {channelId} was not found.");
        }
        return SendTestPingAsync(channel.OrganizationId, channelId, actorUserId, ct);
    }

    private async Task DispatchToChannelAsync(NotificationChannel channel, TaskLifecycleNotification notification, CancellationToken ct)
    {
        try
        {
            string payload = channel.ChannelType switch
            {
                NotificationChannelType.Slack => NotificationPayloadFormatter.FormatSlack(notification),
                NotificationChannelType.Discord => NotificationPayloadFormatter.FormatDiscord(notification),
                NotificationChannelType.GenericWebhook => NotificationPayloadFormatter.FormatGeneric(notification),
                _ => NotificationPayloadFormatter.FormatGeneric(notification)
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, channel.WebhookUrl)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/json")
            };

            if (channel.ChannelType == NotificationChannelType.GenericWebhook)
            {
                var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                request.Headers.Add("X-MuniClaw-Timestamp", timestamp.ToString());

                if (!string.IsNullOrEmpty(channel.SigningSecret))
                {
                    var sig = NotificationPayloadFormatter.ComputeHmacSignature(channel.SigningSecret, payload, timestamp);
                    request.Headers.Add("X-MuniClaw-Signature", sig);
                }
            }

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(10));

            var response = await _httpClient.SendAsync(request, cts.Token);
            channel.LastDispatchedAt = DateTimeOffset.UtcNow;
            if (response.IsSuccessStatusCode)
            {
                channel.LastDispatchStatus = "Success";
            }
            else
            {
                channel.LastDispatchStatus = $"Failed: HTTP {(int)response.StatusCode}";
            }
        }
        catch (Exception ex)
        {
            channel.LastDispatchedAt = DateTimeOffset.UtcNow;
            channel.LastDispatchStatus = $"Error: {ex.Message}";
        }
        finally
        {
            PersistSnapshot();
        }
    }

    private void PersistSnapshot()
    {
        if (_store is MuniClawFileStore fileStore)
        {
            fileStore.SaveSnapshot();
        }
    }
}
