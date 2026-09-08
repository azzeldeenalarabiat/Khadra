// ignore: unused_import
import 'package:intl/intl.dart' as intl;
import 'app_localizations.dart';

// ignore_for_file: type=lint

/// The translations for English (`en`).
class AppLocalizationsEn extends AppLocalizations {
  AppLocalizationsEn([String locale = 'en']) : super(locale);

  @override
  String get appName => 'Khadra';

  @override
  String get appTagline => 'Rent a car in Jordan';

  @override
  String get actionRetry => 'Try again';

  @override
  String get actionCancel => 'Cancel';

  @override
  String get actionClose => 'Close';

  @override
  String get actionSave => 'Save';

  @override
  String get actionSaveChanges => 'Save changes';

  @override
  String get actionContinue => 'Continue';

  @override
  String get actionBack => 'Back';

  @override
  String get actionDone => 'Done';

  @override
  String get actionApply => 'Apply';

  @override
  String get actionClearAll => 'Clear all';

  @override
  String get actionSearch => 'Search';

  @override
  String get actionConfirm => 'Confirm';

  @override
  String get actionSubmit => 'Submit';

  @override
  String get actionSkip => 'Skip';

  @override
  String get actionSeeAll => 'See all';

  @override
  String get actionShowMore => 'Show more';

  @override
  String get actionShowLess => 'Show less';

  @override
  String get labelOptional => 'Optional';

  @override
  String get labelRequired => 'Required';

  @override
  String get labelYes => 'Yes';

  @override
  String get labelNo => 'No';

  @override
  String get labelLoading => 'Loading…';

  @override
  String get labelNone => 'None';

  @override
  String get errorGeneric => 'Something went wrong. Please try again.';

  @override
  String get errorOffline => 'No connection. Check your network and try again.';

  @override
  String get errorTimeout => 'The server took too long to answer. Try again.';

  @override
  String get errorServer =>
      'The server is having trouble. Try again in a moment.';

  @override
  String errorReference(String traceId) {
    return 'Reference $traceId';
  }

  @override
  String get authSignIn => 'Sign in';

  @override
  String get authSignUp => 'Create account';

  @override
  String get authSignOut => 'Sign out';

  @override
  String get authSignOutEverywhere => 'Sign out on all devices';

  @override
  String get authWelcomeTitle => 'Welcome back';

  @override
  String get authWelcomeSubtitle => 'Sign in to manage your bookings.';

  @override
  String get authCreateAccountTitle => 'Create your account';

  @override
  String get authCreateAccountSubtitle =>
      'You need one to book a car. Browsing is open to everyone.';

  @override
  String get authEmail => 'Email address';

  @override
  String get authPassword => 'Password';

  @override
  String get authNewPassword => 'New password';

  @override
  String get authCurrentPassword => 'Current password';

  @override
  String get authConfirmPassword => 'Confirm password';

  @override
  String get authFullName => 'Full name';

  @override
  String get authPhone => 'Phone number';

  @override
  String get authPhoneHint => '07XXXXXXXX';

  @override
  String get authDateOfBirth => 'Date of birth';

  @override
  String get authForeignNational => 'I am not a Jordanian national';

  @override
  String get authForeignNationalHelp =>
      'You will upload a passport instead of a national ID.';

  @override
  String authMinimumAge(int age) {
    return 'You must be at least $age to rent a car on Khadra.';
  }

  @override
  String get authForgotPassword => 'Forgot your password?';

  @override
  String get authForgotPasswordTitle => 'Reset your password';

  @override
  String get authForgotPasswordSubtitle =>
      'We will email you a link to set a new one.';

  @override
  String get authSendResetLink => 'Send the link';

  @override
  String get authResetSent =>
      'If that address has an account, a reset link is on its way.';

  @override
  String get authResetPasswordTitle => 'Choose a new password';

  @override
  String get authResetPasswordDone =>
      'Your password has been changed. Sign in with it.';

  @override
  String get authChangePassword => 'Change password';

  @override
  String get authChangePasswordDone => 'Your password has been changed.';

  @override
  String get authChangePasswordSignsOutOthers =>
      'Changing your password signs you out everywhere else';

