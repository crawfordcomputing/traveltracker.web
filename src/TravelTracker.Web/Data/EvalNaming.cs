namespace TravelTracker.Web.Data;

// Single source of truth for how an eval batch's rows are named and tagged, shared
// by EvalSeeder (writes) and EvalTeardown (matches for deletion). Keeping the
// conventions here means the two can never drift apart.
//
// Users and trips are the authoritative markers: both carry EvalBatchId (a real
// column, filtered-indexed). Reference data (departments, cost centers, project
// codes) has no such column, so it is instead namespaced with a batch-scoped
// prefix that teardown removes by prefix match. All names/codes embed the batch id
// so two concurrent batches never collide and a teardown only ever touches its own.
public static class EvalNaming
{
    // Guard: batch ids flow into 40-char reference codes and into email local
    // parts, so keep them short and filesystem/URL-safe.
    public const int MaxBatchIdLength = 20;

    public static void ValidateBatchId(string batchId)
    {
        if (string.IsNullOrWhiteSpace(batchId))
            throw new ArgumentException("Eval batch id must be a non-empty value.", nameof(batchId));
        if (batchId.Length > MaxBatchIdLength)
            throw new ArgumentException(
                $"Eval batch id must be {MaxBatchIdLength} characters or fewer.", nameof(batchId));
        foreach (var c in batchId)
            if (!(char.IsLetterOrDigit(c) || c is '-' or '_'))
                throw new ArgumentException(
                    "Eval batch id may contain only letters, digits, '-' and '_'.", nameof(batchId));
    }

    // e.g. ("manager", "run1", "eval.traveltracker.test") -> "manager.run1@eval.traveltracker.test"
    public static string Email(string local, string batchId, string domain)
        => $"{local}.{batchId}@{domain}";

    // Department names: "EVAL-run1 Engineering".
    public static string DeptName(string name, string batchId) => DeptPrefix(batchId) + name;
    public static string DeptPrefix(string batchId) => $"EVAL-{batchId} ";

    // Reference codes (cost center / project code), <= 40 chars: "EV-run1-ENG-100".
    public static string Code(string code, string batchId) => CodePrefix(batchId) + code;
    public static string CodePrefix(string batchId) => $"EV-{batchId}-";
}
