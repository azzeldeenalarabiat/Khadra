using Khadra.Application.Common.Ports;

namespace Khadra.Tests.Application.Common;

// One answer to "what is this file", shared by the endpoint that serves the bytes and the screen
// that labels the link. They were separate switches, and the console had a third answer of its own
// (".jpg", always), which is how an Admin came to be told that a PDF licence was a photograph.
public sealed class DocumentContentTypeTests
{
    [Theory]
    [InlineData("dealers/a/registration.pdf", "application/pdf")]
    [InlineData("dealers/a/registration.png", "image/png")]
    [InlineData("dealers/a/registration.webp", "image/webp")]
    [InlineData("dealers/a/registration.jpg", "image/jpeg")]
    [InlineData("dealers/a/registration.jpeg", "image/jpeg")]
    public void A_key_is_read_as_the_type_it_will_be_served_as(string key, string expected) =>
        Assert.Equal(expected, DocumentContentTypes.ForStorageKey(key));

    // Storage generates every key and only ever appends an accepted extension, so these cannot
    // arise; falling back to an image beats throwing on the read path of a licence check.
    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("dealers/a/registration")]
    public void An_unrecognised_key_falls_back_rather_than_throwing(string? key) =>
        Assert.Equal(DocumentContentTypes.Fallback, DocumentContentTypes.ForStorageKey(key));

    [Fact]
    public void The_extension_is_read_without_regard_to_case() =>
        Assert.Equal("application/pdf", DocumentContentTypes.ForStorageKey("dealers/a/DEED.PDF"));
}
