using Khadra.Domain.Common;

namespace Khadra.Domain.Fleet;

public static class FleetErrors
{
    public static readonly Error InvalidPlateNumber =
        Error.Validation("vehicle.invalid_plate", "The plate number is not valid.");

    public static readonly Error InvalidMakeOrModel =
        Error.Validation("vehicle.invalid_make_model", "The make and model are required and must be at most 60 characters each.");

    public static readonly Error InvalidYear =
        Error.Validation("vehicle.invalid_year", "The model year is not plausible for a rental vehicle.");

    public static readonly Error InvalidSeats =
        Error.Validation("vehicle.invalid_seats", "A vehicle must have between 1 and 20 seats.");

    public static readonly Error RateMustBePositive =
        Error.Validation("vehicle.rate_not_positive", "The daily rate must be greater than zero.");

    public static readonly Error InvalidMileagePolicy =
        Error.Validation("vehicle.invalid_mileage_policy", "A limited mileage policy needs a positive daily limit and a non-negative excess fee.");

    public static readonly Error DealerNotApproved =
        Error.Forbidden("vehicle.dealer_not_approved", "Only an approved dealer can publish vehicles.");

    public static readonly Error AlreadyPublished =
        Error.Conflict("vehicle.already_published", "This vehicle is already published.");

    public static readonly Error NotPublished =
        Error.Conflict("vehicle.not_published", "This vehicle is not published.");

    public static readonly Error ImageNotFound =
        Error.NotFound("vehicle.image_not_found", "The image was not found on this vehicle.");

    public static readonly Error TooManyImages =
        Error.Validation("vehicle.too_many_images", "A vehicle can have at most 12 images.");

    public static readonly Error ImageRequiredToPublish =
        Error.Validation("vehicle.image_required", "A vehicle needs at least one photo before it can be published.");

    public static readonly Error CurrencyMismatch =
        Error.Validation("vehicle.currency_mismatch", "The daily rate and the security deposit must use the same currency.");

    public static readonly Error AlreadyDeleted =
        Error.Conflict("vehicle.already_deleted", "This vehicle is already deleted.");
}
