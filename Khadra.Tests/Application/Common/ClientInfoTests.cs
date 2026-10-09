using Khadra.Application.Common;

namespace Khadra.Tests.Application.Common;

/// <summary>
/// Who a rule an app build learned still spares (Wave 7): only an app build that DECLARED an older version, and only until
/// the minimum supported version refuses it outright. A temporary bridge, never a security boundary (pre-launch item 239).
/// </summary>
public sealed class ClientInfoTests
{
    private static readonly AppVersion Rule = AppVersion.Parse("1.4.0");

    private static ClientInfo App(string version) => new("1.2.3.4", "Dart/3.5", AppVersion.Parse(version));

    [Fact]
    public void Only_an_app_build_older_than_the_rule_predates_it()
    {
        Assert.True(App("1.3.0").PredatesRule(Rule));
        Assert.True(App("1.3.9+40").PredatesRule(Rule));
        Assert.True(App("1.4.0-rc.1").PredatesRule(Rule));
        Assert.False(App("1.4.0").PredatesRule(Rule));
        Assert.False(App("1.4.0+7").PredatesRule(Rule));
        Assert.False(App("1.10.0").PredatesRule(Rule));
    }

    [Fact]
    public void The_website_the_console_and_a_caller_nobody_named_are_held_to_every_rule()
    {
        Assert.False(new ClientInfo("1.2.3.4", "Mozilla/5.0").PredatesRule(Rule));
        Assert.False(ClientInfo.Unknown.PredatesRule(Rule));
        Assert.False(ClientInfo.Unknown.IsCustomerApp);
        Assert.True(App("1.3.0").IsCustomerApp);
    }

    [Fact]
    public void A_version_written_in_this_codebase_must_parse()
    {
        Assert.Equal("1.4.0", AppVersion.Parse("1.4.0+7").ToString());
        Assert.Throws<FormatException>(() => AppVersion.Parse("v1.4"));
    }

    [Fact]
    public void Every_rule_a_build_learned_was_learned_by_1_4_0()
    {
        Assert.Equal(Rule, MobileAppContract.ConsentAwareFrom);
        Assert.Equal(Rule, MobileAppContract.HandoverWindowAwareFrom);
        Assert.Equal(Rule, MobileAppContract.DisputeSharesWithheldFrom);
    }
}
