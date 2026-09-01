using System.ComponentModel.DataAnnotations;

namespace TravelTracker.Web.Data.Entities;

// Outcome of a single outbound email attempt. Deliberately NOT a raw application
// log: deep diagnostics (stack traces, request logs) go to Application Insights.
// This is the admin-facing "did the notification actually send?" trail, surfaced
// at Pages/Admin/Notifications. It never stores the message body, because bodies
// can contain one-time confirmation / password-reset links.
public enum NotificationStatus
{
    Sent = 0,
    Failed = 1
}

public class NotificationLog
{
    public int Id { get; set; }

    // UTC, matching every other timestamp in the model (maps to datetimeoffset).
    public DateTimeOffset SentAt { get; set; } = DateTimeOffset.UtcNow;

    [StringLength(256)]
    public string Recipient { get; set; } = string.Empty;

    [StringLength(256)]
    public string Subject { get; set; } = string.Empty;

    public NotificationStatus Status { get; set; }

    // Exception message on failure; null on success. Message only, never the body.
    [StringLength(2000)]
    public string? Error { get; set; }
}
