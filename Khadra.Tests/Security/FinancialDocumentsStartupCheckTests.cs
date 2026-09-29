using Khadra.Application.FinancialDocuments.ReadModels;
using Khadra.Application.FinancialDocuments.Rendering;
using Khadra.Infrastructure.Persistence;
using Khadra.Tests.Support;
using Khadra.WebAPI;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Khadra.Tests.Security;

/// <summary>
/// What the boot log says about PDFs (payments Phase 6): drawn, with the renderer and the licence it is used
/// under — or, loudly, that they are not drawn and why. A host that silently draws nothing looks exactly like
/// one with nothing to draw.
/// </summary>
public sealed class FinancialDocumentsStartupCheckTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly Capture _log = new();

    public FinancialDocumentsStartupCheckTests() => _connection.Open();

    public void Dispose()
    {
        _connection.Dispose();
        _log.Dispose();
    }

    [Fact]
    public async Task A_host_that_can_draw_says_so_and_names_the_licence()
    {
        var renderer = Substitute.For<IFinancialDocumentPdfRenderer>();
        renderer.Probe().Returns(new PdfRendererStatus(true, "drawn with QuestPDF 2026.9.1 under the QuestPDF Community licence"));

        await FinancialDocumentsStartupCheck.ReportAsync(Services(renderer));

        var line = Assert.Single(_log.Lines, line => line.Message.Contains("PDF", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Information, line.Level);
        Assert.Equal("Financial document PDFs are drawn with QuestPDF 2026.9.1 under the QuestPDF Community licence.", line.Message);
    }

    [Fact]
    public async Task A_host_that_cannot_draw_says_so_loudly_with_the_reason()
    {
        var renderer = Substitute.For<IFinancialDocumentPdfRenderer>();
        renderer.Probe().Returns(new PdfRendererStatus(false, "DllNotFoundException: libSkiaSharp."));

        await FinancialDocumentsStartupCheck.ReportAsync(Services(renderer));

        var line = Assert.Single(_log.Lines, line => line.Message.Contains("PDF", StringComparison.Ordinal));
        Assert.Equal(LogLevel.Warning, line.Level);
        Assert.StartsWith("FINANCIAL DOCUMENT PDFs ARE NOT DRAWN. DllNotFoundException: libSkiaSharp.", line.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_real_renderer_draws_on_this_host_and_says_under_which_licence()
    {
        var status = new Khadra.Infrastructure.FinancialDocuments.QuestPdfFinancialDocumentRenderer().Probe();

        Assert.True(status.IsReady, status.Description);
        Assert.StartsWith("drawn with QuestPDF 2026.", status.Description, StringComparison.Ordinal);
        Assert.EndsWith("under the QuestPDF Community licence", status.Description, StringComparison.Ordinal);
    }

    private ServiceProvider Services(IFinancialDocumentPdfRenderer renderer)
    {
        var options = new DbContextOptionsBuilder<KhadraDbContext>().UseSqlite(_connection).UseSnakeCaseNamingConvention().Options;
        using (var context = new KhadraDbContext(options))
            context.Database.EnsureCreated();

        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(_log));
        services.AddSingleton<IFinancialDocumentSettings>(new TestDocumentSettings(DocumentFixtures.Issuer));
        services.AddSingleton(renderer);
        services.AddScoped(_ => new KhadraDbContext(options));
        return services.BuildServiceProvider();
    }

    /// <summary>Every line the check writes, rendered.</summary>
    private sealed class Capture : ILoggerProvider
    {
        public List<(LogLevel Level, string Message)> Lines { get; } = [];

        public ILogger CreateLogger(string categoryName) => new Sink(this);

        public void Dispose()
        {
        }

        private sealed class Sink(Capture capture) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                capture.Lines.Add((logLevel, formatter(state, exception)));
        }
    }
}
