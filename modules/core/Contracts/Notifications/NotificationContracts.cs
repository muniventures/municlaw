using System.Text.RegularExpressions;
using MuniClaw.Core.Models;

namespace MuniClaw.Core.Contracts.Notifications;

public sealed record CreateNotificationChannelRequest(
    string Name,
    NotificationChannelType ChannelType,
    string WebhookUrl,
    List<NotificationEventType> SubscribedEvents,
    string? SigningSecret = null)
{
    public Guid? OrganizationId { get; init; }
}

public sealed record UpdateNotificationChannelRequest(
    string Name,
    string? WebhookUrl,
    List<NotificationEventType> SubscribedEvents,
    bool IsEnabled,
    string? SigningSecret = null);

public sealed record NotificationChannelDto(
    Guid Id,
    Guid OrganizationId,
    NotificationChannelType ChannelType,
    string Name,
    string MaskedWebhookUrl,
    List<NotificationEventType> SubscribedEvents,
    bool IsEnabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastDispatchedAt,
    string? LastDispatchStatus)
{
    public static NotificationChannelDto FromModel(NotificationChannel channel)
    {
        return new NotificationChannelDto(
            channel.Id,
            channel.OrganizationId,
            channel.ChannelType,
            channel.Name,
            MaskWebhookUrl(channel.WebhookUrl),
            channel.SubscribedEvents,
            channel.IsEnabled,
            channel.CreatedAt,
            channel.LastDispatchedAt,
            channel.LastDispatchStatus);
    }

    public static string MaskWebhookUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return "********";
        }

        var query = uri.Query;
        if (!string.IsNullOrEmpty(query))
        {
            query = Regex.Replace(query, @"(=)[^&]+", "$1********");
        }

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length > 0)
        {
            if (uri.Host.Contains("slack.com", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.Contains("discord.com", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrEmpty(query) ||
                segments.Length > 1)
            {
                segments[^1] = "********";
            }
        }

        var path = "/" + string.Join("/", segments);
        var builder = new UriBuilder(uri)
        {
            Path = path,
            Query = query.TrimStart('?')
        };

        if (uri.IsDefaultPort)
        {
            builder.Port = -1;
        }

        return builder.Uri.ToString();
    }
}
