using Khadra.Domain.Auditing;
using Khadra.Domain.Common;
using Khadra.Domain.IdentityAccess;

namespace Khadra.Tests.Domain.Auditing;

public sealed class AuditEntryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void An_attributed_entry_snapshots_who_acted_rather_than_pointing_at_them()
    {
        var adminId = Id.New();

        var entry = AuditEntry.By(
            adminId, "Rania Haddad", UserRole.Admin,
            AuditAction.DealerApproved, AuditEntityType.Dealer, Id.New(),
            "Aqaba Coast Cars", Now,
            previousValue: "PendingReview", newValue: "Approved", reason: "Documents verified.");

        // The name is copied, not looked up: the line must still read correctly after a rename, a
        // role change, or the account being soft-deleted.
        Assert.Equal("Rania Haddad", entry.ActorName);
        Assert.Same(UserRole.Admin, entry.ActorRole);
        Assert.Equal(adminId, entry.ActorUserId);
        Assert.Equal("Aqaba Coast Cars", entry.SubjectLabel);
        Assert.Equal("PendingReview", entry.PreviousValue);
        Assert.Equal("Approved", entry.NewValue);
    }

    [Fact]
    public void Scheduled_work_records_with_no_actor_rather_than_borrowing_one()
    {
        var entry = AuditEntry.BySystem(
            AuditAction.BookingExpired, AuditEntityType.Booking, Id.New(), "KH-20984", Now);

        Assert.Null(entry.ActorUserId);
        Assert.Null(entry.ActorRole);
        Assert.Equal(AuditEntry.SystemActorName, entry.ActorName);
    }

    [Fact]
    public void An_attributed_entry_demands_an_actor()
    {
        Assert.Throws<DomainException>(() => AuditEntry.By(
            Id.Empty, "Nobody", UserRole.Admin,
            AuditAction.DealerApproved, AuditEntityType.Dealer, Id.New(), "Some dealer", Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_entry_without_a_subject_label_is_a_line_nobody_could_read(string label)
    {
        Assert.Throws<DomainException>(() => AuditEntry.BySystem(
            AuditAction.BookingExpired, AuditEntityType.Booking, Id.New(), label, Now));
    }

    [Fact]
    public void Long_values_are_clipped_rather_than_rejected()
    {
        // An audit write must never be the reason a privileged action fails, so an over-long value is
        // truncated instead of throwing.
        var entry = AuditEntry.By(
            Id.New(), "Rania Haddad", UserRole.Admin,
            AuditAction.BusinessRuleChanged, AuditEntityType.Setting, null,
            "commission_rate", Now,
            newValue: new string('x', AuditEntry.MaxValueLength + 500),
            reason: new string('y', AuditEntry.MaxReasonLength + 500));

        Assert.Equal(AuditEntry.MaxValueLength, entry.NewValue!.Length);
        Assert.Equal(AuditEntry.MaxReasonLength, entry.Reason!.Length);
    }

    [Fact]
    public void Blank_optional_values_are_stored_as_absent_not_as_empty_strings()
    {
        var entry = AuditEntry.BySystem(
            AuditAction.BookingMarkedNoShow, AuditEntityType.Booking, Id.New(), "KH-20402", Now,
            previousValue: "  ", newValue: null, reason: string.Empty);

        Assert.Null(entry.PreviousValue);
        Assert.Null(entry.NewValue);
        Assert.Null(entry.Reason);
    }

    [Fact]
    public void An_entry_exposes_no_way_to_change_it_once_created()
    {
        // Immutability is the whole point, so it is asserted structurally rather than by behaviour:
        // there is nothing on the type that could mutate an entry after it has been constructed.
        // Init-only setters do not count -- they are how the factory and EF build one in the first
        // place, and the compiler already refuses them everywhere else.
        var settable = typeof(AuditEntry)
            .GetProperties()
            .Where(property => property.SetMethod is { IsPublic: true } setter && !IsInitOnly(setter))
            .Select(property => property.Name)
            .ToList();

        Assert.Empty(settable);
    }

    private static bool IsInitOnly(System.Reflection.MethodInfo setter) =>
        setter.ReturnParameter
            .GetRequiredCustomModifiers()
            .Contains(typeof(System.Runtime.CompilerServices.IsExternalInit));
}
