using Khadra.Bff.Security;
using StackExchange.Redis;

namespace Khadra.Tests.Infrastructure;

/// <summary>
/// The two forms a platform might hand a Redis connection over in.
/// </summary>
/// <remarks>
/// Written before this could fail rather than after, having just watched the identical mismatch take
/// out the database. Here the consequence would have been worse: the BFF awaits
/// <c>ConnectionMultiplexer.ConnectAsync</c> during startup and treats failure as fatal, so a URL
/// would crash-loop the admin console rather than producing a 500 on one endpoint.
/// </remarks>
public class RedisConnectionStringTests
{
    [Fact]
    public void Native_form_is_left_exactly_as_it_was_given()
    {
        const string native = "redis-abc.frankfurt.render.com:6379,password=s3cret,ssl=True";

        Assert.Equal(native, RedisConnectionString.Normalise(native));
    }

    [Fact]
    public void A_url_becomes_an_endpoint_stackexchange_can_read()
    {
        var result = RedisConnectionString.Normalise("redis://:s3cret@red-abc123:6379");

        var parsed = ConfigurationOptions.Parse(result);
        Assert.Equal("s3cret", parsed.Password);
        Assert.Single(parsed.EndPoints);
        Assert.Contains("red-abc123", parsed.EndPoints[0].ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void A_url_with_no_port_gets_the_redis_default()
    {
        var result = RedisConnectionString.Normalise("redis://:s3cret@red-abc123");

        Assert.Contains("6379", ConfigurationOptions.Parse(result).EndPoints[0].ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void The_rediss_scheme_turns_tls_on()
    {
        // Getting this wrong fails as a timeout, which names nothing.
        var result = RedisConnectionString.Normalise("rediss://:s3cret@red-abc123:6380");

        Assert.True(ConfigurationOptions.Parse(result).Ssl);
    }

    [Fact]
    public void The_plain_redis_scheme_leaves_tls_off()
    {
        var result = RedisConnectionString.Normalise("redis://:s3cret@red-abc123:6379");

        Assert.False(ConfigurationOptions.Parse(result).Ssl);
    }

    [Fact]
    public void A_url_with_a_username_carries_it_across()
    {
        // Redis 6 added usernames; before that a URL carried only a password.
        var result = RedisConnectionString.Normalise("redis://default:s3cret@red-abc123:6379");

        var parsed = ConfigurationOptions.Parse(result);
        Assert.Equal("default", parsed.User);
        Assert.Equal("s3cret", parsed.Password);
    }

    [Fact]
    public void A_url_with_no_username_is_normal_rather_than_an_error()
    {
        var parsed = ConfigurationOptions.Parse(RedisConnectionString.Normalise("redis://:s3cret@red-abc123:6379"));

        Assert.Null(parsed.User);
        Assert.Equal("s3cret", parsed.Password);
    }

    [Fact]
    public void A_password_with_url_characters_survives_the_round_trip()
    {
        var result = RedisConnectionString.Normalise("redis://:p%40ss%2Fw%3Ard@red-abc123:6379");

        Assert.Equal("p@ss/w:rd", ConfigurationOptions.Parse(result).Password);
    }

    [Fact]
    public void A_database_number_in_the_path_is_honoured()
    {
        var result = RedisConnectionString.Normalise("redis://:s3cret@red-abc123:6379/3");

        Assert.Equal(3, ConfigurationOptions.Parse(result).DefaultDatabase);
    }

    [Fact]
    public void A_url_that_cannot_be_parsed_says_which_two_forms_are_accepted()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => RedisConnectionString.Normalise("redis://not a url at all"));

        Assert.Contains("redis://", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_empty_connection_string_is_refused(string value) =>
        Assert.Throws<ArgumentException>(() => RedisConnectionString.Normalise(value));
}