  @override
  String get authChangePasswordSignsOutOthersBody =>
      'Every other phone or computer signed in to this account will have to sign in again. This one stays signed in.';

  @override
  String get authAlreadyHaveAccount => 'Already have an account?';

  @override
  String get authNoAccount => 'New to Khadra?';

  @override
  String get authPasswordRules =>
      'At least 8 characters, with a letter and a number.';

  @override
  String get authVerifyEmailTitle => 'Verify your email';

  @override
  String authVerifyEmailBody(String email) {
    return 'We sent a link to $email. Open it to finish setting up your account.';
  }

  @override
  String get authVerifyEmailWhy =>
      'Your booking updates go to this address, so it has to work before you can book.';

  @override
  String get authResendVerification => 'Send it again';

  @override
  String get authVerificationResent =>
      'Sent. Check your inbox, and your spam folder.';

  @override
  String get authVerifiedTitle => 'Email verified';

  @override
  String get authVerifiedBody => 'You can book a car now.';

  @override
  String get authEmailNotDelivered =>
      'We could not send that email just now. Try again in a moment.';

  @override
  String get authBrowseInstead => 'Look around first';

  @override
  String get authSignInToContinue => 'Sign in to continue';

  @override
  String get authSignInToBook => 'Sign in to book this car';

  @override
  String get authSessionExpired => 'Your session ended. Sign in again.';

  @override
  String get navBrowse => 'Browse';

  @override
  String get navBookings => 'Bookings';

  @override
  String get navNotifications => 'Alerts';

  @override
  String get navProfile => 'Profile';

  @override
  String get searchTitle => 'Find a car';

  @override
  String get searchHint => 'Make or model';

  @override
  String get searchFilters => 'Filters';

  @override
  String searchFiltersApplied(int count) {
    String _temp0 = intl.Intl.pluralLogic(
      count,
      locale: localeName,
      other: '$count filters',
      one: '1 filter',
      zero: 'No filters',
    );
    return '$_temp0';
  }

  @override
  String get searchCity => 'City';

  @override
  String get searchAnyCity => 'Any city';

  @override
  String get searchCarType => 'Car type';

  @override
  String get searchAnyCarType => 'Any type';

  @override
  String get searchTransmission => 'Transmission';

  @override
  String get searchAnyTransmission => 'Any';

  @override
  String get searchSeats => 'Seats';

  @override
  String searchMinimumSeats(int count) {
    return 'At least $count seats';
  }

  @override
  String get searchAnySeats => 'Any';

  @override
  String get searchPriceRange => 'Price per day';

  @override
  String get searchDeliveryOnly => 'Delivered to me only';

  @override
  String get searchDates => 'Dates';

  @override
  String get searchAnyDates => 'Any dates';

  @override
  String get searchPickup => 'Pick-up';

  @override
  String get searchReturn => 'Return';

  @override
  String get searchChooseDates => 'Choose your dates';

  @override
  String get searchDatesHelp =>
      'Choosing dates shows only the cars that are free, and lets us price the rental.';

  @override
  String get searchClearDates => 'Clear the dates';

  @override
  String searchResults(int count) {
    String _temp0 = intl.Intl.pluralLogic(
      count,
      locale: localeName,
      other: '$count cars',
      one: '1 car',
      zero: 'No cars',
    );
    return '$_temp0';
  }

  @override
  String get searchEmptyTitle => 'No cars match that';

  @override
  String get searchEmptyBody =>
      'Try widening the dates, the price, or the city.';

  @override
  String get searchEmptyNoListings =>
      'No cars are listed on Khadra yet. A rental office has to publish one before it can appear here.';

  @override
  String get searchLoadMore => 'Load more';

  @override
  String vehiclePerDay(String amount) {
    return '$amount / day';
  }

  @override
  String vehicleSeats(int count) {
    String _temp0 = intl.Intl.pluralLogic(
      count,
      locale: localeName,
      other: '$count seats',
      one: '1 seat',
    );
    return '$_temp0';
  }

  @override
  String get vehicleDeliveryAvailable => 'Delivery available';

  @override
  String get vehicleNoPhoto => 'No photo';

