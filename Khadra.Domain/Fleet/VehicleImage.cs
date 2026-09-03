using Khadra.Domain.Common;
using Entity = Khadra.Domain.Common.Entity;

namespace Khadra.Domain.Fleet;

// Car photos are public marketing images, unlike the private ID documents in the Dealers and
// IdentityAccess contexts, so a plain storage key with a public URL is fine here (spec 7).
public sealed class VehicleImage : Entity
{
    public Id VehicleId { get; private set; }
    public string StorageKey { get; private set; } = null!;
    public int Position { get; private set; }
    public bool IsPrimary { get; private set; }
    public DateTimeOffset UploadedAt { get; private set; }

    private VehicleImage()
    {
    }

    private VehicleImage(Id id) : base(id)
    {
    }

    internal static VehicleImage Create(Id vehicleId, string storageKey, int position, bool isPrimary, DateTimeOffset now)
    {
        if (vehicleId.IsEmpty)
            throw new DomainException("An image requires a vehicle.");
        if (string.IsNullOrWhiteSpace(storageKey) || storageKey.Length > 500)
            throw new DomainException("An image storage key is required.");

        return new VehicleImage(Id.New())
        {
            VehicleId = vehicleId,
            StorageKey = storageKey.Trim(),
            Position = position,
            IsPrimary = isPrimary,
            UploadedAt = now
        };
    }

    internal void SetPrimary(bool isPrimary) => IsPrimary = isPrimary;

    internal void SetPosition(int position) => Position = position;
}
