export type JobStatus = "Queued" | "Running" | "Completed" | "Failed";
export type WriteBackStatus = "Pending" | "Delivered" | "Failed";

export interface ReviewListItem {
  id: string; taskId: string; taskUrl: string; title: string;
  status: JobStatus; writeBackStatus: WriteBackStatus;
  overallResult: string | null; summary: string | null;
  createdAtUtc: string; startedAtUtc: string | null; completedAtUtc: string | null;
  findingCount: number; durationSeconds: number | null;
}
export interface Finding {
  id: string; severity: string; category: string; title: string;
  evidence: string; recommendation: string; confidence: string;
}
export interface DeliveryAttempt {
  id: string; destination: string; attemptedAtUtc: string; statusCode: number;
  errorCode: string | null; attemptNumber: number;
}
export interface ReviewDetail extends Omit<ReviewListItem, "findingCount" | "durationSeconds" | "taskUrl"> {
  taskUrl: string; model: string | null; promptVersion: string | null; error: string | null;
  attempts: number; correlationId: string; limitations: string[];
  findings: Finding[]; deliveryAttempts: DeliveryAttempt[];
}
export interface Metrics {
  windowHours: number; total: number; queued: number; running: number; completed: number; failed: number;
  writeBackFailed: number; medianSeconds: number | null; p95Seconds: number | null;
  outcomes: { result: string; count: number }[]; queueDepth: number; oldestQueuedAgeSeconds: number | null;
}
export interface Page<T> { items: T[]; page: number; pageSize: number; total: number }

async function getJson<T>(url: string): Promise<T> {
  const r = await fetch(url);
  if (!r.ok) throw new Error(`${url} -> ${r.status}`);
  return r.json() as Promise<T>;
}

export const api = {
  list: (status: string, taskId: string) => {
    const q = new URLSearchParams({ pageSize: "25" });
    if (status) q.set("status", status);
    if (taskId.trim()) q.set("taskId", taskId.trim());
    return getJson<Page<ReviewListItem>>(`/api/v1/reviews?${q}`);
  },
  detail: (id: string) => getJson<ReviewDetail>(`/api/v1/reviews/${id}`),
  metrics: () => getJson<Metrics>("/api/v1/metrics/summary"),
  ready: async () => (await fetch("/api/v1/health/ready")).ok,
};
