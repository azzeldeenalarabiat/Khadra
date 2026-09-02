namespace Khadra.Application.Common.Ports;

public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string hash);

    // A throwaway hash used to equalise timing when the account does not exist.
    string DummyHash { get; }
}
