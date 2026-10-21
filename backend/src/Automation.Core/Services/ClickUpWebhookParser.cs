using System.Text.Json;

namespace Automation.Core.Services;

public record ClickUpWebhookEvent(string Event, string TaskId, string NewStatus, string EventId);

/// <summary>Reads the body ClickUp sends for native webhooks (taskStatusUpdated etc.).</summary>
public static class ClickUpWebhookParser
{
    public static ClickUpWebhookEvent? Parse(byte[] raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var ev = Str(root, "event");
            var taskId = Str(root, "task_id");
            var webhookId = Str(root, "webhook_id");
            string status = "", historyId = "", date = "";

            if (root.TryGetProperty("history_items", out var items) && items.ValueKind == JsonValueKind.Array && items.GetArrayLength() > 0)
            {
                var first = items[0];
                historyId = Str(first, "id");
                date = Str(first, "date");
                if (first.TryGetProperty("after", out var after))
                    status = after.ValueKind == JsonValueKind.String ? after.GetString() ?? "" : Str(after, "status");
            }

            // Stable idempotency key: ClickUp may redeliver the same history item.
            var eventId = historyId != "" ? "clickup-" + historyId : $"clickup-{webhookId}-{taskId}-{date}";
            return new ClickUpWebhookEvent(ev, taskId, status, eventId);
        }
        catch (JsonException) { return null; }
    }

    private static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v)
            ? (v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString())
            : "";
}