  @override
  String get vehicleSpecifications => 'Specifications';

  @override
  String get vehicleYear => 'Year';

  @override
  String get vehicleColour => 'Colour';

  @override
  String get vehicleFuel => 'Fuel';

  @override
  String get vehicleAbout => 'About this car';

  @override
  String get vehicleMileage => 'Mileage';

  @override
  String get vehicleMileageUnlimited => 'Unlimited mileage';

  @override
  String vehicleMileageLimited(int limit) {
    return '$limit km per day';
  }

  @override
  String vehicleMileageExcess(String amount) {
    return '$amount for every extra kilometre';
  }

  @override
  String get vehicleFuelPolicy => 'Fuel policy';

  @override
  String get vehicleFuelPolicyFullToFull =>
      'Return it with the same fuel you collected it with.';

  @override
  String get vehicleFuelPolicySameToSame =>
      'Return it with the same fuel you collected it with.';

  @override
  String get vehicleFuelPolicyPrepaid => 'Fuel is paid for in advance.';

  @override
  String get vehicleSecurityDeposit => 'Security deposit';

  @override
  String get vehicleSecurityDepositHelp =>
      'Held by the rental office, not by Khadra, and returned when the car comes back.';

  @override
  String get vehicleAvailableForDates => 'Free for your dates';

  @override
  String get vehicleUnavailableForDates => 'Not free for those dates';

  @override
  String get vehicleChooseDatesToBook => 'Choose dates to see the price';

  @override
  String get vehicleSeePrice => 'See the price';

  @override
  String get vehicleNotFoundTitle => 'This car is not available';

  @override
  String get vehicleNotFoundBody =>
      'It may have been taken off the platform, or the rental office is no longer trading.';

  @override
  String get galleryTitle => 'Rental office';

  @override
  String get galleryAbout => 'About';

  @override
  String get galleryOpeningHours => 'Opening hours';

  @override
  String get galleryClosed => 'Closed';

  @override
  String galleryOpenToday(String opens, String closes) {
    return 'Open today $opens – $closes';
  }

  @override
  String get galleryClosedToday => 'Closed today';

  @override
  String get galleryLocation => 'Where they are';

  @override
  String get galleryOpenInMaps => 'Open in maps';

  @override
  String get galleryDelivery => 'Delivery';

  @override
  String galleryDeliveryOffered(String radius, String fee) {
    return 'They deliver within $radius km for $fee.';
  }

  @override
  String get galleryDeliveryNotOffered =>
      'This office does not deliver. You collect the car from them.';

  @override
  String get bookDeliveryNotForThisCar =>
      'This car is not offered for delivery. You collect it from the office.';

  @override
  String get galleryCars => 'Their cars';

  @override
  String galleryRating(String rating) {
    return '$rating out of 5';
  }

  @override
  String galleryReviewCount(int count) {
    String _temp0 = intl.Intl.pluralLogic(
      count,
      locale: localeName,
      other: '$count reviews',
      one: '1 review',
      zero: 'No reviews yet',
    );
    return '$_temp0';
  }

  @override
  String get galleryNotRatedYet => 'Not rated yet';

  @override
  String get bookTitle => 'Your rental';

  @override
  String get bookPickupMethod => 'How will you get the car?';

  @override
  String get bookDeliveryLocation => 'Where should they bring it?';

  @override
  String get bookChooseOnMap => 'Choose the spot on the map';

  @override
  String get bookLocationChosen => 'Location chosen';

  @override
  String get bookLocationRequired =>
      'Choose where the car should be delivered.';

  @override
  String get bookUseMyLocation => 'Use my current location';

  @override
  String get bookPriceTitle => 'What it costs';

  @override
  String get bookDailyRate => 'Daily rate';

  @override
  String bookDays(int count) {
    String _temp0 = intl.Intl.pluralLogic(
      count,
      locale: localeName,
      other: '$count days',
      one: '1 day',
    );
    return '$_temp0';
  }

  @override
  String get bookRentalTotal => 'Rental';

  @override
  String get bookDeliveryFee => 'Delivery';

  @override
  String get bookTotal => 'Total';

