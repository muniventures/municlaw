import { getRuntimeConfig } from "@/core/config/runtime";
import type { TaskEventDto, TaskEventType } from "./types";

export type SseConnectionStatus =
  | "idle"
  | "connecting"
  | "connected"
  | "reconnecting"
  | "disconnected"
  | "error";

export interface SseSubscriber {
  onEvent: (event: TaskEventDto) => void;
  onStatusChange?: (status: SseConnectionStatus) => void;
  onError?: (err: Error) => void;
}

const STORAGE_CURSOR_PREFIX = "municlaw_sse_cursor_";

function getStoredCursor(runId: string): number {
  if (typeof sessionStorage === "undefined") return 0;
  const stored = sessionStorage.getItem(`${STORAGE_CURSOR_PREFIX}${runId}`);
  if (!stored) return 0;
  const parsed = parseInt(stored, 10);
  return Number.isNaN(parsed) ? 0 : parsed;
}

function storeCursor(runId: string, seq: number): void {
  if (typeof sessionStorage === "undefined") return;
  sessionStorage.setItem(`${STORAGE_CURSOR_PREFIX}${runId}`, String(seq));
}

class SharedSseManager {
  private activeRunId: string | null = null;
  private abortController: AbortController | null = null;
  private subscribers = new Set<SseSubscriber>();
  private status: SseConnectionStatus = "idle";
  private highestSeq = 0;
  private seenEventIds = new Set<string>();
  private reconnectTimeoutId: number | null = null;
  private retryCount = 0;
  private maxRetries = 10;
  private baseBackoffMs = 1000;

  public subscribe(
    organizationId: string,
    taskId: string,
    runId: string,
    subscriber: SseSubscriber
  ): () => void {
    // If switching to a new run, reset state
    if (this.activeRunId !== runId) {
      this.disconnect();
      this.activeRunId = runId;
      this.highestSeq = getStoredCursor(runId);
      this.seenEventIds.clear();
      this.retryCount = 0;
    }

    this.subscribers.add(subscriber);
    subscriber.onStatusChange?.(this.status);

    // If not connected and no active controller, connect
    if (this.status === "idle" || this.status === "disconnected") {
      this.connect(organizationId, taskId, runId);
    }

    // Return unbind function
    return () => {
      this.subscribers.delete(subscriber);
      if (this.subscribers.size === 0) {
        this.disconnect();
      }
    };
  }

  public getStatus(): SseConnectionStatus {
    return this.status;
  }

  public getHighestSequence(): number {
    return this.highestSeq;
  }

  private setStatus(newStatus: SseConnectionStatus) {
    this.status = newStatus;
    for (const sub of this.subscribers) {
      sub.onStatusChange?.(newStatus);
    }
  }

  private emitEvent(evt: TaskEventDto) {
    // Deduplication check: by ID and monotonic sequence check
    if (this.seenEventIds.has(evt.eventId)) {
      return;
    }
    this.seenEventIds.add(evt.eventId);

    if (evt.sequenceNumber > this.highestSeq) {
      this.highestSeq = evt.sequenceNumber;
      if (this.activeRunId) {
        storeCursor(this.activeRunId, this.highestSeq);
      }
    }

    for (const sub of this.subscribers) {
      try {
        sub.onEvent(evt);
      } catch (err) {
        console.error("Error in SSE event subscriber handler:", err);
      }
    }
  }

  private connect(organizationId: string, taskId: string, runId: string) {
    if (this.abortController) {
      this.abortController.abort();
    }
    this.abortController = new AbortController();
    const signal = this.abortController.signal;

    this.setStatus(this.retryCount > 0 ? "reconnecting" : "connecting");

    const config = getRuntimeConfig();
    const baseUrl = config.apiBaseUrl.replace(/\/$/, "");
    const url = `${baseUrl}/api/v1/organizations/${organizationId}/tasks/${taskId}/runs/${runId}/events?sinceSequence=${this.highestSeq}`;

    const lastEventId = `${runId}:${this.highestSeq}`;

    fetch(url, {
      signal,
      headers: {
        Accept: "text/event-stream",
        "Last-Event-ID": lastEventId,
      },
    })
      .then(async (response) => {
        if (!response.ok) {
          throw new Error(`SSE stream returned status ${response.status}: ${response.statusText}`);
        }
        if (!response.body) {
          throw new Error("No readable body in SSE response");
        }

        this.setStatus("connected");
        this.retryCount = 0;

        const reader = response.body.getReader();
        const decoder = new TextDecoder();
        let buffer = "";

        while (true) {
          const { done, value } = await reader.read();
          if (done) break;

          buffer += decoder.decode(value, { stream: true });
          const parts = buffer.split("\n\n");
          buffer = parts.pop() || "";

          for (const block of parts) {
            if (!block.trim()) continue;
            this.processSseBlock(block, runId, taskId);
          }
        }

        // Clean finish
        this.setStatus("disconnected");
      })
      .catch((err: Error) => {
        if (signal.aborted) {
          return;
        }
        console.warn("SSE connection error:", err.message);
        this.setStatus("error");
        for (const sub of this.subscribers) {
          sub.onError?.(err);
        }
        this.scheduleReconnect(organizationId, taskId, runId);
      });
  }

  private processSseBlock(block: string, defaultRunId: string, defaultTaskId: string) {
    let eventType: TaskEventType = "TextDelta";
    let payload = "";
    let cursor = "";

    const lines = block.split("\n");
    for (const line of lines) {
      if (line.startsWith("event: ")) {
        eventType = line.slice(7).trim() as TaskEventType;
      } else if (line.startsWith("data: ")) {
        payload = line.slice(6);
      } else if (line.startsWith("id: ")) {
        cursor = line.slice(4).trim();
      }
    }

    if (!payload && !cursor) return;

    let seq = this.highestSeq + 1;
    let runId = defaultRunId;

    if (cursor) {
      const parts = cursor.split(":");
      if (parts.length === 2) {
        runId = parts[0];
        const parsedSeq = parseInt(parts[1], 10);
        if (!Number.isNaN(parsedSeq)) seq = parsedSeq;
      }
    }

    const eventDto: TaskEventDto = {
      eventId: `${runId}-${seq}`,
      taskId: defaultTaskId,
      runId,
      sequenceNumber: seq,
      cursor: cursor || `${runId}:${seq}`,
      eventType,
      timestamp: new Date().toISOString(),
      payloadJson: payload,
      isTruncated: false,
    };

    this.emitEvent(eventDto);
  }

  private scheduleReconnect(organizationId: string, taskId: string, runId: string) {
    if (this.reconnectTimeoutId !== null) {
      window.clearTimeout(this.reconnectTimeoutId);
    }
    if (this.retryCount >= this.maxRetries) {
      this.setStatus("error");
      return;
    }

    const delay = Math.min(this.baseBackoffMs * Math.pow(1.5, this.retryCount), 30000);
    this.retryCount++;

    this.reconnectTimeoutId = window.setTimeout(() => {
      this.reconnectTimeoutId = null;
      if (this.subscribers.size > 0 && this.activeRunId === runId) {
        this.connect(organizationId, taskId, runId);
      }
    }, delay);
  }

  public disconnect() {
    if (this.reconnectTimeoutId !== null) {
      window.clearTimeout(this.reconnectTimeoutId);
      this.reconnectTimeoutId = null;
    }
    if (this.abortController) {
      this.abortController.abort();
      this.abortController = null;
    }
    this.setStatus("disconnected");
  }
}

// Single shared SSE instance per frontend application session
export const sseManager = new SharedSseManager();
