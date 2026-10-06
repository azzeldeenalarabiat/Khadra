using Khadra.Domain.Common;
using Khadra.Domain.Dealers;
using Khadra.Domain.Notifications;
using Khadra.Tests.Support;

namespace Khadra.Tests.Domain.Dealers;

/// <summary>
/// No rental office may take the platform's own name (owner, 2026-10-05 and 2026-10-06; pre-launch item 230): "Khadra",
/// «خضرا», and «خضراء» with hamza. An office's notifications to its customers carry its business name the way the
/// platform's carry "Khadra", so an office approved under that name would be announced as the platform.
/// </summary>
public sealed class ReservedBusinessNameTests
{
    [Theory]
    [InlineData("Khadra")]
    [InlineData("khadra")]
    [InlineData("KHADRA")]
    [InlineData("  Khadra  ")]
    [InlineData("Kha​dra")] // a zero-width space inside
    [InlineData("‏Khadra")] // a direction mark before
    [InlineData("Ｋｈａｄｒａ")] // full-width letters
    [InlineData("خضرا")]
    [InlineData("خضراء")]
    [InlineData("خَضرا")] // with a fatha
    [InlineData("خضـرا")] // stretched with a tatweel
    [InlineData("ﺧﻀﺮﺎ")] // the same word in Arabic presentation forms
    public void The_platforms_names_are_reserved_however_they_are_typed(string name) =>
        Assert.True(Platform.IsReservedName(name));

    [Theory]
    [InlineData("Khadra Rentals")]
    [InlineData("Khadra Cars")]
    [InlineData("خضرا للتأجير")]
    [InlineData("Kadra")]
    [InlineData("Petra Rentals")]
    [InlineData("")]
    [InlineData(null)]
    public void Any_other_name_is_free(string? name) =>
        Assert.False(Platform.IsReservedName(name));

    [Fact]
    public void The_platforms_actor_name_is_the_one_reserved()
    {
        Assert.Equal(Platform.Name, Notification.PlatformActorName);
        Assert.Contains(Platform.Name, Platform.ReservedNames);
    }

    [Fact]
    public void A_new_office_cannot_register_under_the_platforms_name()
    {
        var refused = Dealer.Register(
            Id.New(),
            BusinessName.Create("Khadra").Value,
            CommercialRegistrationNumber.Create("445566").Value,
            Build.Amman,
            Build.NineToFive,
            Build.Now,
            Build.ReviewSla);

        Assert.True(refused.IsFailure);
        Assert.Equal("dealer.business_name_reserved", refused.Error.Code);
        // Named on the field, so the form puts the message under the box that caused it.
        Assert.Contains("businessName", refused.Error.Details!.Keys);
    }

    [Fact]
    public void An_applicant_still_fixing_its_application_cannot_rename_itself_to_it()
    {
        var applicant = Build.Dealer();

        var refused = applicant.UpdateProfile(
            BusinessName.Create("خضراء").Value, Build.Amman, Build.NineToFive, null, null);

        Assert.Equal("dealer.business_name_reserved", refused.Error.Code);
        Assert.Equal("Petra Rentals", applicant.BusinessName.Value);
    }

    [Fact]
    public void An_office_already_carrying_the_name_can_still_save_its_page()
    {
        // A row stored before the rule existed, the way EF materialises it: through FromPersisted, past Create. The
        // dealer page sends the current name with every save, so only a CHANGED name is checked.
        var office = Build.ApprovedDealer();
        typeof(Dealer).GetProperty(nameof(Dealer.BusinessName))!.SetValue(office, BusinessName.FromPersisted("Khadra"));

        var saved = office.UpdateProfile(
            BusinessName.FromPersisted("Khadra"), Build.Amman, Build.NineToFive, null, null);

        Assert.True(saved.IsSuccess, saved.IsFailure ? saved.Error.Code : null);
    }
}
