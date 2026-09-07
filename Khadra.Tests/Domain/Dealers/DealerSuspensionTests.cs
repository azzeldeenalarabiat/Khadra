using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Tests.Support;

namespace Khadra.Tests.Domain.Dealers;

/// <summary>
/// Suspension is one of the few admin decisions that writes to <c>audit_entries</c>, which a database
/// trigger and a SaveChanges guard both keep append-only. A transition that quietly does nothing but
/// still reports success therefore leaves a permanent record of something that never happened, and
/// nobody can take it back out. Both directions now refuse instead.
/// </summary>
public sealed class DealerSuspensionTests
{
    private static readonly DateTimeOffset Now = Build.Now;
    private static readonly Id Admin = Id.New();

    private static Dealer Approved()
    {
        var dealer = Build.Dealer();
        foreach (var type in Enumeration.GetAll<DealerDocumentType>())
            dealer.AttachDocument(type, "docs/" + type.Name + ".jpg", Now);
        dealer.Approve(Admin, Now);
        return dealer;
    }

    [Fact]
    public void Suspending_twice_is_refused_rather_than_recorded_again()
    {
        var dealer = Approved();
        Assert.True(dealer.Suspend(Admin, "Licence under review.", Now).IsSuccess);

        var second = dealer.Suspend(Admin, "A different reason entirely.", Now);

        Assert.True(second.IsFailure);
        Assert.Equal("dealer.already_suspended", second.Error.Code);
        // The reason on the record is still the one that was actually applied.
        Assert.Equal("Licence under review.", dealer.SuspensionReason);
    }

    [Fact]
    public void Reactivating_a_dealer_that_was_never_suspended_is_refused()
    {
        var dealer = Approved();

        var outcome = dealer.Reactivate();

        Assert.True(outcome.IsFailure);
        Assert.Equal("dealer.not_suspended", outcome.Error.Code);
    }

    [Fact]
    public void A_real_suspension_and_a_real_reactivation_both_succeed()
    {
        var dealer = Approved();

        Assert.True(dealer.Suspend(Admin, "Papers expired.", Now).IsSuccess);
        Assert.True(dealer.IsSuspended);

        Assert.True(dealer.Reactivate().IsSuccess);
        Assert.False(dealer.IsSuspended);
        Assert.Null(dealer.SuspensionReason);
    }
}