  @override
  String bookDepositNow(String percent) {
    return 'Deposit ($percent)';
  }

  @override
  String get bookBalanceAtPickup => 'Cash to the office at pick-up';

  @override
  String get bookCalendarDaysNote =>
      'Rentals are billed by calendar day in Amman, so a day is counted whether you keep the car for an hour of it or all of it.';

  @override
  String get bookTermsTitle => 'What you are agreeing to';

  @override
  String bookTermsPayAfterApproval(String hours) {
    return 'Nothing is charged now. The office answers within $hours hours, and only then does the deposit fall due.';
  }

  @override
  String bookTermsPaymentWindow(String hours) {
    return 'Once they approve, you have $hours hours to pay the deposit or the booking ends and the car goes back on the market.';
  }

  @override
  String bookTermsFreeCancellation(String hours) {
    return 'Free cancellation for $hours hours after the deposit clears.';
  }

  @override
  String bookTermsCancellationPenalty(String percent) {
    return 'Cancelling after that is assessed at $percent of the deposit. Nothing is taken without a dispute being opened and settled.';
  }

  @override
  String get bookRequest => 'Request this car';

  @override
  String get bookRequesting => 'Sending your request…';

  @override
  String get bookDoneTitle => 'Request sent';

  @override
  String bookDoneBody(String gallery, String hours) {
    return '$gallery has your request and will answer within $hours hours. We will tell you as soon as they do.';
  }

  @override
  String bookDoneReference(String reference) {
    return 'Your reference is $reference.';
  }

  @override
  String get bookViewBooking => 'View the booking';

  @override
  String get bookDocumentsNeededTitle => 'Upload your documents first';

  @override
  String get bookDocumentsNeededBody =>
      'Jordanian law requires the rental office to check your driving licence and identity before handing over a car.';

  @override
  String get bookDocumentsNeededAction => 'Upload them now';

  @override
  String get bookVerifyEmailFirst =>
      'Verify your email address before booking. Your booking updates go there.';

  @override
  String get bookingsTitle => 'My bookings';

  @override
  String get bookingsTabAll => 'All';

  @override
  String get bookingsTabPending => 'Waiting';

  @override
  String get bookingsTabUpcoming => 'Upcoming';

  @override
  String get bookingsTabActive => 'Out now';

  @override
  String get bookingsTabReturned => 'Returned';

  @override
  String get bookingsTabCompleted => 'Finished';

  @override
  String get bookingsTabClosed => 'Closed';

  @override
  String get bookingsTabDisputed => 'Disputed';

  @override
  String get bookingsEmptyTitle => 'Nothing here yet';

  @override
  String get bookingsEmptyBody => 'Bookings you make will show up here.';

  @override
  String get bookingsEmptyAction => 'Find a car';

  @override
  String get bookingsSignedOutTitle => 'Sign in to see your bookings';

  @override
  String get bookingsSignedOutBody =>
      'Your bookings, documents and alerts live in your account.';

  @override
  String get statusRequested => 'Waiting for the office';

  @override
  String get statusApproved => 'Approved — deposit due';

  @override
  String get statusConfirmed => 'Confirmed';

  @override
  String get statusPickedUp => 'Out on rental';

  @override
  String get statusReturned => 'Returned';

  @override
  String get statusCompleted => 'Finished';

  @override
  String get statusCancelled => 'Cancelled';

  @override
  String get statusRejected => 'Declined';

  @override
  String get statusNoShow => 'Not collected';

  @override
  String get statusExpired => 'Expired';

  @override
  String get bookingReference => 'Reference';

  @override
  String get bookingWhen => 'When';

  @override
  String get bookingWhere => 'Pick-up';

  @override
  String get bookingWhereDelivery => 'Delivered to you';

  @override
  String get bookingWhereSelfPickup => 'You collect it';

  @override
  String get bookingGallery => 'Rental office';

  @override
  String get bookingCar => 'Car';

  @override
  String get bookingPlate => 'Plate';

  @override
  String get bookingHistory => 'What has happened';

  @override
  String get bookingPrice => 'Price';

  @override
  String get bookingTermsFrozen =>
      'These are the terms this booking was made under. Changing a setting today never re-prices a booking already made.';

