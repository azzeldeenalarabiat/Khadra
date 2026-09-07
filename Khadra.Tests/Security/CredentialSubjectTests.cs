using System.Text;
using Khadra.WebAPI.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Khadra.Tests.Security;

/// <summary>
/// The auth rate limiter's partition key.
/// </summary>
/// <remarks>
/// Two things are being protected here. The first is the fix itself: a Jordanian carrier NATs
/// thousands of subscribers behind one address, so a limit keyed on the address alone rationed the
/// whole network to ten sign-ins a quarter hour. The second is subtler and worse — this middleware
/// reads the request body BEFORE model binding, and a body left unrewound would make every sign-in
/// fail with an empty model. That is the test nobody thinks to write until it has happened.
/// </remarks>
public sealed class CredentialSubjectTests
{
    private static async Task<HttpContext> RunAsync(string? json, string path = "/api/v1/auth/login")
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = path;

        if (json is not null)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            context.Request.ContentType = "application/json";
            context.Request.ContentLength = bytes.Length;
            context.Request.Body = new MemoryStream(bytes);
        }

        var builder = new ApplicationBuilder(new EmptyProvider());
        builder.UseCredentialSubject();
        builder.Run(_ => Task.CompletedTask);
        await builder.Build()(context);

        return context;
    }

    private sealed class EmptyProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    [Fact]
    public async Task Two_people_on_one_carrier_address_do_not_share_a_bucket()
    {
        var first = await RunAsync("""{"email":"rania@example.com","password":"x"}""");
        var second = await RunAsync("""{"email":"omar@example.com","password":"x"}""");

        // Same address, different accounts: the whole point of the change.
        Assert.NotEqual(
            CredentialSubject.PartitionKey(first, "10.0.0.1"),
            CredentialSubject.PartitionKey(second, "10.0.0.1"));
    }

    [Fact]
    public async Task One_account_hammered_from_one_address_still_shares_a_bucket()
    {
        var first = await RunAsync("""{"email":"rania@example.com","password":"a"}""");
        var second = await RunAsync("""{"email":"  RANIA@Example.COM ","password":"b"}""");

        // Normalised exactly as the account lookup normalises it, or an attacker would get a fresh
        // allowance for every casing of the same address.
        Assert.Equal(
            CredentialSubject.PartitionKey(first, "10.0.0.1"),
            CredentialSubject.PartitionKey(second, "10.0.0.1"));
    }

    [Fact]
    public async Task The_address_stays_in_the_key()
    {
        var context = await RunAsync("""{"email":"rania@example.com"}""");

        // Dropping it would let one attacker spread attempts on one account across many addresses.
        Assert.NotEqual(
            CredentialSubject.PartitionKey(context, "10.0.0.1"),
            CredentialSubject.PartitionKey(context, "10.0.0.2"));
    }

    [Fact]
    public async Task The_raw_address_is_never_the_key()
    {
        var context = await RunAsync("""{"email":"rania@example.com","password":"x"}""");
        var key = CredentialSubject.PartitionKey(context, "10.0.0.1");

        // Partition keys live in memory for the length of a window. An email address is not
        // something to leave lying in them.
        Assert.DoesNotContain("rania", key, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@", key, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_request_that_names_no_account_falls_back_to_the_address_alone()
    {
        var noEmail = await RunAsync("""{"refreshToken":"abc"}""");
        var noBody = await RunAsync(null);

        Assert.Equal("10.0.0.1", CredentialSubject.PartitionKey(noEmail, "10.0.0.1"));
        Assert.Equal("10.0.0.1", CredentialSubject.PartitionKey(noBody, "10.0.0.1"));
    }

    [Fact]
    public async Task Malformed_json_is_left_for_the_model_binder_to_complain_about()
    {
        var context = await RunAsync("""{"email": """);

        Assert.Equal("10.0.0.1", CredentialSubject.PartitionKey(context, "10.0.0.1"));
    }

    /// <summary>
    /// The body survives being read. Without this, every sign-in on the platform would bind an
    /// empty model and fail validation, and the cause would look like anything but a rate limiter.
    /// </summary>
    [Fact]
    public async Task The_body_is_rewound_so_model_binding_still_works()
    {
        const string json = """{"email":"rania@example.com","password":"correct horse"}""";
        var context = await RunAsync(json);

        Assert.Equal(0, context.Request.Body.Position);
        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
        Assert.Equal(json, await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task Nothing_outside_the_auth_endpoints_is_inspected()
    {
        // The catalogue is anonymous and high-volume; reading its bodies would cost every request
        // for no benefit, and it has no account to key on anyway.
        var context = await RunAsync(
            """{"email":"rania@example.com"}""",
            path: "/api/v1/vehicles");

        Assert.Equal("10.0.0.1", CredentialSubject.PartitionKey(context, "10.0.0.1"));
    }
}
