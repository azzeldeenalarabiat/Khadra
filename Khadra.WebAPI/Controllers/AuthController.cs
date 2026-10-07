using System.ComponentModel.DataAnnotations;
using Khadra.Application.Common;
using Khadra.Application.IdentityAccess.AcceptInvitation;
using Khadra.Application.IdentityAccess.ChangePassword;
using Khadra.Application.IdentityAccess.Dtos;
using Khadra.Application.IdentityAccess.ForgotPassword;
using Khadra.Application.IdentityAccess.GetCurrentUser;
using Khadra.Application.IdentityAccess.Login;
using Khadra.Application.IdentityAccess.Logout;
using Khadra.Application.IdentityAccess.PushDevices;
using Khadra.Application.IdentityAccess.RefreshTokens;
using Khadra.Application.IdentityAccess.RegisterCustomer;
using Khadra.Application.IdentityAccess.RegisterDealerOwner;
using Khadra.Application.IdentityAccess.ResendVerification;
using Khadra.Application.IdentityAccess.ResetPassword;
using Khadra.Application.IdentityAccess.VerifyEmail;
using Khadra.Application.Legal;
using Khadra.WebAPI.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Khadra.WebAPI.Controllers;

[Route("api/v1/auth")]
public sealed class AuthController(ICurrentActor currentActor) : ApiControllerBase
{
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [HttpPost("register")]
    [ProducesResponseType<RegisteredUserDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await Mediator.Send(
            new RegisterCustomerCommand(
                request.Email, request.Password, request.FullName, request.Phone,
                request.DateOfBirth, request.IsForeignNational,
                new ConsentInput(request.AcceptedLegalVersions, request.LegalLanguage), Client),
            cancellationToken);
        return FromResult(result, created => CreatedAtAction(nameof(Me), null, created));
    }

