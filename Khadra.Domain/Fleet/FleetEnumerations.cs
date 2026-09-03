using Khadra.Domain.Common;

namespace Khadra.Domain.Fleet;

// Draft lets a dealer build a listing before exposing it. Only Active vehicles reach customer search.
//
// Note what is NOT here: "Booked". Whether a car is free on given dates is a question about bookings,
// and a stored flag would have to be kept in step with every booking, cancellation and no-show. It is
// derived at query time instead (see Vehicle, which says the same thing about availability).
public sealed class VehicleStatus : Enumeration
{
    public static readonly VehicleStatus Draft = new(1, "Draft");
    public static readonly VehicleStatus Active = new(2, "Active");
    public static readonly VehicleStatus Hidden = new(3, "Hidden");
    // Off the road for servicing. Distinct from Hidden, which is a marketing decision: this car
    // physically cannot be rented, and a dealer needs to say so without pretending it was delisted.
    public static readonly VehicleStatus Maintenance = new(4, "Maintenance");

    private VehicleStatus(int id, string name) : base(id, name)
    {
    }

    public bool IsBookable => this == Active;

    /// <summary>States a dealer moves a listing between; Draft is only ever left, never returned to.</summary>
    public bool IsDealerControlled => this != Draft;
}

public sealed class TransmissionType : Enumeration
{
    public static readonly TransmissionType Automatic = new(1, "Automatic");
    public static readonly TransmissionType Manual = new(2, "Manual");

    private TransmissionType(int id, string name) : base(id, name)
    {
    }
}

public sealed class FuelType : Enumeration
{
    public static readonly FuelType Petrol = new(1, "Petrol");
    public static readonly FuelType Diesel = new(2, "Diesel");
    public static readonly FuelType Hybrid = new(3, "Hybrid");
    public static readonly FuelType Electric = new(4, "Electric");

    private FuelType(int id, string name) : base(id, name)
    {
    }
}

// Spec 4.3: per-car fuel policy. FullToFull is the Jordanian market norm.
public sealed class FuelPolicy : Enumeration
{
    public static readonly FuelPolicy FullToFull = new(1, "FullToFull");
    public static readonly FuelPolicy SameToSame = new(2, "SameToSame");

    private FuelPolicy(int id, string name) : base(id, name)
    {
    }
}
