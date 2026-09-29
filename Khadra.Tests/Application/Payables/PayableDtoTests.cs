using System.Text.Json;
using Khadra.Application.Payables.Dtos;

namespace Khadra.Tests.Application.Payables;

/// <summary>What the ledger's answers carry for a screen to state (payments Phase 8).</summary>
public sealed class PayableDtoTests
{
    [Fact]
    public void The_finance_summary_sends_its_own_count_of_everything_held_back()
    {
        // The screen states the server's count; it never adds two of its own.
        var summary = new FinanceSummaryDto(
            new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), [], HeldBookings: 2, HeldPayables: 3, BlockedPayables: 1);

        using var json = JsonDocument.Parse(JsonSerializer.Serialize(summary, JsonSerializerOptions.Web));

        Assert.Equal(5, json.RootElement.GetProperty("held").GetInt32());
        Assert.Equal(2, json.RootElement.GetProperty("heldBookings").GetInt32());
        Assert.Equal(3, json.RootElement.GetProperty("heldPayables").GetInt32());
        Assert.Equal(1, json.RootElement.GetProperty("blockedPayables").GetInt32());
    }
}