  @override
  String bookingAwaitingDecisionTitle(String gallery) {
    return 'Waiting on $gallery';
  }

  @override
  String bookingAwaitingDecisionBody(String deadline) {
    return 'They have until $deadline to answer. The car is held for you until then.';
  }

  @override
  String bookingAwaitingPaymentTitle(String amount) {
    return 'Deposit of $amount is due';
  }

  @override
  String bookingAwaitingPaymentBy(String deadline) {
    return 'Due by $deadline';
  }

  @override
  String get bookingPaymentNotAvailableTitle =>
      'Paying in the app is not available yet';

  @override
  String get bookingPaymentNotAvailableBody =>
      'Khadra cannot take card payments in this version. This booking will end at the deadline above and the car will go back on the market. Nothing is owed when that happens.';

  @override
  String get bookingExpiredTitle => 'This booking ran out of time';

  @override
  String get bookingExpiredBody =>
      'Nothing is owed. The car went back on the market.';

  @override
  String get bookingRejectedTitle => 'The office declined this request';

  @override
  String get bookingCancelledTitle => 'This booking was cancelled';

  @override
  String bookingCancelledBy(String party) {
    return 'Cancelled by $party';
  }

  @override
  String get bookingNoShowTitle => 'The car was not collected';

  @override
  String get bookingCompletedTitle => 'This rental is finished';

  @override
  String bookingPenaltyAssessed(String amount, String party) {
    return 'An amount of $amount has been assessed against $party.';
  }

  @override
  String bookingPenaltyRange(String min, String max, String party) {
    return 'Between $min and $max has been assessed against $party.';
  }

  @override
  String get bookingPenaltyNotCharged =>
      'Nothing has been charged. An assessment only becomes money if a dispute is opened and Khadra settles it.';

  @override
  String get bookingPartyCustomer => 'you';

  @override
  String get bookingPartyDealer => 'the rental office';

  @override
  String get bookingPartyAdmin => 'Khadra';

  @override
  String get bookingPartySystem => 'Khadra';

  @override
  String get bookingPartyUnattributed => 'nobody';

  @override
  String get bookingHandoverPickup => 'Collected';

  @override
  String get bookingHandoverReturn => 'Returned';

  @override
  String bookingOdometer(String km) {
    return 'Odometer $km km';
  }

  @override
  String bookingFuelLevel(String percent) {
    return 'Fuel $percent%';
  }

  @override
  String bookingCashCollected(String amount) {
    return 'Cash taken: $amount';
  }

  @override
  String bookingCountdownDays(int days, int hours) {
    return '${days}d ${hours}h left';
  }

  @override
  String bookingCountdownHours(int hours, int minutes) {
    return '${hours}h ${minutes}m left';
  }

  @override
  String bookingCountdownMinutes(int minutes) {
    return '${minutes}m left';
  }

  @override
  String get bookingCountdownOver => 'The time has run out';

  @override
  String get cancelTitle => 'Cancel this booking';

  @override
  String get cancelReasonQuestion => 'Why are you cancelling?';

  @override
  String get cancelDetailsLabel => 'Anything you want to add';

  @override
  String get cancelDetailsHint => 'The rental office will see this.';

  @override
  String get cancelFreeNotice => 'Cancelling now costs you nothing.';

  @override
  String cancelPenaltyNotice(String amount) {
    return 'Cancelling now assesses $amount against you. Nothing is charged unless a dispute is opened and settled.';
  }

  @override
  String get cancelConfirm => 'Cancel the booking';

  @override
  String get cancelKeep => 'Keep it';

  @override
  String get cancelDone => 'Your booking has been cancelled.';

  @override
  String get cancelExpiredInstead =>
      'The time on this booking had already run out, so it ended on its own. Nothing is owed.';

  @override
  String get cancelNotPossible => 'This booking can no longer be cancelled.';

  @override
  String get nonDeliveryTitle => 'The car was never handed over';

  @override
  String get nonDeliveryBody =>
      'Tell us what happened. The rental office will be asked to answer, and Khadra will look at it if you open a dispute afterwards.';

