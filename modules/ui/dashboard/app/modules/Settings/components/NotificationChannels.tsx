import * as React from "react";
import {
  Bell,
  Plus,
  Trash2,
  Pencil,
  Send,
  CheckCircle2,
  AlertCircle,
  RefreshCw,
  KeyRound,
  Webhook,
  Hash,
  X,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select } from "@/components/ui/select";
import { Badge } from "@/components/ui/badge";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
  DialogFooter,
} from "@/components/ui/dialog";
import type {
  CreateNotificationChannelRequest,
  NotificationChannel,
  NotificationChannelType,
  NotificationEventType,
  UpdateNotificationChannelRequest,
} from "@/api/types";
import {
  listChannels,
  createChannel,
  updateChannel,
  deleteChannel,
  sendTestPing,
} from "@/api/notifications";

interface NotificationChannelsProps {
  organizationId: string;
  className?: string;
}

const EVENT_CONFIGS: {
  type: NotificationEventType;
  label: string;
  description: string;
  badgeVariant: "warning" | "success" | "destructive" | "info";
}[] = [
  {
    type: "ApprovalRequested",
    label: "Approval Requested",
    description: "Task paused and awaiting human approval sign-off.",
    badgeVariant: "warning",
  },
  {
    type: "TaskCompleted",
    label: "Task Completed",
    description: "Task coding finished with reviewable diff and check results.",
    badgeVariant: "success",
  },
  {
    type: "TaskFailed",
    label: "Task Failed",
    description: "Task execution halted due to failure or timeout.",
    badgeVariant: "destructive",
  },
  {
    type: "DeliveryPublished",
    label: "Delivery Published",
    description: "Draft PR or MR successfully published upstream.",
    badgeVariant: "info",
  },
];

const ALL_EVENTS: NotificationEventType[] = [
  "ApprovalRequested",
  "TaskCompleted",
  "TaskFailed",
  "DeliveryPublished",
];

function generateSecret(): string {
  if (typeof crypto !== "undefined" && crypto.getRandomValues) {
    const array = new Uint8Array(24);
    crypto.getRandomValues(array);
    return Array.from(array, (byte) => byte.toString(16).padStart(2, "0")).join("");
  }
  return "mc_sec_" + Math.random().toString(36).slice(2) + Math.random().toString(36).slice(2);
}

function getChannelTypeIcon(type: NotificationChannelType) {
  switch (type) {
    case "Slack":
      return <Hash className="h-4 w-4 text-[#E01E5A]" />;
    case "Discord":
      return <Bell className="h-4 w-4 text-[#5865F2]" />;
    case "GenericWebhook":
      return <Webhook className="h-4 w-4 text-emerald-500" />;
  }
}

