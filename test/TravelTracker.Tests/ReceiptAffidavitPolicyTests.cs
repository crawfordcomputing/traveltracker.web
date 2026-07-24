using TravelTracker.Web.Domain;
using Xunit;

namespace TravelTracker.Tests;

public class ReceiptAffidavitPolicyTests
{
    [Fact]
    public void Policy_Off_Never_Requires_Anything()
        => Assert.Equal(ReceiptRequirement.NotRequired,
            ReceiptAffidavitPolicy.Decision(hasReceipt: false, baseAmount: 500m, receiptRequired: false, threshold: 25m));

    [Fact]
    public void Receipt_Present_Needs_Nothing()
        => Assert.Equal(ReceiptRequirement.NotRequired,
            ReceiptAffidavitPolicy.Decision(hasReceipt: true, baseAmount: 500m, receiptRequired: true, threshold: 25m));

    [Fact]
    public void Under_Threshold_Without_Receipt_Allows_Affidavit()
        => Assert.Equal(ReceiptRequirement.AffidavitNeeded,
            ReceiptAffidavitPolicy.Decision(hasReceipt: false, baseAmount: 20m, receiptRequired: true, threshold: 25m));

    [Fact]
    public void At_Threshold_Is_Still_Affidavit_Eligible()
        => Assert.Equal(ReceiptRequirement.AffidavitNeeded,
            ReceiptAffidavitPolicy.Decision(hasReceipt: false, baseAmount: 25m, receiptRequired: true, threshold: 25m));

    [Fact]
    public void Over_Threshold_Without_Receipt_Expects_Receipt()
        => Assert.Equal(ReceiptRequirement.ReceiptExpected,
            ReceiptAffidavitPolicy.Decision(hasReceipt: false, baseAmount: 26m, receiptRequired: true, threshold: 25m));

    [Fact]
    public void NeedsJustification_True_When_Required_And_Blank()
        => Assert.True(ReceiptAffidavitPolicy.NeedsJustification(
            hasReceipt: false, baseAmount: 20m, receiptRequired: true, threshold: 25m, affidavit: "  "));

    [Fact]
    public void NeedsJustification_False_When_Affidavit_Supplied()
        => Assert.False(ReceiptAffidavitPolicy.NeedsJustification(
            hasReceipt: false, baseAmount: 20m, receiptRequired: true, threshold: 25m, affidavit: "Lost the receipt; solo lunch at gate."));

    [Fact]
    public void NeedsJustification_False_When_Receipt_Present()
        => Assert.False(ReceiptAffidavitPolicy.NeedsJustification(
            hasReceipt: true, baseAmount: 20m, receiptRequired: true, threshold: 25m, affidavit: null));
}