  @override
  String get nonDeliveryDetails => 'What happened';

  @override
  String get nonDeliveryTooEarly =>
      'It is too soon to report this. Give the rental office the short grace period it is allowed after the agreed time.';

  @override
  String nonDeliveryNotYet(String from) {
    return 'You can report this from $from, once the rental office has had the grace period it is allowed.';
  }

  @override
  String get nonDeliveryReport => 'Report it';

  @override
  String get nonDeliveryDone =>
      'Reported. The booking is closed and the rental office has been told.';

  @override
  String get disputeTitle => 'Open a dispute';

  @override
  String get disputeBody =>
      'Tell us what went wrong. The rental office can answer, and Khadra decides.';

  @override
  String get disputeReason => 'What went wrong';

  @override
  String get disputeEvidence => 'Photos or documents';

  @override
  String disputeEvidenceNumbered(int number) {
    return 'Photo $number';
  }

  @override
  String get disputeAddEvidence => 'Add a photo';

  @override
  String get disputeOpen => 'Open the dispute';

  @override
  String disputeOpened(String hours) {
    return 'Your dispute is open. Khadra will look at it within $hours hours.';
  }

  @override
  String get disputeViewTitle => 'Dispute';

  @override
  String get disputeStatements => 'The conversation';

  @override
  String get disputeAddStatement => 'Add something';

  @override
  String get disputeStatementHint => 'Anything that helps';

  @override
  String get disputeWithdraw => 'Withdraw the dispute';

  @override
  String get disputeWithdrawConfirm =>
      'Withdraw this? The rental office will be told, and nothing will be charged to anyone.';

  @override
  String get disputeWithdrawn => 'Withdrawn. Nothing has been charged.';

  @override
  String get disputeStatusOpen => 'Open';

  @override
  String get disputeStatusUnderReview => 'Being looked at';

  @override
  String get disputeStatusResolved => 'Settled';

  @override
  String get disputeStatusWithdrawn => 'Withdrawn';

  @override
  String get disputeYou => 'You';

  @override
  String get disputeGallery => 'The rental office';

  @override
  String get disputeKhadra => 'Khadra';

  @override
  String get disputeCannotOpen =>
      'A dispute can only be opened while the settlement window is open.';

  @override
  String get disputeExisting =>
      'You already have a dispute open on this booking.';

  @override
  String get disputeView => 'View the dispute';

  @override
  String get reviewTitle => 'Rate this rental';

  @override
  String reviewQuestion(String gallery) {
    return 'How was $gallery?';
  }

  @override
  String get reviewRatingLabel => 'Your rating';

  @override
  String reviewStars(int count) {
    String _temp0 = intl.Intl.pluralLogic(
      count,
      locale: localeName,
      other: '$count stars',
      one: '1 star',
    );
    return '$_temp0';
  }

  @override
  String get reviewCommentLabel => 'Anything to add';

  @override
  String get reviewCommentHint => 'What other renters should know.';

  @override
  String get reviewSubmit => 'Post the rating';

  @override
  String get reviewThanks => 'Thanks. Your rating is on their page now.';

  @override
  String get reviewYours => 'Your rating';

  @override
  String get reviewOnlyWhenFinished =>
      'You can rate a rental once it is finished.';

  @override
  String get reviewAlreadyLeft => 'You have already rated this rental.';

  @override
  String get reviewsTitle => 'Reviews';

  @override
  String get reviewsEmpty => 'Nobody has rated this office yet.';

  @override
  String get reviewHidden =>
      'This comment was removed by Khadra. The rating still counts.';

  @override
  String get reviewsRatingIsOfficeNote =>
      'Khadra rates rental offices, not individual cars.';

  @override
  String get documentsTitle => 'My documents';

  @override
  String get documentsIntro =>
      'The rental office has to see your driving licence and your identity before handing over a car. Upload them once and every booking can use them.';

  @override
  String get documentsDrivingLicenceFront => 'Driving licence — front';

  @override
  String get documentsDrivingLicenceBack => 'Driving licence — back';

  @override
  String get documentsNationalId => 'National ID';

  @override
  String get documentsPassport => 'Passport';

