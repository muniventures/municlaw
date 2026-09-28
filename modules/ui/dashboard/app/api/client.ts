import { getRuntimeConfig } from "@/core/config/runtime";

export class ApiError extends Error {
  constructor(
    public status: number,
    public code: string,
    message: string,
    public details?: unknown
  ) {
    super(message);
    this.name = "ApiError";
  }
}

export interface RequestOptions extends RequestInit {
  params?: Record<string, string | number | boolean | undefined>;
}

export async function apiClient<T>(
  path: string,
  options: RequestOptions = {}
): Promise<T> {
  const config = getRuntimeConfig();
  const baseUrl = config.apiBaseUrl.replace(/\/$/, "");
  
  let url = `${baseUrl}${path.startsWith("/") ? "" : "/"}${path}`;

  if (options.params) {
    const searchParams = new URLSearchParams();
    for (const [key, value] of Object.entries(options.params)) {
      if (value !== undefined && value !== null) {
        searchParams.append(key, String(value));
      }
    }
    const queryString = searchParams.toString();
    if (queryString) {
      url += (url.includes("?") ? "&" : "?") + queryString;
    }
  }

  const headers = new Headers(options.headers);
  if (!headers.has("Content-Type") && options.body && typeof options.body === "string") {
    headers.set("Content-Type", "application/json");
  }
  if (!headers.has("Accept")) {
    headers.set("Accept", "application/json");
  }

  const response = await fetch(url, {
    ...options,
    headers,
  });

  if (!response.ok) {
    let errorCode = `HTTP_${response.status}`;
    let errorMessage = response.statusText || `Request failed with status ${response.status}`;
    let details: unknown = undefined;

    try {
      const data = await response.json();
      if (data && typeof data === "object") {
        if ("error" in data && typeof data.error === "string") {
          errorCode = data.error;
        }
        if ("message" in data && typeof data.message === "string") {
          errorMessage = data.message;
        }
        details = data;
      }
    } catch {
      // Non-JSON response error
    }

    throw new ApiError(response.status, errorCode, errorMessage, details);
  }

  if (response.status === 204) {
    return null as T;
  }

  return response.json();
}
