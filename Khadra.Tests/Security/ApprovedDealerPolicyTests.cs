using System.Security.Claims;
using Khadra.Application.Common;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Dealers.Repositories;
using Khadra.Infrastructure.Security;
using Khadra.Tests.Support;
using Khadra.WebAPI.Security;
using Microsoft.AspNetCore.Authorization;
using NSubstitute;

namespace Khadra.Tests.Security;

// Spec 3.1: a PENDING_REVIEW dealer may sign in and watch their application, but may not operate the
// business. These tests hold that at the authorization layer, where a future fleet or booking
// endpoint will inherit it from the attribute alone.
public sealed class ApprovedDealerPolicyTests
{
    private static readonly Id OwnerId = Id.New();
    private static readonly ApprovedDealerRequirement Requirement = new();

    private static AuthorizationHandlerContext ContextFor(Dealer? dealer, Id? actorId = null, string role = "DealerOwner")
    {
        var dealers = Substitute.For<IDealerRepository>();
        dealers.GetByOwnerUserIdAsync(Arg.Any<Id>(), Arg.Any<CancellationToken>()).Returns(dealer);
        dealers.GetByStaffUserIdAsync(Arg.Any<Id>(), Arg.Any<CancellationToken>()).Returns((Dealer?)null);

        var actor = Substitute.For<ICurrentActor>();
        actor.UserId.Returns(actorId);

        // The role claim matters: the handler deliberately stays silent for a caller who is not
        // dealer staff, so that the role requirement is the one that fails.
        var identity = new ClaimsIdentity(
            [new Claim(KhadraClaimTypes.Role, role)],
            authenticationType: "test",
            nameType: KhadraClaimTypes.Name,
            roleType: KhadraClaimTypes.Role);
        var context = new AuthorizationHandlerContext(
            [Requirement], new ClaimsPrincipal(identity), resource: null);

        new ApprovedDealerAuthorizationHandler(dealers, actor)
            .HandleAsync(context).GetAwaiter().GetResult();

        return context;
    }

    private static Error? FailureError(AuthorizationHandlerContext context) =>
        context.FailureReasons.OfType<DomainAuthorizationFailureReason>().FirstOrDefault()?.Error;

    [Fact]
    public void An_approved_dealer_passes()
    {
        var context = ContextFor(Build.ApprovedDealer(ownerUserId: OwnerId), OwnerId);

        Assert.True(context.HasSucceeded);
        Assert.Null(FailureError(context));
    }

    [Fact]
    public void A_pending_dealer_is_forbidden_with_a_code_that_says_why()
    {
        var context = ContextFor(Build.Dealer(ownerUserId: OwnerId), OwnerId);

        Assert.False(context.HasSucceeded);
        var error = FailureError(context);
        Assert.NotNull(error);
        Assert.Equal("dealer.not_approved", error.Code);
        Assert.Equal(ErrorKind.Forbidden, error.Kind);
    }

    [Fact]
    public void A_suspended_dealer_is_forbidden_even_though_it_was_approved()
    {
        // Verification and suspension are independent flags; CanTrade needs both to be right.
        var dealer = Build.ApprovedDealer(ownerUserId: OwnerId);
        dealer.Suspend(Id.New(), "Policy violation.", Users.Now);

        var context = ContextFor(dealer, OwnerId);

        Assert.False(context.HasSucceeded);
        Assert.Equal("dealer.not_approved", FailureError(context)!.Code);
    }

    [Fact]
    public void An_owner_who_has_not_applied_yet_is_told_that_specifically()
    {
        // Distinct from "not approved": the remedy is to submit an application, not to wait.
        var context = ContextFor(dealer: null, OwnerId);

        Assert.False(context.HasSucceeded);
        Assert.Equal("dealer.not_registered", FailureError(context)!.Code);
    }

    [Fact]
    public void A_caller_who_is_not_dealer_staff_fails_on_the_role_with_no_domain_reason()
    {
        // A customer must get a plain 403 for the wrong role, not a dealer-shaped 404 that suggests
        // applying would help.
        var context = ContextFor(dealer: null, OwnerId, role: "Customer");

        Assert.False(context.HasSucceeded);
        Assert.Null(FailureError(context));
    }

    [Fact]
    public void An_unauthenticated_caller_neither_succeeds_nor_gets_a_domain_reason()
    {
        // Nothing to look up, so the framework's own challenge handles it as a 401.
        var context = ContextFor(Build.ApprovedDealer(ownerUserId: OwnerId), actorId: null);

        Assert.False(context.HasSucceeded);
        Assert.Null(FailureError(context));
    }
}
