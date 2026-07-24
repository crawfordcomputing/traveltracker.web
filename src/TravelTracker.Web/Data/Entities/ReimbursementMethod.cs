namespace TravelTracker.Web.Data.Entities;

// How a traveler prefers to be reimbursed. A per-user default that pre-fills expense
// reports in M3, so it's stored on the profile now. Persisted as an int.
public enum ReimbursementMethod
{
    Unspecified = 0,
    DirectDeposit = 1,
    Check = 2,
    Payroll = 3,
    Other = 4,
}
