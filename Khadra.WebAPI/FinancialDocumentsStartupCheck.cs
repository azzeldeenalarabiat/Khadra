using Khadra.Application.FinancialDocuments.ReadModels;
using Khadra.Application.FinancialDocuments.Rendering;
using Khadra.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Khadra.WebAPI;

/// <summary>
/// Says, on every start, whether financial documents can be issued, and how many are on hold (payments
/// Phase 5). The twin of <see cref="PaymentsStartupCheck"/>'s reporting half: a platform that silently
/// issues nothing looks exactly like one with nothing to issue, and the line at boot is what stops that
/// being a surprise. Never fatal — a half-configured identity is refused by options validation instead.
/// </summary>
internal static partial class FinancialDocumentsStartupCheck
{
    public static async Task ReportAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        var issuer = services.GetRequiredService<IFinancialDocumentSettings>().Issuer;
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Khadra.FinancialDocuments");

        if (issuer is null)
            LogNotIssued(logger);
        else if (issuer.IsTestIdentity)
            LogTestIdentity(logger, issuer.LegalName.En);
        else
            LogReady(logger, issuer.LegalName.En);

        // PDFs (payments Phase 6): proved on this host by drawing one throwaway page, so a missing native library
        // or face is said here, once, instead of once per document owed.
        var pdf = services.GetRequiredService<IFinancialDocumentPdfRenderer>().Probe();
        if (pdf.IsReady)
            LogPdfsReady(logger, pdf.Description);
        else
            LogPdfsNotDrawn(logger, pdf.Description);

        try
        {
            using var scope = services.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<KhadraDbContext>();
            var onHold = await context.FinancialDocumentIssuanceHolds.CountAsync(hold => hold.ResolvedAt == null, cancellationToken);
            if (onHold > 0)
                LogOnHold(logger, onHold);
        }
        catch (Exception unreadable) when (DatabaseUnreadable.IsCauseOf(unreadable))
        {
            // The first request that needs the database will say so more usefully than this would. Refused or timed
            // out as well as answered wrongly: EF hands a refused connection over wrapped (pre-launch item 221).
            LogHoldsUnreadable(logger, DatabaseUnreadable.Reason(unreadable));
        }
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "FINANCIAL DOCUMENTS ARE NOT ISSUED. Khadra's legal identity is not configured "
                  + "(FinancialDocuments:Issuer), and no receipt or statement is issued without it: every "
                  + "document owed waits on hold (IssuerNotConfigured) until it is set.")]
    private static partial void LogNotIssued(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "FINANCIAL DOCUMENTS ARE SIGNED BY A TEST IDENTITY ({LegalName}). Allowed only in Development "
                  + "with SANDBOX payments; every document it signs is numbered TEST-, and it never signs real money.")]
    private static partial void LogTestIdentity(ILogger logger, string legalName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Financial documents issued as {LegalName}.")]
    private static partial void LogReady(ILogger logger, string legalName);

    [LoggerMessage(Level = LogLevel.Information, Message = "Financial document PDFs are {Detail}.")]
    private static partial void LogPdfsReady(ILogger logger, string detail);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "FINANCIAL DOCUMENT PDFs ARE NOT DRAWN. {Reason} Documents are still issued and readable on screen; "
                  + "their PDFs are drawn once this host can draw them and the API restarts.")]
    private static partial void LogPdfsNotDrawn(ILogger logger, string reason);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "{Count} financial document(s) are on hold. The administrator's work queue lists them with their reasons.")]
    private static partial void LogOnHold(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not count the financial documents on hold. {Reason}")]
    private static partial void LogHoldsUnreadable(ILogger logger, string reason);
}
