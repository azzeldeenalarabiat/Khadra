using Khadra.Domain.Common;
using Khadra.Domain.Shortlist;
using Khadra.Tests.Support;

namespace Khadra.Tests.Domain.Shortlist;

/// <summary>
/// The cars a customer has saved.
/// </summary>
/// <remarks>
/// The invariants worth stating are both about a TOGGLE on a mobile network: a tap that is retried
/// must not become an error, in either direction. Everything else is a cap.
/// </remarks>
public sealed class CustomerShortlistTests
{
    private static readonly DateTimeOffset Now = Build.Now;

    private static CustomerShortlist Empty() => CustomerShortlist.Start(Id.New(), Now);

    [Fact]
    public void A_new_list_belongs_to_the_customer_it_is_keyed_by()
    {
        var customerId = Id.New();
        var shortlist = CustomerShortlist.Start(customerId, Now);

        // The aggregate's identity IS the customer's, which is what makes "does this person have a
        // list" a primary-key lookup and two concurrent first saves collide rather than fork.
        Assert.Equal(customerId, shortlist.Id);
        Assert.Equal(customerId, shortlist.CustomerId);
        Assert.Equal(0, shortlist.Count);
    }

    [Fact]
    public void Saving_the_same_car_twice_succeeds_and_saves_it_once()
    {
        var shortlist = Empty();
        var car = Id.New();

        Assert.True(shortlist.Add(car, 10, Now).IsSuccess);
        Assert.True(shortlist.Add(car, 10, Now.AddSeconds(1)).IsSuccess);

        Assert.Equal(1, shortlist.Count);
        Assert.True(shortlist.Contains(car));
    }

    [Fact]
    public void Removing_a_car_that_was_never_saved_is_not_a_failure()
    {
        var shortlist = Empty();

        // Nothing to assert but the absence of a throw and an unchanged list: the customer's list
        // already says what they asked it to say.
        shortlist.Remove(Id.New());

        Assert.Equal(0, shortlist.Count);
    }

    [Fact]
    public void A_full_list_refuses_a_new_car_and_names_the_limit()
    {
        var shortlist = Empty();
        for (var index = 0; index < 3; index++)
            Assert.True(shortlist.Add(Id.New(), 3, Now).IsSuccess);

        var refused = shortlist.Add(Id.New(), 3, Now);

        Assert.True(refused.IsFailure);
        Assert.Equal("shortlist.full", refused.Error.Code);
        // The configured figure travels in the message so a client can say why without holding its
        // own copy of the platform's rule.
        Assert.Contains("3", refused.Error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A full list still accepts a car it already holds.
    /// </summary>
    /// <remarks>
    /// This is the retried tap on the last slot: the request changes nothing, and refusing it would
    /// show a customer an error about a car that is already saved.
    /// </remarks>
    [Fact]
    public void A_full_list_still_succeeds_when_re_saving_something_already_on_it()
    {
        var shortlist = Empty();
        var first = Id.New();
        Assert.True(shortlist.Add(first, 2, Now).IsSuccess);
        Assert.True(shortlist.Add(Id.New(), 2, Now).IsSuccess);

        Assert.True(shortlist.Add(first, 2, Now).IsSuccess);
        Assert.Equal(2, shortlist.Count);
    }

    [Fact]
    public void Removing_frees_a_slot()
    {
        var shortlist = Empty();
        var first = Id.New();
        Assert.True(shortlist.Add(first, 1, Now).IsSuccess);
        Assert.True(shortlist.Add(Id.New(), 1, Now).IsFailure);

        shortlist.Remove(first);

        Assert.Equal(0, shortlist.Count);
        Assert.True(shortlist.Add(Id.New(), 1, Now).IsSuccess);
    }

    [Fact]
    public void The_list_reads_newest_save_first()
    {
        var shortlist = Empty();
        var oldest = Id.New();
        var newest = Id.New();

        shortlist.Add(oldest, 10, Now);
        shortlist.Add(newest, 10, Now.AddHours(1));

        Assert.Equal([newest, oldest], shortlist.Entries.Select(entry => entry.VehicleId));
    }
}
