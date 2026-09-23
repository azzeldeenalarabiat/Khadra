namespace Khadra.Bff.Security;

/// <summary>
/// The names under which one BFF deployment keeps its state in a Redis it may share with another.
/// </summary>
/// <remarks>
/// <para>
/// Two deployments on one Redis (Render gives a workspace a single free Key Value) must not read each
/// other's state. The Data Protection application name is what isolates them cryptographically: with a
/// different name, a console session cookie presented to the customer BFF does not decrypt, whatever
/// key ring either can see. The cache prefix and the key-ring key keep the stored bytes apart too, so
/// one deployment rotating or losing its keys cannot touch the other's.
/// </para>
/// <para>
/// The console keeps EXACTLY the names it had before deployments existed. Changing any of them would
/// sign every staff member out on the next deploy and orphan the key ring their cookies were
/// protected with.
/// </para>
/// </remarks>
internal sealed record BffRealm(string CacheInstanceName, string DataProtectionApplicationName, string KeyRingKey)
{
    public const string ConsoleDeployment = "console";

    public static BffRealm For(string deployment) =>
        deployment == ConsoleDeployment
            ? new BffRealm("khadra-bff:", "Khadra.Bff", "khadra-bff:dataprotection-keys")
            : new BffRealm($"khadra-bff-{deployment}:", $"Khadra.Bff.{deployment}", $"khadra-bff-{deployment}:dataprotection-keys");
}
