using Khadra.Infrastructure.Configuration;
using Khadra.Infrastructure.Scheduling;
using Khadra.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Khadra.Tests.Infrastructure;

/// <summary>
/// The receipt email service on a server whose financial-document email is switched off (owner, 2026-09-29): Production
/// on Brevo, until its single-send idempotency is verified (pre-launch item 202). The service does not run — it claims
/// nothing and sends nothing — and says why at boot, while the rest of the API carries on.
/// </summary>
public sealed class FinancialDocumentEmailServiceTests
{
    [Fact]
    public async Task Switched_off_it_never_starts_a_pass_and_says_why_at_boot()
    {
        var scopes = Substitute.For<IServiceScopeFactory>();
        var log = new RecordingLogger<FinancialDocumentEmailService>();
        var settings = new TestDocumentEmailSettings { DeliveryDisabledReason = FinancialDocumentEmailSettings.BrevoInProduction };
        using var service = new FinancialDocumentEmailService(
            scopes,
            Options.Create(new SchedulingOptions()),
            Options.Create(new FinancialDocumentEmailOptions()),
            settings,
            log);

        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!;

        // One warning, at boot, with the whole reason; and not a single scope — no claim, no send.
        Assert.True(log.Logged(2715));
        Assert.Contains("FINANCIAL-DOCUMENT EMAILS ARE NOT SENT", log.AllText, StringComparison.Ordinal);
        Assert.Contains("single-send idempotency has not been verified (pre-launch item 202)", log.AllText, StringComparison.Ordinal);
        Assert.Contains("their emails wait in the queue", log.AllText, StringComparison.Ordinal);
        scopes.DidNotReceiveWithAnyArgs().CreateScope();
        Assert.True(service.ExecuteTask.IsCompletedSuccessfully);

        await service.StopAsync(CancellationToken.None);
    }
}
