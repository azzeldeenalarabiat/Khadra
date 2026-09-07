using CSharpFunctionalExtensions;
using FluentValidation;
using Khadra.Application.Bookings.Dtos;
using Khadra.Application.Common;
using Khadra.Domain.Common;

namespace Khadra.Application.Bookings.CreateBooking;

/// <summary>
/// A customer asks a gallery for a car. Nothing is paid here.
/// </summary>
/// <remarks>
/// Since 2026-09-07 (see <c>docs/spec-amendments.md</c>) the deposit comes AFTER the dealer approves,
/// so this is the whole of a request: dates, a car, and how the customer wants to take it. What it
/// creates holds the vehicle until the dealer's answer window closes.
///
/// The customer sends no prices. Every figure on the booking is computed and frozen server-side by
/// <c>BookingPricer</c> — a client that could name a total could name a cheaper one.
/// </remarks>
public sealed record CreateBookingCommand(
    Id CustomerUserId,
    Id VehicleId,
    DateTimeOffset PickupAt,
    DateTimeOffset ReturnAt,
    string PickupMethod,
    double? Latitude,
    double? Longitude) : ICommand<Result<BookingDto, Error>>;

public sealed class CreateBookingCommandValidator : AbstractValidator<CreateBookingCommand>
{
    public CreateBookingCommandValidator()
    {
        RuleFor(command => command.VehicleId)
            .Must(id => !id.IsEmpty)
            .WithMessage("Choose a car.");

        RuleFor(command => command.PickupMethod)
            .NotEmpty()
            .WithMessage("Choose how you will take the car.");

        // Only the shape, never the business bounds. How soon and how far ahead a rental may start
        // are configured numbers judged by BookingWindowPolicy, which the quote endpoint applies
        // too; duplicating them here would give two answers to one question the first time the
        // owner moved either.
        RuleFor(command => command.ReturnAt)
            .GreaterThan(command => command.PickupAt)
            .WithMessage("The return must be after the pickup.");

        // A pair or neither. One half of a coordinate is a bug in the caller, and silently ignoring
        // it would book a delivery to nowhere.
        RuleFor(command => command.Longitude)
            .NotNull()
            .When(command => command.Latitude is not null)
            .WithMessage("A delivery location needs both a latitude and a longitude.");

        RuleFor(command => command.Latitude)
            .NotNull()
            .When(command => command.Longitude is not null)
            .WithMessage("A delivery location needs both a latitude and a longitude.");
    }
}
