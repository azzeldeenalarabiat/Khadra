using System.Net;
using System.Net.Sockets;
using System.Text;
using Khadra.Application.Common.Ports;
using Khadra.Infrastructure.Configuration;
using Khadra.Infrastructure.Notifications;
using Khadra.Tests.Support;
using Microsoft.Extensions.Options;

namespace Khadra.Tests.Infrastructure;

/// <summary>
/// What the SMTP transport's receipt claims, and what it does around the moment a relay accepts a
/// message — tested against real SMTP conversations with a relay that lives inside the test.
/// </summary>
/// <remarks>
/// A stubbed MailKit would only prove the stub. Everything worth pinning happens on the wire: the
/// Message-ID the receipt reports is the one the relay was handed, a retry hands over the SAME
/// message, nothing after the relay's acceptance can turn into a second copy or a failure, and a
/// refusal comes back out without the address the relay quoted in it.
/// </remarks>
public sealed class SmtpEmailSenderReceiptTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 17, 9, 30, 0, TimeSpan.Zero);

    /// <summary>
    /// Just enough of an SMTP relay to accept messages, to misbehave in the ways that matter, and to
    /// remember what it was sent.
    /// </summary>
    private sealed class FakeRelay : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _stop = new();
        private readonly List<string> _messages = [];
        private readonly string _replyToMessage;
        private readonly bool _hangUpOnFirstMessage;
        private readonly bool _stallOnQuit;
        private readonly string? _refuseRecipient;
        private int _connections;

        public FakeRelay(
            string replyToMessage = "250 2.0.0 Ok: queued as FAKE42",
            bool hangUpOnFirstMessage = false,
            bool stallOnQuit = false,
            string? refuseRecipient = null)
        {
            _replyToMessage = replyToMessage;
            _hangUpOnFirstMessage = hangUpOnFirstMessage;
            _stallOnQuit = stallOnQuit;
            _refuseRecipient = refuseRecipient;

            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _ = ServeAsync();
        }

        public int Port { get; }

        public int Connections => Volatile.Read(ref _connections);

        /// <summary>Every message the relay received in full, as it received it.</summary>
        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (_messages)
                    return [.. _messages];
            }
        }

        public void Dispose()
        {
            _stop.Cancel();
            _listener.Dispose();
            _stop.Dispose();
        }

        private async Task ServeAsync()
        {
            try
            {
                while (true)
                {
                    using var client = await _listener.AcceptTcpClientAsync(_stop.Token);
                    var connection = Interlocked.Increment(ref _connections);
                    try
                    {
                        await ConverseAsync(client, connection);
                    }
                    catch (IOException)
                    {
                        // The client hung up, which is the client's business.
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // The test is over.
            }
            catch (ObjectDisposedException)
            {
                // The test is over.
            }
            catch (SocketException)
            {
                // The test is over.
            }
        }

        private async Task ConverseAsync(TcpClient client, int connection)
        {
            using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true)
            {
                NewLine = "\r\n",
                AutoFlush = true,
            };

            await writer.WriteLineAsync("220 relay.test ESMTP");
            while (await reader.ReadLineAsync(_stop.Token) is { } command)
            {
                if (Is(command, "EHLO") || Is(command, "HELO"))
                {
                    await writer.WriteLineAsync("250 relay.test");
                }
                else if (Is(command, "MAIL FROM"))
                {
                    await writer.WriteLineAsync("250 2.1.0 Ok");
                }
                else if (Is(command, "RCPT TO"))
                {
                    await writer.WriteLineAsync(_refuseRecipient ?? "250 2.1.5 Ok");
                }
                else if (Is(command, "DATA"))
                {
                    await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");
                    var message = new StringBuilder();
                    while (await reader.ReadLineAsync(_stop.Token) is { } line && line != ".")
                        message.AppendLine(line);

                    lock (_messages)
                        _messages.Add(message.ToString());

                    // The whole message arrived, and the answer to it never leaves: a relay that went
                    // down, or a connection that dropped, between queueing a message and saying so.
                    if (_hangUpOnFirstMessage && connection == 1)
                        return;

                    await writer.WriteLineAsync(_replyToMessage);
                }
                else if (Is(command, "RSET") || Is(command, "NOOP"))
                {
                    await writer.WriteLineAsync("250 2.0.0 Ok");
                }
                else if (Is(command, "QUIT"))
                {
                    // Neither answers nor hangs up, until the test is over.
                    if (_stallOnQuit)
                        await Task.Delay(Timeout.Infinite, _stop.Token);

                    await writer.WriteLineAsync("221 2.0.0 Bye");
                    return;
                }
                else
                {
                    await writer.WriteLineAsync("502 5.5.2 Command not recognised");
                }
            }
        }

        private static bool Is(string command, string verb) =>
            command.StartsWith(verb, StringComparison.OrdinalIgnoreCase);
    }

    private static SmtpEmailSender Sender(int port, int timeoutSeconds = 10, int maxAttempts = 1) => new(
        Options.Create(new EmailOptions
        {
            Provider = EmailOptions.SmtpProvider,
            Host = "127.0.0.1",
            Port = port,
            UseStartTls = false,
            FromAddress = "no-reply@khadra.test",
            FromName = "Khadra",
            TimeoutSeconds = timeoutSeconds,
            MaxAttempts = maxAttempts,
        }),
        new TestClock(Now));

    private static EmailMessage Message() =>
        new("ali@example.com", "Ali Haddad", "Verify your Khadra email address", "<p>link</p>", "link");

    [Fact]
    public async Task The_receipt_names_the_message_id_the_relay_received_and_carries_the_relays_reply()
    {
        using var relay = new FakeRelay("250 2.0.0 Ok: queued as FAKE42");

        var receipt = await Sender(relay.Port).SendAsync(Message());

        Assert.Equal("Smtp", receipt.Provider);
        Assert.Equal(Now, receipt.AcceptedAt);
        Assert.Equal(1, receipt.Attempts);
        Assert.Contains("queued as FAKE42", receipt.ProviderResponse, StringComparison.Ordinal);

        // The id the log carries is the id the relay was handed — on the sender's own domain, not one
        // built from the name of the machine that sent it.
        Assert.NotNull(receipt.ProviderMessageId);
        Assert.EndsWith("@khadra.test>", receipt.ProviderMessageId, StringComparison.Ordinal);
        Assert.Contains(
            $"Message-Id: {receipt.ProviderMessageId}",
            Assert.Single(relay.Messages),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_relay_that_repeats_the_recipient_back_does_not_put_the_address_in_the_receipt()
    {
        using var relay = new FakeRelay("250 2.0.0 Ok: queued as FAKE43 for <ali@example.com>");

        var receipt = await Sender(relay.Port).SendAsync(Message());

        Assert.Contains("queued as FAKE43", receipt.ProviderResponse, StringComparison.Ordinal);
        Assert.DoesNotContain("ali@example.com", receipt.ProviderResponse, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The relay took the whole message and the answer was lost. The retry sends it again — that is
    /// the right trade, because a lost verification email costs more than a duplicate — and it sends
    /// the SAME message, under the same Message-ID, which is what lets an inbox fold the two together.
    /// </summary>
    [Fact]
    public async Task A_retry_resends_the_same_message_and_the_receipt_says_which_attempt_worked()
    {
        using var relay = new FakeRelay(hangUpOnFirstMessage: true);

        var receipt = await Sender(relay.Port, maxAttempts: 2).SendAsync(Message());

        Assert.Equal(2, receipt.Attempts);
        Assert.Equal(2, relay.Connections);
        Assert.Equal(2, relay.Messages.Count);
        Assert.All(relay.Messages, message => Assert.Contains(
            $"Message-Id: {receipt.ProviderMessageId}", message, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Once the relay has said 250 the message is sent, whatever happens saying goodbye. A relay that
    /// then stalls on QUIT used to throw after acceptance: that was retried, handing the relay a second
    /// copy, and on the last attempt it was reported as a message that did not go.
    /// </summary>
    [Fact]
    public async Task A_relay_that_stalls_after_accepting_gets_one_copy_and_the_send_succeeds()
    {
        using var relay = new FakeRelay(stallOnQuit: true);

        var receipt = await Sender(relay.Port, timeoutSeconds: 4, maxAttempts: 2).SendAsync(Message());

        Assert.Equal(1, receipt.Attempts);
        Assert.Single(relay.Messages);
        Assert.Equal(1, relay.Connections);
    }

    /// <summary>
    /// A relay's refusal quotes the address it refused, and the exception it becomes is logged at
    /// Error. The reason survives; the address does not.
    /// </summary>
    [Fact]
    public async Task A_refused_recipient_is_reported_with_the_reason_and_without_the_address()
    {
        using var relay = new FakeRelay(
            refuseRecipient: "550 5.1.1 <ali@example.com>: Recipient address rejected: User unknown");

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Sender(relay.Port).SendAsync(Message()));

        Assert.Contains("Recipient address rejected", thrown.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("ali@example.com", thrown.Message, StringComparison.OrdinalIgnoreCase);
        // Nothing the sink could print in full is attached to it.
        Assert.Null(thrown.InnerException);
        Assert.Empty(relay.Messages);
    }

    [Fact]
    public void A_long_reply_is_cut_to_the_limit_with_its_line_breaks_flattened()
    {
        var reply = ProviderReply.Text("2.0.0 Ok\r\nqueued as " + new string('x', 400), "ali@example.com");

        Assert.NotNull(reply);
        Assert.Equal(ProviderReply.MaxLength, reply!.Length);
        Assert.DoesNotContain("\r", reply, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", reply, StringComparison.Ordinal);
    }

    /// <summary>
    /// The address is taken out before the reply is cut. The other way round, a cut landing inside the
    /// address would leave its first half behind, and the replacement would no longer find it.
    /// </summary>
    [Fact]
    public void An_address_the_cut_would_split_leaves_no_half_of_itself_behind()
    {
        var reply = ProviderReply.Text(
            new string('x', ProviderReply.MaxLength - 10) + " for ali@example.com",
            "ali@example.com");

        Assert.DoesNotContain("ali@", reply, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// An acceptance keeps its id even when the id is shaped like an address, because that id is what
    /// the message is traced by. A refusal keeps no address at all, whoever it belongs to.
    /// </summary>
    [Fact]
    public void An_acceptance_keeps_its_queue_id_and_a_refusal_keeps_no_address()
    {
        const string id = "<202609170930.4242@smtp-relay.mailin.fr>";

        Assert.Contains(id, ProviderReply.Text($"2.0.0 OK: queued as {id}", "ali@example.com"), StringComparison.Ordinal);

        var refusal = ProviderReply.Refusal(
            "You can only send testing emails to your own email address (owner@example.com), not ali@example.com.");
        Assert.DoesNotContain("@", refusal, StringComparison.Ordinal);
        Assert.Contains("You can only send testing emails", refusal, StringComparison.Ordinal);
    }
}