  @override
  String get documentsUpload => 'Upload';

  @override
  String get documentsReplace => 'Replace';

  @override
  String get documentsView => 'View';

  @override
  String get documentsTakePhoto => 'Take a photo';

  @override
  String get documentsChooseFile => 'Choose a file';

  @override
  String get documentsUploading => 'Uploading…';

  @override
  String documentsUploaded(String when) {
    return 'Uploaded $when';
  }

  @override
  String get documentsStatusPendingReview => 'Waiting to be checked';

  @override
  String get documentsStatusVerified => 'Checked';

  @override
  String get documentsStatusRejected => 'Not accepted';

  @override
  String get documentsMissing => 'Still needed';

  @override
  String get documentsBadgeComplete => 'Complete';

  @override
  String get documentsBadgeMissing => 'Needed';

  @override
  String get documentsComplete => 'You have everything you need to book.';

  @override
  String get documentsIncomplete =>
      'Upload the missing documents before you can book.';

  @override
  String get documentsNotYetCheckedTitle => 'Nobody has checked these yet';

  @override
  String get documentsNotYetCheckedBody =>
      'Khadra does not verify documents in this version. The rental office checks them in person when you collect the car.';

  @override
  String documentsTooLarge(String size) {
    return 'That file is larger than $size.';
  }

  @override
  String documentsWrongType(String types) {
    return 'That file type is not accepted. Use $types.';
  }

  @override
  String get documentsCameraNote =>
      'Take the photo in good light with the whole document in frame.';

  @override
  String get profileTitle => 'Profile';

  @override
  String profileSignedInAs(String email) {
    return 'Signed in as $email';
  }

  @override
  String get profilePersonalDetails => 'Your details';

  @override
  String get profileEdit => 'Edit your details';

  @override
  String get profileEmailFixed =>
      'Your email address cannot be changed here. It is how you sign in and where a password reset is sent, so changing it needs its own verified step, which Khadra has not built yet.';

  @override
  String get profileSaved => 'Saved.';

  @override
  String get profileSecurity => 'Security';

  @override
  String get profileSessions => 'Where you are signed in';

  @override
  String get profileSessionsEmpty => 'No other devices.';

  @override
  String get profileSessionThis => 'This device';

  @override
  String profileSessionLastUsed(String when) {
    return 'Last used $when';
  }

  @override
  String get profileSessionRevoke => 'Sign out';

  @override
  String get profileSessionRevoked => 'That device has been signed out.';

  @override
  String get profileLanguage => 'Language';

  @override
  String get profileLanguageEnglish => 'English';

  @override
  String get profileLanguageArabic => 'العربية';

  @override
  String get profileAbout => 'About Khadra';

  @override
  String get profileAboutBody =>
      'Khadra connects renters with licensed rental offices in Jordan. Every office on the platform holds a green plate licence and is checked before it can list a car.';

  @override
  String profileVersion(String version) {
    return 'Version $version';
  }

  @override
  String get profileSignOutConfirm => 'Sign out of Khadra?';

  @override
  String get profileSignOutEverywhereConfirm =>
      'Sign out on every device you have used?';

  @override
  String profileMemberSince(String when) {
    return 'With Khadra since $when';
  }

  @override
  String get profileEmailUnverified => 'Your email is not verified yet';

  @override
  String get notificationsTitle => 'Alerts';

  @override
  String get notificationsMarkAllRead => 'Mark all as read';

  @override
  String get notificationsEmptyTitle => 'Nothing to tell you';

  @override
  String get notificationsEmptyBody =>
      'We will let you know when a rental office answers one of your bookings.';

  @override
  String notificationsUnread(int count) {
    String _temp0 = intl.Intl.pluralLogic(
      count,
      locale: localeName,
      other: '$count unread',
      one: '1 unread',
    );
    return '$_temp0';
  }

  @override
  String notificationYourBookingApproved(String actor) {
    return '$actor approved your booking';
  }

  @override
  String notificationYourBookingRejected(String actor) {
    return '$actor declined your booking';
  }

  @override
  String notificationYourBookingExpired(String actor) {
    return 'Your booking with $actor ran out of time';
  }

