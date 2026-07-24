namespace TravelTracker.Web.Domain;

// Pure session-lifetime rule. The application cookie enforces an *idle* timeout via
// sliding expiration (handled by the cookie middleware); this adds the hard *absolute*
// cap, measured from first sign-in, that must survive sliding renewal. Kept pure so the
// boundary condition is unit-testable without spinning up the cookie pipeline.
public static class SessionPolicy
{
    // Auth-property key holding the ISO-8601 UTC instant the session first signed in.
    // Stamped once at sign-in and carried across sliding renewals (which only reset the
    // cookie's IssuedUtc, not the auth properties).
    public const string AbsoluteStartKey = "absolute_start";

    // True once the session has outlived the absolute cap, regardless of activity.
    public static bool IsAbsolutelyExpired(
        DateTimeOffset? startUtc, DateTimeOffset nowUtc, TimeSpan absoluteLifetime)
        => startUtc is { } start && nowUtc - start >= absoluteLifetime;
}
