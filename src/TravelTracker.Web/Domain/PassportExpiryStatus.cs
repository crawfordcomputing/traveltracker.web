namespace TravelTracker.Web.Domain;

public enum PassportValidity
{
    Unknown,        // no expiry on file
    Ok,             // valid beyond the threshold
    ExpiringSoon,   // valid, but inside the threshold window
    Expired,        // already past expiry
}

// Pure duty-of-care rule for passport validity. Many destinations require a passport
// valid for six months beyond the travel dates, so the default threshold is six
// months: anything expiring within that window is flagged while the traveler still
// has time to renew. Kept free of EF so it can be unit-tested and reused by any view.
public static class PassportExpiryStatus
{
    public static PassportValidity Evaluate(DateOnly? expiry, DateOnly asOf, int monthsThreshold = 6)
    {
        if (expiry is null) return PassportValidity.Unknown;
        if (expiry.Value < asOf) return PassportValidity.Expired;
        if (expiry.Value <= asOf.AddMonths(monthsThreshold)) return PassportValidity.ExpiringSoon;
        return PassportValidity.Ok;
    }
}
