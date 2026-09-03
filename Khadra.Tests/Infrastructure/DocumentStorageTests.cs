using System.Text;
using Khadra.Infrastructure.Configuration;
using Khadra.Infrastructure.Documents;
using Khadra.Tests.Support;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Khadra.Tests.Infrastructure;

// Spec 7: identity papers live in access-controlled storage and are reachable only through a
// short-lived signed link. These tests hold that line at both ends.
public sealed class DocumentStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"khadra-docs-{Guid.NewGuid():N}");

    private LocalDocumentStorage Storage()
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.ContentRootPath.Returns(_root);
        return new LocalDocumentStorage(
            Options.Create(new DocumentStorageOptions { RootPath = _root }), environment);
    }

    [Fact]
    public async Task A_saved_document_round_trips_and_gets_a_generated_key()
    {
        var storage = Storage();
        var scope = $"customers/{Guid.CreateVersion7()}";

        var stored = await storage.SaveAsync(scope, "licence.JPG", "image/jpeg",
            new MemoryStream(Encoding.UTF8.GetBytes("front of licence")));

        Assert.StartsWith(scope, stored.StorageKey, StringComparison.Ordinal);
        // The uploaded name is discarded; only a vetted extension survives.
        Assert.DoesNotContain("licence", stored.StorageKey, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".jpg", stored.StorageKey, StringComparison.Ordinal);

        await using var read = await storage.OpenAsync(stored.StorageKey);
        Assert.NotNull(read);
        using var reader = new StreamReader(read);
        Assert.Equal("front of licence", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task A_deleted_document_is_gone()
    {
        var storage = Storage();
        var stored = await storage.SaveAsync($"customers/{Guid.CreateVersion7()}", "a.png", "image/png",
            new MemoryStream([1, 2, 3]));

        await storage.DeleteAsync(stored.StorageKey);

        Assert.Null(await storage.OpenAsync(stored.StorageKey));
    }

    [Theory]
    [InlineData("../../appsettings.json")]
    [InlineData("customers/../../secrets.json")]
    [InlineData("/etc/passwd")]
    [InlineData("customers/abc/../../../escape.jpg")]
    public async Task A_key_that_tries_to_escape_the_storage_root_is_refused(string key)
    {
        // Keys are generated, but they come back from the database eventually, and a path check is
        // cheaper than trusting that every future code path stayed disciplined.
        var storage = Storage();

        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.OpenAsync(key));
    }

    [Fact]
    public async Task A_scope_a_caller_made_up_is_refused()
    {
        var storage = Storage();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            storage.SaveAsync("../wwwroot", "a.jpg", "image/jpeg", new MemoryStream([1])));
    }

    private static HmacDocumentLinkSigner Signer() =>
        new HmacDocumentLinkSigner(
            Options.Create(new JwtOptions { SigningKey = new string('k', 64) }),
            FakeDocumentPolicy.Default);

    [Fact]
    public void A_freshly_signed_link_validates_and_carries_its_own_expiry()
    {
        var signer = Signer();
        var now = Users.Now;

        var link = signer.Sign("customers/abc/1.jpg", now);
        var (key, expires, signature) = Parse(signer, link.Url);

        Assert.Equal("customers/abc/1.jpg", key);
        Assert.True(signer.IsValid(key, expires, signature, now));
        Assert.Equal(now.AddMinutes(5), link.ExpiresAt);
    }

    [Fact]
    public void A_link_stops_working_once_it_expires()
    {
        var signer = Signer();
        var now = Users.Now;
        var link = signer.Sign("customers/abc/1.jpg", now);
        var (key, expires, signature) = Parse(signer, link.Url);

        Assert.False(signer.IsValid(key, expires, signature, now.AddMinutes(6)));
    }

    [Fact]
    public void A_signature_cannot_be_moved_onto_a_different_document()
    {
        // Otherwise one valid link would be a key to every file in storage.
        var signer = Signer();
        var now = Users.Now;
        var link = signer.Sign("customers/abc/1.jpg", now);
        var (_, expires, signature) = Parse(signer, link.Url);

        Assert.False(signer.IsValid("customers/someone-else/1.jpg", expires, signature, now));
    }

    [Fact]
    public void A_tampered_expiry_or_signature_is_rejected()
    {
        var signer = Signer();
        var now = Users.Now;
        var link = signer.Sign("customers/abc/1.jpg", now);
        var (key, expires, signature) = Parse(signer, link.Url);

        Assert.False(signer.IsValid(key, expires + 86_400, signature, now));
        Assert.False(signer.IsValid(key, expires, signature[..^2] + "xy", now));
        Assert.False(signer.IsValid(key, expires, string.Empty, now));
    }

    [Fact]
    public void A_token_that_is_not_valid_base64url_is_rejected_rather_than_throwing()
    {
        Assert.False(Signer().TryDecodeToken("!!!not-base64!!!", out _));
    }

    private static (string Key, long Expires, string Signature) Parse(HmacDocumentLinkSigner signer, string url)
    {
        var uri = new Uri("https://localhost" + url);
        var token = uri.AbsolutePath.Split('/')[^1];
        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
        Assert.True(signer.TryDecodeToken(token, out var key));
        return (key, long.Parse(query["expires"]!, System.Globalization.CultureInfo.InvariantCulture), query["signature"]!);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
