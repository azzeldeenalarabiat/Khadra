namespace Khadra.WebAPI;

public sealed class WebApiAssemblyMarker;

public static class SecurityPolicies
{
    public const string Admin = "Admin";
    public const string DealerStaff = "DealerStaff";
    public const string DealerOwner = "DealerOwner";
    public const string Customer = "Customer";
    public const string VerifiedEmail = "VerifiedEmail";

    // Spec 3.1: a dealer owner whose application is still PENDING_REVIEW (or was rejected, or is
    // suspended) may sign in and watch their application, but may not operate the business.
    public const string ApprovedDealer = "ApprovedDealer";

    // The same trading gate, open to employees as well as the owner. Spec 4.2 gives employees exactly
    // one job -- acting on booking requests -- and that job lives behind this policy. Fleet, delivery,
    // profile and staff management stay on ApprovedDealer, which is owner-only.
    public const string ApprovedDealerStaff = "ApprovedDealerStaff";
}

public static class RateLimitPolicies
{
    public const string Auth = "auth";
    public const string Login = "login";
    public const string Refresh = "refresh";

    /// <summary>
    /// Address suggestions, which cost an outbound call to a provider that allows roughly one
    /// request per second for the WHOLE server.
    /// </summary>
    /// <remarks>
    /// Partitioned on the signed-in owner rather than the address: a Jordanian carrier NATs thousands
    /// of subscribers behind one IPv4 address, so an address-keyed bucket would let one busy applicant
    /// spend the budget of everybody on their network. Generous per person -- dragging a pin is a
    /// normal thing to do a few times -- and no queue, because a form waiting is a form the applicant
    /// has already given up on.
    /// </remarks>
    public const string Geocode = "geocode";

    /// <summary>
    /// The anonymous catalogue: browsing, one listing, a quote, a gallery page, and the two lookups
    /// the filter chips need.
    /// </summary>
    /// <remarks>
    /// Deliberately far looser than the auth limits. These are read-only public prices, and mobile
    /// carriers NAT thousands of subscribers behind a single address — a tight per-IP window here
    /// would shut out an entire network rather than one abuser. The global 600/minute still applies
    /// above it, and checklist item 32 tracks the partitioning problem behind a proxy.
    /// </remarks>
    public const string Public = "public";

    /// <summary>
    /// A payment provider delivering its webhooks.
    /// </summary>
    /// <remarks>
    /// Its own bucket rather than <see cref="Public"/>, because the two want opposite things. A
    /// provider that has been unable to reach us retries a BURST when we come back, and every one of
    /// those deliveries is a payment somebody made; throttling them as if they were a scraper would
    /// leave real money unapplied. It is still bounded — the endpoint is anonymous, and a signature
    /// is only checked after the request is admitted — but the ceiling is set for a retry storm from
    /// one provider rather than for a crowd of browsers behind one address.
    /// </remarks>
    public const string Webhook = "webhook";

    /// <summary>
    /// A signed-in person reading private documents they have already been authorised for.
    /// </summary>
    /// <remarks>
    /// Its own bucket rather than <see cref="Auth"/>, which is the wrong shape for a GET that serves
    /// images. <c>CredentialSubject</c> only names a subject for a POST body under
    /// <c>/api/v1/auth</c>, so on a GET that policy silently degrades to ten requests a minute per
    /// ADDRESS — and one gallery's office NAT, or a Jordanian carrier, is one address. A handover
    /// screen opens three or four documents at once, and two colleagues doing that would lock out
    /// the whole network from the one screen the platform built to stop cars being handed to
    /// strangers.
    ///
    /// <b>This is keyed on the address too, and cannot be keyed on the account.</b> The rate limiter
    /// runs BEFORE authentication in the pipeline, so <c>context.User</c> is still the empty
    /// principal when the partitioner asks for a subject claim; a per-account bucket would need
    /// <c>UseRateLimiter</c> moved below <c>UseAuthentication</c>, which would make every
    /// over-the-limit request pay the security-stamp read before being refused. <see cref="Geocode"/>
    /// has the same shape and the same limitation. What this policy actually buys is a ceiling twelve
    /// times higher than <see cref="Auth"/>, which is what a screen that loads several images needs.
    /// </remarks>
    public const string PrivateDocuments = "private-documents";
}
