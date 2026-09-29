using Khadra.Infrastructure;
using Khadra.Infrastructure.Configuration;
using Khadra.Tests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Khadra.Tests.Security;

/// <summary>
/// The business rules the shipped settings file carries, and the ones startup refuses.
/// </summary>
/// <remarks>
/// Built from the REAL <c>Khadra.WebAPI/appsettings.json</c>, with one key changed per case, so each
/// test proves both that the shipped file is valid and that the change alone is what startup refuses.
/// </remarks>
public sealed class BusinessRulesConfigurationTests
{
    private static BusinessRulesOptions Rules(params (string Key, string? Value)[] overrides)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=khadra_tests;Username=x;Password=y",
            ["Authentication:Jwt:SigningKey"] = new string('k', 48),
        };
        foreach (var (key, value) in overrides)
            settings[key] = value;

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(RepositoryRoot.File("Khadra.WebAPI", "appsettings.json"))
            .AddInMemoryCollection(settings)
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<Microsoft.Extensions.Hosting.IHostEnvironment>(
            new Microsoft.Extensions.Hosting.Internal.HostingEnvironment
            {
                ContentRootPath = Path.GetTempPath(),
                EnvironmentName = "Testing",
                ApplicationName = "Khadra.Tests",
            });
        services.AddInfrastructureServices(configuration);
        return services.BuildServiceProvider().GetRequiredService<IOptions<BusinessRulesOptions>>().Value;
    }

    [Fact]
    public void The_shipped_rules_price_commission_by_one_day_and_leave_the_processing_fee_off()
    {
        var rules = Rules();

        Assert.Equal("OneDay", rules.CommissionBasis);
        Assert.False(rules.ProcessingFee.Enabled);
        Assert.True(rules.ProcessingFee.Refundable);
    }

    [Theory]
    [InlineData("BusinessRules:CommissionBasis", "PerBooking", "CommissionBasis")]
    [InlineData("BusinessRules:ProcessingFee:Basis", "Everything", "ProcessingFee.Basis")]
    public void An_unknown_name_is_refused_at_startup(string key, string value, string named)
    {
        var failure = Assert.Throws<OptionsValidationException>(() => Rules((key, value)));

        Assert.Contains(named, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_enabled_processing_fee_of_nothing_is_refused_at_startup()
    {
        var failure = Assert.Throws<OptionsValidationException>(() => Rules(
            ("BusinessRules:ProcessingFee:Enabled", "true"),
            ("BusinessRules:ProcessingFee:Percent", "0")));

        Assert.Contains("ProcessingFee", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("50")]
    [InlineData("0")]
    public void A_customer_penalty_below_the_whole_deposit_is_refused_at_startup(string percent)
    {
        // Payments Phase 8 (pre-launch item 205): the ledger keeps a customer's penalty from the deposit when the window
        // closes with no dispute, and nothing can return the rest of a partial one yet.
        var failure = Assert.Throws<OptionsValidationException>(() => Rules(("BusinessRules:CustomerCancellationPenaltyPercent", percent)));

        Assert.Contains("CustomerCancellationPenaltyPercent", failure.Message, StringComparison.Ordinal);
        Assert.Equal(100m, Rules().CustomerCancellationPenaltyPercent);
    }

    [Fact]
    public void An_enabled_processing_fee_with_a_figure_is_accepted()
    {
        var rules = Rules(
            ("BusinessRules:ProcessingFee:Enabled", "true"),
            ("BusinessRules:ProcessingFee:Percent", "1.5"),
            ("BusinessRules:ProcessingFee:Basis", "AboveDeposit"));

        Assert.True(rules.ProcessingFee.Enabled);
        Assert.Equal(1.5m, rules.ProcessingFee.Percent);
    }
}