  @override
  String notificationYourBookingCompleted(String actor) {
    return 'Your rental with $actor is finished';
  }

  @override
  String notificationYourBookingMarkedNoShow(String actor) {
    return '$actor recorded that the car was not collected';
  }

  @override
  String notificationUnknown(String actor) {
    return '$actor updated something on your account';
  }

  @override
  String notificationAboutBooking(String reference) {
    return 'Booking $reference';
  }

  @override
  String get reasonPlansChanged => 'My plans changed';

  @override
  String get reasonFoundBetterPrice => 'I found a better price';

  @override
  String get reasonTravelCancelled => 'My trip was cancelled';

  @override
  String get reasonBookedByMistake => 'I booked this by mistake';

  @override
  String get reasonDealerUnresponsive => 'The rental office did not respond';

  @override
  String get reasonOther => 'Another reason';

  @override
  String get timeJustNow => 'just now';

  @override
  String timeMinutesAgo(int count) {
    String _temp0 = intl.Intl.pluralLogic(
      count,
      locale: localeName,
      other: '$count minutes ago',
      one: 'a minute ago',
    );
    return '$_temp0';
  }

  @override
  String timeHoursAgo(int count) {
    String _temp0 = intl.Intl.pluralLogic(
      count,
      locale: localeName,
      other: '$count hours ago',
      one: 'an hour ago',
    );
    return '$_temp0';
  }

  @override
  String timeDaysAgo(int count) {
    String _temp0 = intl.Intl.pluralLogic(
      count,
      locale: localeName,
      other: '$count days ago',
      one: 'yesterday',
    );
    return '$_temp0';
  }

  @override
  String get timeAmmanNote => 'Times are Amman time.';

  @override
  String get validationRequired => 'This is needed.';

  @override
  String get validationEmail => 'That does not look like an email address.';

  @override
  String get validationPhone =>
      'Enter a Jordanian mobile number, like 0791234567.';

  @override
  String get validationPasswordShort => 'Use at least 8 characters.';

  @override
  String get validationPasswordMatch => 'The two passwords do not match.';

  @override
  String get validationDateOfBirth => 'Enter your date of birth.';

  @override
  String validationTooLong(int max) {
    return 'That is longer than $max characters.';
  }

  @override
  String get validationReturnAfterPickup =>
      'The return has to come after the pick-up.';

  @override
  String get validationChooseReason => 'Choose a reason.';

  @override
  String get errorAuthInvalidCredentials =>
      'That email and password do not match an account.';

  @override
  String get errorAuthEmailTaken =>
      'There is already an account on that email address.';

  @override
  String get errorAuthPhoneTaken =>
      'That phone number is already on another account.';

  @override
  String get errorAuthInvalidPhone =>
      'Enter a Jordanian mobile number, like 0791234567.';

  @override
  String get errorAuthInvalidEmail =>
      'That does not look like an email address.';

  @override
  String get errorAuthWeakPassword =>
      'Choose a longer password with a letter and a number.';

  @override
  String get errorAuthAccountSuspended =>
      'This account has been suspended. Contact Khadra.';

  @override
  String get errorAuthEmailNotVerified => 'Verify your email address first.';

  @override
  String get errorAuthUnderage =>
      'You are not old enough to rent a car on Khadra.';

  @override
  String get errorAuthInvalidToken =>
      'That link is no longer valid. Ask for a new one.';

  @override
  String get errorAuthTooManyAttempts =>
      'Too many attempts. Wait a few minutes and try again.';

  @override
  String get errorBookingVehicleUnavailable =>
      'Somebody took that car while you were deciding. Try different dates.';

  @override
  String get errorBookingDocumentsIncomplete =>
      'Upload your driving licence and identity document before booking.';

  @override
  String get errorBookingEmailNotVerified =>
      'Verify your email address before booking.';

  @override
  String get errorBookingPeriodInPast => 'Choose dates in the future.';

  @override
  String get errorBookingNotFound => 'That booking was not found.';

  @override
  String get errorBookingCannotCancel =>
      'This booking can no longer be cancelled.';

  @override
  String get errorRateLimited =>
      'Too many requests. Wait a moment and try again.';
}