    /// <summary>
    /// Step one of the dealer application (spec 3.1): the owner gets an account.
    ///
    /// The account is a DealerOwner from the start; it is the DEALER that begins PENDING_REVIEW.
    /// Nothing dealer-specific is permitted until that application is approved.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [HttpPost("register-dealer-owner")]
    [ProducesResponseType<RegisteredUserDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> RegisterDealerOwner(
        RegisterDealerOwnerRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await Mediator.Send(
            new RegisterDealerOwnerCommand(
                request.Email, request.Password, request.FullName, request.Phone,
                new ConsentInput(request.AcceptedLegalVersions, request.LegalLanguage)),
            cancellationToken);
        return FromResult(result, created => CreatedAtAction(nameof(Me), null, created));
    }

    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Login)]
    [HttpPost("login")]
    [ProducesResponseType<AuthTokensDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await Mediator.Send(new LoginCommand(request.Email, request.Password, Client), cancellationToken);
        return FromResult(result);
    }

    // Anonymous: possession of a live refresh token is the credential.
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Refresh)]
    [HttpPost("refresh")]
    [ProducesResponseType<AuthTokensDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> Refresh(RefreshRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await Mediator.Send(new RefreshTokensCommand(request.RefreshToken, Client), cancellationToken);
        return FromResult(result);
    }

    [Authorize]
    [AllowWhileConsentPending]
    [HttpPost("logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult> Logout(LogoutRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await Mediator.Send(
            new LogoutCommand(currentActor.UserId!.Value, request.RefreshToken, request.AllDevices),
            cancellationToken);
        return FromResult(result);
    }

    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [HttpPost("verify-email")]
    [ProducesResponseType<MessageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> VerifyEmail(VerifyEmailRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await Mediator.Send(new VerifyEmailCommand(request.Token), cancellationToken);
        return FromResult(result, () => Ok(new MessageResponse("Your email address is verified. You can sign in now.")));
    }

    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [HttpPost("resend-verification")]
    [ProducesResponseType<MessageResponse>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult> ResendVerification(EmailRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await Mediator.Send(new ResendVerificationCommand(request.Email), cancellationToken);
        return FromResult(result, () => Accepted(new MessageResponse("If an unverified account exists for this email, a new verification link has been sent.")));
    }

    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [HttpPost("forgot-password")]
    [ProducesResponseType<MessageResponse>(StatusCodes.Status202Accepted)]
    public async Task<ActionResult> ForgotPassword(EmailRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await Mediator.Send(new ForgotPasswordCommand(request.Email), cancellationToken);
        return FromResult(result, () => Accepted(new MessageResponse("If an account exists for this email, a password reset link has been sent.")));
    }

    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [HttpPost("reset-password")]
    [ProducesResponseType<MessageResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> ResetPassword(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await Mediator.Send(new ResetPasswordCommand(request.Token, request.NewPassword), cancellationToken);
        return FromResult(result, () => Ok(new MessageResponse("Your password has been reset. Sign in with your new password.")));
    }

    /// <summary>
    /// An invited employee takes up their account (spec 4.2): the emailed link proves the mailbox and
    /// the password they choose here is their first. Anonymous and rate limited like every other
    /// token-bearing endpoint.
    /// </summary>
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [HttpPost("accept-invitation")]
    [ProducesResponseType<InvitationAcceptedResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> AcceptInvitation(AcceptInvitationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await Mediator.Send(
            new AcceptInvitationCommand(
                request.Token, request.Password, new ConsentInput(request.AcceptedLegalVersions, request.LegalLanguage)),
            cancellationToken);
        return FromResult(result, recorded => Ok(new InvitationAcceptedResponse(
            "Your account is ready. Sign in with the password you chose.", recorded)));
    }

    [Authorize]
    [EnableRateLimiting(RateLimitPolicies.Auth)]
    [HttpPost("change-password")]
    [ProducesResponseType<AuthTokensDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await Mediator.Send(
            new ChangePasswordCommand(currentActor.UserId!.Value, request.CurrentPassword, request.NewPassword, Client),
            cancellationToken);
        return FromResult(result);
    }

    [Authorize]
    [AllowWhileConsentPending]
    [HttpGet("me")]
    [ProducesResponseType<CurrentUserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult> Me(CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(new GetCurrentUserQuery(currentActor.UserId!.Value), cancellationToken);
        return FromResult(result);
    }

    /// <summary>
    /// The language this person reads Khadra in: "ar" or "en". Emails and reminders sent while they
    /// are away from the app follow it; until it is set they arrive in both languages.
    /// </summary>
    [Authorize]
    [AllowWhileConsentPending]
    [HttpPut("me/language")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> SetLanguage(SetLanguageRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await Mediator.Send(new SetMyLanguageCommand(request.Language), cancellationToken);
        return FromResult(result);
    }

    /// <summary>
    /// This person's consents to the legal texts (Wave 4, W4-8): every acceptance on their record, newest first, and the
    /// texts in force they have still to accept. Reachable while the consent gate is closed: it is what the prompt reads.
    /// </summary>
    [Authorize]
    [AllowWhileConsentPending]
    [HttpGet("me/legal-consents")]
    [ProducesResponseType<MyLegalConsentsDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult> MyLegalConsents(CancellationToken cancellationToken)
    {
        var result = await Mediator.Send(
            new GetMyLegalConsentsQuery(currentActor.UserId!.Value, currentActor.Role!), cancellationToken);
        return FromResult(result);
    }

    /// <summary>
    /// The consent prompt's answer: this person accepts the texts in force they were shown (Wave 4, W4-8). A text they
    /// have already accepted writes nothing; a text not in force is refused with 409 <c>legal.version_not_current</c>.
    /// Answers with the record as it now stands.
    /// </summary>
    [Authorize]
    [AllowWhileConsentPending]
    [HttpPost("me/legal-consents")]
    [ProducesResponseType<MyLegalConsentsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> AcceptLegalTexts(AcceptLegalTextsRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await Mediator.Send(
            new AcceptLegalTextsCommand(
                currentActor.UserId!.Value, currentActor.Role!, request.VersionIds, request.Language, Client),
            cancellationToken);
        return FromResult(result);
    }
}

/// <param name="VersionIds">The published versions in force the person was shown and accepts.</param>
/// <param name="Language">The language they read them in: <c>ar</c> or <c>en</c>.</param>
public sealed record AcceptLegalTextsRequest(
    [param: Required] IReadOnlyList<Guid> VersionIds,
    [param: Required, StringLength(2)] string Language);

public sealed record SetLanguageRequest([param: Required, StringLength(2)] string Language);

public sealed record RegisterRequest(
    [param: Required, StringLength(256)] string Email,
    [param: Required, StringLength(72)] string Password,
    [param: Required, StringLength(150)] string FullName,
    [param: Required, StringLength(32)] string Phone,
    // Spec 5.1: the minimum-age check. Not [Required] here on purpose -- whether it is needed depends
    // on a configured business rule, and RenterAgePolicy is the one place that decides.
    DateOnly? DateOfBirth = null,
    // Spec 5.1: a foreign renter files a passport rather than a national ID.
    bool IsForeignNational = false,
    // Wave 4, W4-8: the legal texts in force the person accepted, and the language they read them in. Required of the
    // website while a text is in force; the customer app is spared until a build asks (1.4.0).
    IReadOnlyList<Guid>? AcceptedLegalVersions = null,
    [param: StringLength(2)] string? LegalLanguage = null);

// No date of birth: spec 5.1's minimum age is a rule about renters, and this registers the person
// who owns the rental office. Their identity is proved by the document an admin reviews (spec 3.1).
public sealed record RegisterDealerOwnerRequest(
    [param: Required, StringLength(256)] string Email,
    [param: Required, StringLength(72)] string Password,
    [param: Required, StringLength(150)] string FullName,
    [param: Required, StringLength(32)] string Phone,
    // Wave 4, W4-8: required while a legal text is in force.
    IReadOnlyList<Guid>? AcceptedLegalVersions = null,
    [param: StringLength(2)] string? LegalLanguage = null);

public sealed record LoginRequest(
    [param: Required, StringLength(256)] string Email,
    [param: Required, StringLength(72)] string Password);

public sealed record RefreshRequest([param: Required, StringLength(512)] string RefreshToken);

public sealed record LogoutRequest([param: Required, StringLength(512)] string RefreshToken, bool AllDevices = false);

public sealed record VerifyEmailRequest([param: Required, StringLength(512)] string Token);

public sealed record EmailRequest([param: Required, StringLength(256)] string Email);

public sealed record ResetPasswordRequest(
    [param: Required, StringLength(512)] string Token,
    [param: Required, StringLength(72)] string NewPassword);

public sealed record AcceptInvitationRequest(
    [param: Required, StringLength(512)] string Token,
    [param: Required, StringLength(72)] string Password,
    // Wave 4, W4-8: required of a staff invitation while a legal text is in force; ignored for an administrator's.
    IReadOnlyList<Guid>? AcceptedLegalVersions = null,
    [param: StringLength(2)] string? LegalLanguage = null);

public sealed record ChangePasswordRequest(
    [param: Required, StringLength(72)] string CurrentPassword,
    [param: Required, StringLength(72)] string NewPassword);

public sealed record MessageResponse(string Message);

/// <param name="ConsentsRecorded">How many legal texts in force the invitation recorded as accepted (Wave 4, W4-8).</param>
public sealed record InvitationAcceptedResponse(string Message, int ConsentsRecorded);