function getChannelTypeBadge(type: NotificationChannelType) {
  switch (type) {
    case "Slack":
      return (
        <span className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-semibold bg-[#4A154B]/10 text-[#4A154B] dark:bg-[#E01E5A]/15 dark:text-[#E01E5A] border border-[#4A154B]/20 dark:border-[#E01E5A]/30">
          <Hash className="h-3 w-3" /> Slack
        </span>
      );
    case "Discord":
      return (
        <span className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-semibold bg-[#5865F2]/10 text-[#5865F2] dark:bg-[#5865F2]/20 dark:text-[#7983F5] border border-[#5865F2]/20">
          <Bell className="h-3 w-3" /> Discord
        </span>
      );
    case "GenericWebhook":
      return (
        <span className="inline-flex items-center gap-1.5 px-2.5 py-0.5 rounded-full text-xs font-semibold bg-emerald-500/10 text-emerald-700 dark:text-emerald-400 border border-emerald-500/20">
          <Webhook className="h-3 w-3" /> Webhook
        </span>
      );
  }
}

interface FeedbackState {
  type: "success" | "error";
  channelId?: string;
  message: string;
}

export function NotificationChannels({
  organizationId,
  className = "",
}: NotificationChannelsProps) {
  const [channels, setChannels] = React.useState<NotificationChannel[]>([]);
  const [isLoading, setIsLoading] = React.useState(true);
  const [loadError, setLoadError] = React.useState<string | null>(null);

  // Dialog state
  const [isDialogOpen, setIsDialogOpen] = React.useState(false);
  const [dialogMode, setDialogMode] = React.useState<"add" | "edit">("add");
  const [editingChannelId, setEditingChannelId] = React.useState<string | null>(null);

  // Dialog form state
  const [formName, setFormName] = React.useState("");
  const [formChannelType, setFormChannelType] = React.useState<NotificationChannelType>("Slack");
  const [formWebhookUrl, setFormWebhookUrl] = React.useState("");
  const [formSubscribedEvents, setFormSubscribedEvents] = React.useState<NotificationEventType[]>([
    ...ALL_EVENTS,
  ]);
  const [formSigningSecret, setFormSigningSecret] = React.useState("");
  const [formIsEnabled, setFormIsEnabled] = React.useState(true);
  const [formError, setFormError] = React.useState<string | null>(null);
  const [isSubmitting, setIsSubmitting] = React.useState(false);

  // Action states
  const [testingChannelId, setTestingChannelId] = React.useState<string | null>(null);
  const [togglingChannelId, setTogglingChannelId] = React.useState<string | null>(null);
  const [deletingChannelId, setDeletingChannelId] = React.useState<string | null>(null);
  const [feedback, setFeedback] = React.useState<FeedbackState | null>(null);

  const fetchChannels = React.useCallback(async () => {
    try {
      setIsLoading(true);
      setLoadError(null);
      const data = await listChannels(organizationId);
      setChannels(data);
    } catch (err: unknown) {
      setLoadError(err instanceof Error ? err.message : "Failed to load notification channels");
    } finally {
      setIsLoading(false);
    }
  }, [organizationId]);

  React.useEffect(() => {
    fetchChannels();
  }, [fetchChannels]);

  // Auto-dismiss feedback after 5 seconds
  React.useEffect(() => {
    if (!feedback) return;
    const timer = setTimeout(() => {
      setFeedback(null);
    }, 5000);
    return () => clearTimeout(timer);
  }, [feedback]);

  const handleOpenAddDialog = () => {
    setDialogMode("add");
    setEditingChannelId(null);
    setFormName("");
    setFormChannelType("Slack");
    setFormWebhookUrl("");
    setFormSubscribedEvents([...ALL_EVENTS]);
    setFormSigningSecret("");
    setFormIsEnabled(true);
    setFormError(null);
    setIsDialogOpen(true);
  };

  const handleOpenEditDialog = (channel: NotificationChannel) => {
    setDialogMode("edit");
    setEditingChannelId(channel.id);
    setFormName(channel.name);
    setFormChannelType(channel.channelType);
    setFormWebhookUrl(""); // Write-only / blank to keep existing
    setFormSubscribedEvents([...channel.subscribedEvents]);
    setFormSigningSecret("");
    setFormIsEnabled(channel.isEnabled);
    setFormError(null);
    setIsDialogOpen(true);
  };

  const handleToggleEventSubscription = (eventType: NotificationEventType) => {
    setFormSubscribedEvents((prev) =>
      prev.includes(eventType) ? prev.filter((e) => e !== eventType) : [...prev, eventType]
    );
  };

  const handleSelectAllEvents = () => {
    setFormSubscribedEvents([...ALL_EVENTS]);
  };

  const handleDeselectAllEvents = () => {
    setFormSubscribedEvents([]);
  };

  const handleGenerateSecret = () => {
    setFormSigningSecret(generateSecret());
  };

  const handleDialogSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!formName.trim()) {
      setFormError("Channel name is required.");
      return;
    }

    if (dialogMode === "add" && !formWebhookUrl.trim()) {
      setFormError("Webhook URL is required.");
      return;
    }

    if (formWebhookUrl.trim() && !/^https?:\/\/.+/i.test(formWebhookUrl.trim())) {
      setFormError("Webhook URL must start with http:// or https://");
      return;
    }

    if (formSubscribedEvents.length === 0) {
      setFormError("Please select at least one subscribable event.");
      return;
    }

    try {
      setIsSubmitting(true);
      setFormError(null);

      if (dialogMode === "add") {
        const payload: CreateNotificationChannelRequest = {
          name: formName.trim(),
          channelType: formChannelType,
          webhookUrl: formWebhookUrl.trim(),
          subscribedEvents: formSubscribedEvents,
          isEnabled: formIsEnabled,
          signingSecret:
            formChannelType === "GenericWebhook" && formSigningSecret.trim()
              ? formSigningSecret.trim()
              : undefined,
        };

        const newChannel = await createChannel(organizationId, payload);
        setChannels((prev) => [...prev, newChannel]);
        setFeedback({
          type: "success",
          channelId: newChannel.id,
          message: `Notification channel "${newChannel.name}" created successfully.`,
        });
      } else if (dialogMode === "edit" && editingChannelId) {
        const payload: UpdateNotificationChannelRequest = {
          name: formName.trim(),
          channelType: formChannelType,
          subscribedEvents: formSubscribedEvents,
          isEnabled: formIsEnabled,
        };

        if (formWebhookUrl.trim()) {
          payload.webhookUrl = formWebhookUrl.trim();
        }

        if (formChannelType === "GenericWebhook" && formSigningSecret.trim()) {
          payload.signingSecret = formSigningSecret.trim();
        }

        const updated = await updateChannel(organizationId, editingChannelId, payload);
        setChannels((prev) => prev.map((c) => (c.id === updated.id ? updated : c)));
        setFeedback({
          type: "success",
          channelId: updated.id,
          message: `Notification channel "${updated.name}" updated successfully.`,
        });
      }

      setIsDialogOpen(false);
    } catch (err: unknown) {
      setFormError(err instanceof Error ? err.message : "Failed to save channel.");
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleToggleEnabled = async (channel: NotificationChannel) => {
    const nextState = !channel.isEnabled;
    try {
      setTogglingChannelId(channel.id);
      // Optimistic update
      setChannels((prev) =>
        prev.map((c) => (c.id === channel.id ? { ...c, isEnabled: nextState } : c))
      );

      const updated = await updateChannel(organizationId, channel.id, {
        isEnabled: nextState,
      });

      setChannels((prev) => prev.map((c) => (c.id === updated.id ? updated : c)));
      setFeedback({
        type: "success",
        channelId: channel.id,
        message: `Channel "${channel.name}" ${nextState ? "enabled" : "disabled"}.`,
      });
    } catch (err: unknown) {
      // Revert on error
      setChannels((prev) =>
        prev.map((c) => (c.id === channel.id ? { ...c, isEnabled: channel.isEnabled } : c))
      );
      setFeedback({
        type: "error",
        channelId: channel.id,
        message: err instanceof Error ? err.message : "Failed to update channel status.",
      });
    } finally {
      setTogglingChannelId(null);
    }
  };

  const handleTestPing = async (channel: NotificationChannel) => {
    try {
      setTestingChannelId(channel.id);
      setFeedback(null);
      const res = await sendTestPing(organizationId, channel.id);

      // Update last dispatched status locally
      const now = new Date().toISOString();
      setChannels((prev) =>
        prev.map((c) =>
          c.id === channel.id
            ? { ...c, lastDispatchedAt: now, lastDispatchStatus: res.success ? "Success" : "Failed" }
            : c
        )
      );

      setFeedback({
        type: res.success ? "success" : "error",
        channelId: channel.id,
        message: res.message || (res.success ? "Test ping delivered successfully!" : "Test ping failed."),
      });
    } catch (err: unknown) {
      setFeedback({
        type: "error",
        channelId: channel.id,
        message: err instanceof Error ? err.message : "Test ping failed.",
      });
    } finally {
      setTestingChannelId(null);
    }
  };

  const handleDelete = async (channel: NotificationChannel) => {
    const confirmed = window.confirm(
      `Are you sure you want to delete the notification channel "${channel.name}"? This action cannot be undone.`
    );
    if (!confirmed) return;

    try {
      setDeletingChannelId(channel.id);
      await deleteChannel(organizationId, channel.id);
      setChannels((prev) => prev.filter((c) => c.id !== channel.id));
      setFeedback({
        type: "success",
        message: `Channel "${channel.name}" has been deleted.`,
      });
    } catch (err: unknown) {
      setFeedback({
        type: "error",
        channelId: channel.id,
        message: err instanceof Error ? err.message : "Failed to delete notification channel.",
      });
    } finally {
      setDeletingChannelId(null);
    }
  };

  return (
    <div className={`space-y-6 ${className}`}>
      {/* Header and Add Button */}
      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
        <div>
          <h2 className="text-lg font-semibold tracking-tight text-foreground flex items-center gap-2">
            <Bell className="h-5 w-5 text-primary" />
            <span>Notification Channels</span>
          </h2>
          <p className="text-xs text-muted-foreground mt-0.5">
            Dispatch outbound webhook notifications for human approvals, task completions, failures, and PR deliveries to Slack, Discord, or generic endpoints.
          </p>
        </div>

        <div className="flex items-center gap-2 shrink-0">
          <Button
            variant="outline"
            size="sm"
            onClick={fetchChannels}
            disabled={isLoading}
            className="gap-1.5 text-xs"
          >
            <RefreshCw className={`h-3.5 w-3.5 ${isLoading ? "animate-spin" : ""}`} />
            <span>Refresh</span>
          </Button>
          <Button
            size="sm"
            onClick={handleOpenAddDialog}
            className="gap-1.5 text-xs"
          >
            <Plus className="h-4 w-4" />
            <span>Add Channel</span>
          </Button>
        </div>
      </div>

      {/* Global Feedback Alert */}
      {feedback && (
        <div
          role="alert"
          className={`p-4 rounded-xl border flex items-start justify-between gap-3 text-sm animate-in fade-in duration-200 ${
            feedback.type === "success"
              ? "bg-emerald-500/10 border-emerald-500/20 text-emerald-700 dark:text-emerald-300"
              : "bg-destructive/10 border-destructive/20 text-destructive dark:text-red-400"
          }`}
        >
          <div className="flex items-center gap-2.5">
            {feedback.type === "success" ? (
              <CheckCircle2 className="h-5 w-5 shrink-0 text-emerald-600 dark:text-emerald-400" />
            ) : (
              <AlertCircle className="h-5 w-5 shrink-0 text-destructive" />
            )}
            <span className="font-medium text-xs sm:text-sm">{feedback.message}</span>
          </div>
          <button
            type="button"
            onClick={() => setFeedback(null)}
            className="opacity-70 hover:opacity-100 p-0.5 rounded cursor-pointer"
            aria-label="Close notification"
          >
            <X className="h-4 w-4" />
          </button>
        </div>
      )}

      {/* Load Error */}
      {loadError && (
        <div className="p-4 rounded-xl border border-destructive/20 bg-destructive/10 text-destructive text-xs flex items-center justify-between gap-3">
          <div className="flex items-center gap-2">
            <AlertCircle className="h-4 w-4 shrink-0" />
            <span>{loadError}</span>
          </div>
          <Button variant="outline" size="sm" onClick={fetchChannels} className="text-xs h-7">
            Retry
          </Button>
        </div>
      )}

      {/* Channel List or Empty State */}
      {isLoading ? (
        <div className="p-12 text-center text-muted-foreground font-mono text-xs rounded-xl border border-border bg-card">
          <RefreshCw className="h-5 w-5 animate-spin mx-auto mb-2 text-muted-foreground" />
          Loading notification channels...
        </div>
      ) : channels.length === 0 ? (
        <div className="rounded-xl border border-dashed border-border bg-card/50 p-10 text-center space-y-4">
          <div className="mx-auto flex h-12 w-12 items-center justify-center rounded-full bg-primary/10 text-primary">
            <Bell className="h-6 w-6" />
          </div>
          <div className="max-w-md mx-auto space-y-1">
            <h3 className="text-sm font-semibold text-foreground">No notification channels configured</h3>
            <p className="text-xs text-muted-foreground">
              Connect Slack, Discord, or generic webhooks to receive real-time updates when tasks need approval, finish coding, or open draft pull requests.
            </p>
          </div>
          <Button onClick={handleOpenAddDialog} size="sm" className="gap-1.5 text-xs">
            <Plus className="h-3.5 w-3.5" />
            <span>Add First Channel</span>
          </Button>
        </div>
      ) : (
        <div className="grid grid-cols-1 gap-4">
          {channels.map((channel) => {
            const isTesting = testingChannelId === channel.id;
            const isToggling = togglingChannelId === channel.id;
            const isDeleting = deletingChannelId === channel.id;

            return (
              <div
                key={channel.id}
                className={`rounded-xl border border-border bg-card p-5 shadow-xs transition-all ${
                  !channel.isEnabled ? "opacity-75 bg-muted/20" : ""
                }`}
              >
                <div className="flex flex-col md:flex-row md:items-start justify-between gap-4">
                  {/* Left Column: Channel Details */}
                  <div className="space-y-2.5 flex-1 min-w-0">
                    <div className="flex flex-wrap items-center gap-2.5">
                      {getChannelTypeBadge(channel.channelType)}
                      <h3 className="font-semibold text-sm text-foreground truncate">
                        {channel.name}
                      </h3>
                      {!channel.isEnabled && (
                        <Badge variant="secondary" className="text-[10px]">
                          Disabled
                        </Badge>
                      )}
                    </div>

                    {/* Masked Webhook URL */}
                    <div className="flex items-center gap-2 text-xs">
                      <span className="text-muted-foreground font-mono text-[11px] bg-muted/60 px-2 py-0.5 rounded border border-border/50 truncate max-w-lg">
                        {channel.maskedWebhookUrl || "••••••••••••••••••••••••••••••••"}
                      </span>
                    </div>

                    {/* Subscribed Event Chips */}
                    <div className="space-y-1 pt-1">
                      <div className="text-[11px] font-medium text-muted-foreground">
                        Subscribed Events:
                      </div>
                      <div className="flex flex-wrap gap-1.5">
                        {channel.subscribedEvents && channel.subscribedEvents.length > 0 ? (
                          channel.subscribedEvents.map((evt) => {
                            const conf = EVENT_CONFIGS.find((e) => e.type === evt);
                            return (
                              <Badge
                                key={evt}
                                variant={conf?.badgeVariant || "outline"}
                                className="text-[10px] font-normal"
                              >
                                {conf?.label || evt}
                              </Badge>
                            );
                          })
                        ) : (
                          <span className="text-[11px] text-muted-foreground italic">
                            No events subscribed
                          </span>
                        )}
                      </div>
                    </div>

                    {/* Last Dispatched / Activity Info */}
                    {channel.lastDispatchedAt && (
                      <div className="text-[11px] text-muted-foreground pt-1 flex items-center gap-2">
                        <span>
                          Last dispatched: {new Date(channel.lastDispatchedAt).toLocaleString()}
                        </span>
                        {channel.lastDispatchStatus && (
                          <Badge
                            variant={channel.lastDispatchStatus === "Success" ? "success" : "destructive"}
                            className="text-[9px] px-1.5 py-0"
                          >
                            {channel.lastDispatchStatus}
                          </Badge>
                        )}
                      </div>
                    )}
                  </div>

                  {/* Right Column: Switch & Actions */}
                  <div className="flex flex-row md:flex-col items-end justify-between md:justify-start gap-3 shrink-0 pt-2 md:pt-0 border-t md:border-t-0 border-border/50">
                    {/* Enabled Switch */}
                    <div className="flex items-center gap-2">
                      <span className="text-xs text-muted-foreground font-medium">
                        {channel.isEnabled ? "Active" : "Paused"}
                      </span>
                      <button
                        type="button"
                        role="switch"
                        aria-label={`Toggle channel ${channel.name}`}
                        aria-checked={channel.isEnabled}
                        disabled={isToggling}
                        onClick={() => handleToggleEnabled(channel)}
                        className={`relative inline-flex h-5 w-9 shrink-0 cursor-pointer rounded-full border-2 border-transparent transition-colors duration-200 ease-in-out focus:outline-none focus:ring-2 focus:ring-primary focus:ring-offset-2 disabled:opacity-50 ${
                          channel.isEnabled ? "bg-emerald-600" : "bg-muted-foreground/30"
                        }`}
                      >
                        <span
                          aria-hidden="true"
                          className={`pointer-events-none inline-block h-4 w-4 transform rounded-full bg-white shadow-sm ring-0 transition duration-200 ease-in-out ${
                            channel.isEnabled ? "translate-x-4" : "translate-x-0"
                          }`}
                        />
                      </button>
                    </div>

                    {/* Action Buttons: Test Ping, Edit, Delete */}
                    <div className="flex items-center gap-1.5">
                      <Button
                        variant="outline"
                        size="sm"
                        disabled={isTesting || !channel.isEnabled}
                        onClick={() => handleTestPing(channel)}
                        className="h-8 px-2.5 text-xs gap-1.5"
                        title={
                          channel.isEnabled
                            ? "Send immediate test ping payload to this channel"
                            : "Channel must be active to test"
                        }
                      >
                        {isTesting ? (
                          <RefreshCw className="h-3.5 w-3.5 animate-spin" />
                        ) : (
                          <Send className="h-3.5 w-3.5 text-primary" />
                        )}
                        <span>{isTesting ? "Testing..." : "Test Ping"}</span>
                      </Button>

                      <Button
                        variant="ghost"
                        size="sm"
                        onClick={() => handleOpenEditDialog(channel)}
                        className="h-8 w-8 p-0 text-muted-foreground hover:text-foreground"
                        aria-label="Edit notification channel"
                        title="Edit channel settings"
                      >
                        <Pencil className="h-3.5 w-3.5" />
                      </Button>

                      <Button
                        variant="ghost"
                        size="sm"
                        disabled={isDeleting}
                        onClick={() => handleDelete(channel)}
                        className="h-8 w-8 p-0 text-destructive/80 hover:text-destructive hover:bg-destructive/10"
                        aria-label="Delete notification channel"
                        title="Delete channel"
                      >
                        <Trash2 className="h-3.5 w-3.5" />
                      </Button>
                    </div>
                  </div>
                </div>
              </div>
            );
          })}
        </div>
      )}

      {/* Add / Edit Channel Modal Dialog */}
      <Dialog open={isDialogOpen} onOpenChange={setIsDialogOpen}>
        <DialogContent onClose={() => setIsDialogOpen(false)} className="max-w-xl">
          <form onSubmit={handleDialogSubmit} className="space-y-4">
            <DialogHeader>
              <div className="flex items-center gap-2">
                <div className="p-2 rounded-lg bg-primary/10 text-primary">
                  {getChannelTypeIcon(formChannelType)}
                </div>
                <div>
                  <DialogTitle>
                    {dialogMode === "add" ? "Add Notification Channel" : "Edit Notification Channel"}
                  </DialogTitle>
                  <DialogDescription className="text-xs">
                    {dialogMode === "add"
                      ? "Register an outbound chat or webhook endpoint for real-time task lifecycle dispatches."
                      : "Modify channel name, webhook URL, or subscribable lifecycle events."}
                  </DialogDescription>
                </div>
              </div>
            </DialogHeader>

            {formError && (
              <div className="p-3 rounded-lg bg-destructive/10 border border-destructive/20 text-destructive text-xs flex items-center gap-2">
                <AlertCircle className="h-4 w-4 shrink-0" />
                <span>{formError}</span>
              </div>
            )}

            <div className="space-y-4 py-1">
              {/* Channel Type Selector */}
              <div>
                <label
                  htmlFor="channel-type-select"
                  className="block text-xs font-semibold text-foreground mb-1.5"
                >
                  Channel Type
                </label>
                <Select
                  id="channel-type-select"
                  value={formChannelType}
                  onChange={(e) => setFormChannelType(e.target.value as NotificationChannelType)}
                  disabled={dialogMode === "edit"} // Keep type immutable on edit to prevent payload divergence
                  className="bg-card text-xs"
                >
                  <option value="Slack">Slack (Block Kit with action button)</option>
                  <option value="Discord">Discord (Color-coded Embeds)</option>
                  <option value="GenericWebhook">Generic Webhook (JSON payload with HMAC signature)</option>
                </Select>
                {dialogMode === "edit" && (
                  <p className="text-[11px] text-muted-foreground mt-1">
                    Channel type cannot be changed after creation.
                  </p>
                )}
              </div>

              {/* Channel Name */}
              <div>
                <label
                  htmlFor="channel-name-input"
                  className="block text-xs font-semibold text-foreground mb-1.5"
                >
                  Channel Name <span className="text-destructive">*</span>
                </label>
                <Input
                  id="channel-name-input"
                  type="text"
                  placeholder={
                    formChannelType === "Slack"
                      ? "e.g. #dev-alerts"
                      : formChannelType === "Discord"
                      ? "e.g. #municlaw-builds"
                      : "e.g. Internal CI Webhook"
                  }
                  value={formName}
                  onChange={(e) => setFormName(e.target.value)}
                  className="text-xs"
                  required
                />
              </div>

              {/* Webhook URL Input */}
              <div>
                <label
                  htmlFor="channel-url-input"
                  className="block text-xs font-semibold text-foreground mb-1.5"
                >
                  Webhook URL{" "}
                  {dialogMode === "add" ? (
                    <span className="text-destructive">*</span>
                  ) : (
                    <span className="text-muted-foreground font-normal">(leave blank to keep current)</span>
                  )}
                </label>
                <Input
                  id="channel-url-input"
                  type="url"
                  placeholder={
                    dialogMode === "edit"
                      ? "••••••••••••••••••••••••••••••••"
                      : formChannelType === "Slack"
                      ? "https://hooks.slack.com/services/T00/B00/XXXX"
                      : formChannelType === "Discord"
                      ? "https://discord.com/api/webhooks/..."
                      : "https://api.yourdomain.com/webhooks/municlaw"
                  }
                  value={formWebhookUrl}
                  onChange={(e) => setFormWebhookUrl(e.target.value)}
                  className="font-mono text-xs"
                />
                <p className="text-[11px] text-muted-foreground mt-1">
                  {dialogMode === "edit"
                    ? "Webhook URLs are write-only. Leave this field empty to preserve the existing URL, or enter a new URL to replace it."
                    : "Encrypted and write-only upon creation. Masked in all console views."}
                </p>
              </div>

              {/* Subscribed Events Checkboxes */}
              <div className="space-y-2 pt-2 border-t border-border">
                <div className="flex items-center justify-between">
                  <span className="text-xs font-semibold text-foreground">
                    Subscribed Lifecycle Events <span className="text-destructive">*</span>
                  </span>
                  <div className="flex items-center gap-2 text-[11px]">
                    <button
                      type="button"
                      onClick={handleSelectAllEvents}
                      className="text-primary hover:underline cursor-pointer"
                    >
                      Select all
                    </button>
                    <span className="text-muted-foreground">•</span>
                    <button
                      type="button"
                      onClick={handleDeselectAllEvents}
                      className="text-muted-foreground hover:underline cursor-pointer"
                    >
                      Deselect all
                    </button>
                  </div>
                </div>

                <div className="grid grid-cols-1 sm:grid-cols-2 gap-2 pt-1">
                  {EVENT_CONFIGS.map((evt) => {
                    const isChecked = formSubscribedEvents.includes(evt.type);
                    return (
                      <label
                        key={evt.type}
                        className={`flex items-start gap-2.5 p-2.5 rounded-lg border cursor-pointer transition-colors ${
                          isChecked
                            ? "border-primary/40 bg-primary/5"
                            : "border-border hover:bg-muted/30"
                        }`}
                      >
                        <input
                          type="checkbox"
                          checked={isChecked}
                          onChange={() => handleToggleEventSubscription(evt.type)}
                          className="mt-0.5 rounded border-input text-primary focus:ring-primary h-4 w-4"
                        />
                        <div className="space-y-0.5">
                          <div className="text-xs font-semibold text-foreground flex items-center gap-1.5">
                            <span>{evt.label}</span>
                          </div>
                          <p className="text-[10px] text-muted-foreground leading-tight">
                            {evt.description}
                          </p>
                        </div>
                      </label>
                    );
                  })}
                </div>
              </div>

              {/* Generic Webhook Signing Secret Generator */}
              {formChannelType === "GenericWebhook" && (
                <div className="space-y-2 pt-2 border-t border-border">
                  <div className="flex items-center justify-between">
                    <label
                      htmlFor="channel-secret-input"
                      className="text-xs font-semibold text-foreground flex items-center gap-1.5"
                    >
                      <KeyRound className="h-3.5 w-3.5 text-emerald-500" />
                      <span>HMAC-SHA256 Signing Secret</span>
                    </label>
                    <button
                      type="button"
                      onClick={handleGenerateSecret}
                      className="text-[11px] font-semibold text-emerald-600 hover:text-emerald-700 dark:text-emerald-400 hover:underline cursor-pointer"
                    >
                      Generate Secret
                    </button>
                  </div>

                  <div className="flex gap-2">
                    <Input
                      id="channel-secret-input"
                      type="text"
                      placeholder={
                        dialogMode === "edit"
                          ? "Leave blank to keep existing secret"
                          : "Enter or generate high-entropy secret"
                      }
                      value={formSigningSecret}
                      onChange={(e) => setFormSigningSecret(e.target.value)}
                      className="font-mono text-xs flex-1"
                    />
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      onClick={handleGenerateSecret}
                      className="shrink-0 text-xs gap-1"
                    >
                      <KeyRound className="h-3.5 w-3.5" />
                      <span>Generate</span>
                    </Button>
                  </div>
                  <p className="text-[11px] text-muted-foreground">
                    Receivers authenticate payloads by verifying the{" "}
                    <code className="text-[10px] bg-muted px-1 py-0.5 rounded font-mono">
                      X-MuniClaw-Signature
                    </code>{" "}
                    header computed as HMAC-SHA256(secret, timestamp + &quot;.&quot; + body).
                  </p>
                </div>
              )}

              {/* Channel Enabled Checkbox */}
              <div className="pt-2 border-t border-border">
                <label className="flex items-center gap-2 text-xs font-semibold text-foreground cursor-pointer">
                  <input
                    type="checkbox"
                    checked={formIsEnabled}
                    onChange={(e) => setFormIsEnabled(e.target.checked)}
                    className="rounded border-input text-primary focus:ring-primary h-4 w-4"
                  />
                  <span>Enable channel immediately for event dispatch</span>
                </label>
              </div>
            </div>

            <DialogFooter>
              <Button
                type="button"
                variant="outline"
                onClick={() => setIsDialogOpen(false)}
                disabled={isSubmitting}
                className="text-xs"
              >
                Cancel
              </Button>
              <Button type="submit" disabled={isSubmitting} className="text-xs gap-1.5">
                {isSubmitting && <RefreshCw className="h-3.5 w-3.5 animate-spin" />}
                <span>
                  {dialogMode === "add"
                    ? isSubmitting
                      ? "Creating Channel..."
                      : "Create Channel"
                    : isSubmitting
                    ? "Saving Changes..."
                    : "Save Changes"}
                </span>
              </Button>
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>
    </div>
  );
}
