using System.Text.Json;
using Khadra.Application.Common.Ports;
using Khadra.Application.FinancialDocuments.Composition;
using Khadra.Domain.Bookings;
using Khadra.Infrastructure.Configuration;
using Khadra.Infrastructure.Reporting;
using Microsoft.Extensions.Options;

namespace Khadra.Tests.Support;

/// <summary>What composing a financial document needs in a test (payments Phase 5).</summary>
internal static class DocumentFixtures
{
    public static readonly IReportingCalendar Amman = new ReportingCalendar(
        Options.Create(new AdminDashboardOptions { ReportingTimeZone = "Asia/Amman" }));

    /// <summary>An identity that says what it is: tests never use anything that could pass for a real one.</summary>
    public static readonly DocumentIssuer Issuer = new(
        BilingualText.Of("TEST ISSUER — not a real company", "جهة إصدار تجريبية — ليست شركة حقيقية"),
        "TEST-0000",
        BilingualText.Of("Test address, Amman", "عنوان تجريبي، عمّان"),
        "documents@example.invalid",
        "+962 6 000 0000",
        IsTestIdentity: true);

    public static FinancialDocumentComposer Composer() => new(Amman);

    public static DocumentParties PartiesOf(Booking booking, string office = "Petra Rentals") =>
        new(
            new CustomerParty(booking.CustomerId, "Rana Sharif"),
            new OfficeParty(booking.DealerId, office, "123456", BilingualText.Of("Amman", "عمّان"), "Abdoun", "Street 12"),
            new VehicleParty(booking.VehicleId, "Toyota", "Corolla", 2024, "12-34567", BilingualText.Of("Sedan", "سيدان")));

    public static JsonElement Parse(string snapshot)
    {
        using var document = JsonDocument.Parse(snapshot);
        return document.RootElement.Clone();
    }

    /// <summary>Every property name anywhere in the document: what a leak test looks through.</summary>
    public static IReadOnlySet<string> PropertyNames(JsonElement element)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        Collect(element, names);
        return names;

        static void Collect(JsonElement node, HashSet<string> into)
        {
            if (node.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in node.EnumerateObject())
                {
                    into.Add(property.Name);
                    Collect(property.Value, into);
                }
            }
            else if (node.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in node.EnumerateArray())
                    Collect(item, into);
            }
        }
    }

    /// <summary>The content line with this key in the section with this key.</summary>
    public static JsonElement Line(JsonElement snapshot, string section, string line)
    {
        foreach (var candidate in snapshot.GetProperty("content").GetProperty("sections").EnumerateArray())
        {
            if (candidate.GetProperty("key").GetString() != section)
                continue;
            foreach (var entry in candidate.GetProperty("lines").EnumerateArray())
            {
                if (entry.GetProperty("key").GetString() == line)
                    return entry;
            }
        }

        throw new KeyNotFoundException($"No line {section}/{line}.");
    }

    public static IReadOnlyList<string> SectionKeys(JsonElement snapshot) =>
        [.. snapshot.GetProperty("content").GetProperty("sections").EnumerateArray().Select(section => section.GetProperty("key").GetString()!)];

    public static string Amount(JsonElement money) => money.GetProperty("amount").GetString()!;
}
