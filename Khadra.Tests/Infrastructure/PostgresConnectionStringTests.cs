using Khadra.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Khadra.Tests.Support;
using Npgsql;

namespace Khadra.Tests.Infrastructure;

/// <summary>
/// The two forms a platform might hand a Postgres connection over in.
/// </summary>
/// <remarks>
/// Written after a live deployment returned 500 for every request that touched the database while
/// answering everything else perfectly. The cause was a URL-form connection string, which Npgsql
/// rejects with "Format of the initialization string does not conform to specification starting at
/// index 0" — thrown on first use rather than at startup, so the application looked healthy.
/// </remarks>
public class PostgresConnectionStringTests
{
    [Fact]
    public void Keyword_form_is_left_exactly_as_it_was_given()
    {
        const string keyword = "Host=db.example.com;Port=5432;Database=khadra;Username=u;Password=p";

        Assert.Equal(keyword, PostgresConnectionString.Normalise(keyword));
    }

    [Theory]
    [InlineData("postgres://")]
    [InlineData("postgresql://")]
    [InlineData("POSTGRES://")]
    public void Both_url_schemes_are_recognised_whatever_their_casing(string scheme)
    {
        var result = PostgresConnectionString.Normalise($"{scheme}u:p@db.example.com:5432/khadra");

        var parsed = new NpgsqlConnectionStringBuilder(result);
        Assert.Equal("db.example.com", parsed.Host);
        Assert.Equal("khadra", parsed.Database);
    }

    [Fact]
    public void A_url_becomes_every_field_npgsql_needs()
    {
        var result = PostgresConnectionString.Normalise(
            "postgresql://khadra_user:s3cret@dpg-abc123.frankfurt-postgres.render.com:5432/khadra_db");

        var parsed = new NpgsqlConnectionStringBuilder(result);
        Assert.Equal("dpg-abc123.frankfurt-postgres.render.com", parsed.Host);
        Assert.Equal(5432, parsed.Port);
        Assert.Equal("khadra_db", parsed.Database);
        Assert.Equal("khadra_user", parsed.Username);
        Assert.Equal("s3cret", parsed.Password);
    }

    [Fact]
    public void A_url_with_no_port_gets_the_postgres_default()
    {
        var result = PostgresConnectionString.Normalise("postgres://u:p@db.example.com/khadra");

        Assert.Equal(5432, new NpgsqlConnectionStringBuilder(result).Port);
    }

    [Fact]
    public void A_password_with_url_characters_survives_the_round_trip()
    {
        // A generated password routinely contains these, and they MUST be percent-encoded in a URL
        // or it does not parse. Decoding them again is the whole reason this is not a string split.
        var result = PostgresConnectionString.Normalise(
            "postgresql://user:p%40ss%2Fw%3Ard%21@db.example.com:5432/khadra");

        Assert.Equal("p@ss/w:rd!", new NpgsqlConnectionStringBuilder(result).Password);
    }

    [Fact]
    public void Managed_postgres_is_reached_over_tls_even_when_the_url_is_silent()
    {
        var result = PostgresConnectionString.Normalise("postgres://u:p@db.example.com:5432/khadra");

        Assert.Equal(SslMode.Require, new NpgsqlConnectionStringBuilder(result).SslMode);
    }

    [Theory]
    [InlineData("require", SslMode.Require)]
    [InlineData("disable", SslMode.Disable)]
    [InlineData("verify-full", SslMode.VerifyFull)]
    [InlineData("verify-ca", SslMode.VerifyCA)]
    // libpq has no Npgsql equivalent for these two; both mean "use TLS if offered".
    [InlineData("prefer", SslMode.Prefer)]
    [InlineData("allow", SslMode.Prefer)]
    public void An_explicit_sslmode_in_the_url_is_honoured_and_translated(string libpq, SslMode expected)
    {
        var result = PostgresConnectionString.Normalise(
            $"postgres://u:p@db.example.com:5432/khadra?sslmode={libpq}");

        Assert.Equal(expected, new NpgsqlConnectionStringBuilder(result).SslMode);
    }

