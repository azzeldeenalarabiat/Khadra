using Khadra.Application.Common;
using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Application.Dealers;
using Khadra.Domain.IdentityAccess;
using Microsoft.AspNetCore.Authorization;

namespace Khadra.WebAPI.Security;

/// <summary>
/// Requires that the caller's dealer may actually trade (spec 3.1).
///
/// Expressed as an authorization requirement so that any dealer-only endpoint picks the rule up from
/// an attribute rather than remembering to call it. The rule ITSELF is still the domain's
/// <see cref="Dealer.CanTrade"/>: approved, not suspended, not deleted. This is the pipeline seam,
/// not a second copy of the policy.
/// </summary>
public sealed class ApprovedDealerRequirement : IAuthorizationRequirement;

/// <summary>
/// Carries a domain <see cref="Error"/> out of authorization so the 403 can keep its stable code.
/// Without this a denied request is an empty 403 and the client cannot tell "not a dealer" from
/// "dealer not approved yet" -- two situations with completely different remedies.
/// </summary>
public sealed class DomainAuthorizationFailureReason(AuthorizationHandler<ApprovedDealerRequirement> handler, Error error)
    : AuthorizationFailureReason(handler, error.Message)
{
    public Error Error { get; } = error;
}

internal sealed class ApprovedDealerAuthorizationHandler(DealerMembershipResolver membership, ICurrentActor actor)
    : AuthorizationHandler<ApprovedDealerRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ApprovedDealerRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (actor.UserId is not { } userId)
            return;

        // Someone who is not dealer staff at all fails on the ROLE, and must be left to fail that way:
        // a policy collects every requirement's failure, so answering "you have not applied yet" here
        // would mask the role failure and tell a customer 404 instead of 403 -- while also implying
        // they could fix it by applying. It also spares a database lookup on every unrelated request.
        if (!context.User.IsInRole(UserRole.DealerOwner.Name) &&
            !context.User.IsInRole(UserRole.DealerEmployee.Name))
        {
            return;
        }

        // An owner or an ACTIVE employee: both act on behalf of the same business, and neither may
        // act while that business is unapproved. The resolver is what turns a deactivated employee
        // away here (spec 4.2: their access ends immediately); a raw repository lookup would not.
        var member = await membership.ResolveAsync(userId);
        if (member.IsFailure)
        {
            context.Fail(new DomainAuthorizationFailureReason(this, member.Error));
            return;
        }

        if (!member.Value.Dealer.CanTrade)
        {
            context.Fail(new DomainAuthorizationFailureReason(this, DealerErrors.NotApproved));
            return;
        }

        context.Succeed(requirement);
    }
}
