namespace Khadra.Application.Common.Ports;

// `Value` goes to the client (once); only `Hash` is ever persisted.
public sealed record GeneratedOpaqueToken(string Value, string Hash);

public interface IOpaqueTokenService
{
    GeneratedOpaqueToken Generate();

    string Hash(string rawToken);
}
