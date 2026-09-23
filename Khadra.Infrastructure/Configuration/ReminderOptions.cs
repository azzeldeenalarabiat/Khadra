using System.ComponentModel.DataAnnotations;
using Khadra.Application.Bookings.Reminders;
using Microsoft.Extensions.Options;

namespace Khadra.Infrastructure.Configuration;

/// <summary>
/// How far ahead customers are reminded. Settled by the owner on 2026-09-23: one hour before pickup,
/// one hour before return, thirty minutes before the deposit deadline.
/// </summary>
/// <remarks>
/// Reminders fire only while the API is running. A Render free instance sleeps after fifteen idle
/// minutes; a reminder whose window passes while it sleeps is sent late if the moment is still ahead,
/// and not at all once the moment has passed. Exact delivery needs an always-on instance.
/// </remarks>
public sealed class ReminderOptions
{
    public const string SectionName = "Reminders";

    [Range(1, 1440)]
    public int PaymentLeadMinutes { get; init; } = 30;

    [Range(1, 1440)]
    public int PickupLeadMinutes { get; init; } = 60;

    [Range(1, 1440)]
    public int ReturnLeadMinutes { get; init; } = 60;
}

internal sealed class ReminderSettings(IOptions<ReminderOptions> options) : IReminderSettings
{
    public TimeSpan PaymentLead => TimeSpan.FromMinutes(options.Value.PaymentLeadMinutes);

    public TimeSpan PickupLead => TimeSpan.FromMinutes(options.Value.PickupLeadMinutes);

    public TimeSpan ReturnLead => TimeSpan.FromMinutes(options.Value.ReturnLeadMinutes);
}