    [Fact]
    public void The_result_is_something_npgsql_will_actually_accept()
    {
        // The point of the whole class: what comes out must parse where the input did not.
        const string url = "postgresql://u:p@db.example.com:5432/khadra";

        Assert.Throws<ArgumentException>(() => new NpgsqlConnectionStringBuilder(url));
        _ = new NpgsqlConnectionStringBuilder(PostgresConnectionString.Normalise(url));
    }

    [Fact]
    public void A_url_that_cannot_be_parsed_says_which_two_forms_are_accepted()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => PostgresConnectionString.Normalise("postgres://not a url at all"));

        Assert.Contains("postgresql://", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_connection_string_is_refused(string value) =>
        Assert.Throws<ArgumentException>(() => PostgresConnectionString.Normalise(value));

    // Pre-launch item 237: the pool may not ask the database's pooler for more clients than it admits.

    [Fact]
    public void A_pool_cap_is_applied_to_a_string_that_names_none()
    {
        var result = PostgresConnectionString.ApplyPoolCap("Host=db.example.com;Database=khadra;Username=u;Password=p", 5);

        Assert.Equal(5, new NpgsqlConnectionStringBuilder(result).MaxPoolSize);
    }

    [Fact]
    public void A_smaller_cap_already_in_the_connection_string_wins()
    {
        const string staging = "Host=db.example.com;Database=khadra;Username=u;Password=p;Maximum Pool Size=3";

        Assert.Equal(3, new NpgsqlConnectionStringBuilder(PostgresConnectionString.ApplyPoolCap(staging, 5)).MaxPoolSize);
    }

    [Fact]
    public void A_larger_cap_in_the_connection_string_is_lowered_to_the_setting()
    {
        const string wide = "Host=db.example.com;Database=khadra;Username=u;Password=p;Maximum Pool Size=100";

        Assert.Equal(5, new NpgsqlConnectionStringBuilder(PostgresConnectionString.ApplyPoolCap(wide, 5)).MaxPoolSize);
    }

    [Fact]
    public void A_minimum_pool_above_the_cap_is_lowered_with_it()
    {
        const string warm = "Host=db.example.com;Database=khadra;Username=u;Password=p;Minimum Pool Size=10";

        var parsed = new NpgsqlConnectionStringBuilder(PostgresConnectionString.ApplyPoolCap(warm, 5));

        Assert.Equal(5, parsed.MaxPoolSize);
        Assert.Equal(5, parsed.MinPoolSize);
    }

    [Fact]
    public void No_setting_leaves_the_string_exactly_as_it_was()
    {
        const string keyword = "Host=db.example.com;Database=khadra;Username=u;Password=p";

        Assert.Equal(keyword, PostgresConnectionString.ApplyPoolCap(keyword, null));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_cap_below_one_is_refused_naming_the_setting(int cap)
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => PostgresConnectionString.ApplyPoolCap("Host=db.example.com;Database=khadra", cap));

        Assert.Contains("Database:MaxPoolSize", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_resolved_connection_string_carries_the_tracked_cap()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "postgresql://u:p@db.example.com:5432/khadra",
                ["Database:MaxPoolSize"] = "5",
            })
            .Build();

        var resolved = Khadra.Infrastructure.DependencyInjection.ResolveConnectionString(configuration);

        Assert.Equal(5, new NpgsqlConnectionStringBuilder(resolved).MaxPoolSize);
    }

    [Fact]
    public void The_tracked_settings_cap_the_pool_below_the_staging_pooler()
    {
        // Two instances overlap during a deploy, each with its request pool, one readiness connection
        // and (on the new one) one startup check, and Staging's session pooler admits 15.
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(RepositoryRoot.File("Khadra.WebAPI", "appsettings.json"))
            .Build();

        var cap = configuration.GetSection("Database").GetValue<int?>("MaxPoolSize");

        Assert.NotNull(cap);
        Assert.True(2 * (cap.Value + 1) + 1 <= 15, $"Database:MaxPoolSize {cap} would let a deploy's overlap exceed 15 clients.");
    }
}
