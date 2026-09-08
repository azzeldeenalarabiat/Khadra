import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter/widgets.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:intl/intl.dart' as intl;

import 'app_localizations_ar.dart';
import 'app_localizations_en.dart';

// ignore_for_file: type=lint

/// Callers can lookup localized strings with an instance of AppLocalizations
/// returned by `AppLocalizations.of(context)`.
///
/// Applications need to include `AppLocalizations.delegate()` in their app's
/// `localizationDelegates` list, and the locales they support in the app's
/// `supportedLocales` list. For example:
///
/// ```dart
/// import 'l10n/app_localizations.dart';
///
/// return MaterialApp(
///   localizationsDelegates: AppLocalizations.localizationsDelegates,
///   supportedLocales: AppLocalizations.supportedLocales,
///   home: MyApplicationHome(),
/// );
/// ```
///
/// ## Update pubspec.yaml
///
/// Please make sure to update your pubspec.yaml to include the following
/// packages:
///
/// ```yaml
/// dependencies:
///   # Internationalization support.
///   flutter_localizations:
///     sdk: flutter
///   intl: any # Use the pinned version from flutter_localizations
///
///   # Rest of dependencies
/// ```
///
/// ## iOS Applications
///
/// iOS applications define key application metadata, including supported
/// locales, in an Info.plist file that is built into the application bundle.
/// To configure the locales supported by your app, you’ll need to edit this
/// file.
///
/// First, open your project’s ios/Runner.xcworkspace Xcode workspace file.
/// Then, in the Project Navigator, open the Info.plist file under the Runner
/// project’s Runner folder.
///
/// Next, select the Information Property List item, select Add Item from the
/// Editor menu, then select Localizations from the pop-up menu.
///
/// Select and expand the newly-created Localizations item then, for each
/// locale your application supports, add a new item and select the locale
/// you wish to add from the pop-up menu in the Value field. This list should
/// be consistent with the languages listed in the AppLocalizations.supportedLocales
/// property.
abstract class AppLocalizations {
  AppLocalizations(String locale)
    : localeName = intl.Intl.canonicalizedLocale(locale.toString());

  final String localeName;

  static AppLocalizations of(BuildContext context) {
    return Localizations.of<AppLocalizations>(context, AppLocalizations)!;
  }

  static const LocalizationsDelegate<AppLocalizations> delegate =
      _AppLocalizationsDelegate();

  /// A list of this localizations delegate along with the default localizations
  /// delegates.
  ///
  /// Returns a list of localizations delegates containing this delegate along with
  /// GlobalMaterialLocalizations.delegate, GlobalCupertinoLocalizations.delegate,
  /// and GlobalWidgetsLocalizations.delegate.
  ///
  /// Additional delegates can be added by appending to this list in
  /// MaterialApp. This list does not have to be used at all if a custom list
  /// of delegates is preferred or required.
  static const List<LocalizationsDelegate<dynamic>> localizationsDelegates =
      <LocalizationsDelegate<dynamic>>[
        delegate,
        GlobalMaterialLocalizations.delegate,
        GlobalCupertinoLocalizations.delegate,
        GlobalWidgetsLocalizations.delegate,
      ];

  /// A list of this localizations delegate's supported locales.
  static const List<Locale> supportedLocales = <Locale>[
    Locale('ar'),
    Locale('en'),
  ];

  /// No description provided for @appName.
  ///
  /// In en, this message translates to:
  /// **'Khadra'**
  String get appName;

  /// No description provided for @appTagline.
  ///
  /// In en, this message translates to:
  /// **'Rent a car in Jordan'**
  String get appTagline;

  /// No description provided for @actionRetry.
  ///
  /// In en, this message translates to:
  /// **'Try again'**
  String get actionRetry;

  /// No description provided for @actionCancel.
  ///
  /// In en, this message translates to:
  /// **'Cancel'**
  String get actionCancel;

  /// No description provided for @actionClose.
  ///
  /// In en, this message translates to:
  /// **'Close'**
  String get actionClose;

  /// No description provided for @actionSave.
  ///
  /// In en, this message translates to:
  /// **'Save'**
  String get actionSave;

  /// No description provided for @actionSaveChanges.
  ///
  /// In en, this message translates to:
  /// **'Save changes'**
  String get actionSaveChanges;

  /// No description provided for @actionContinue.
  ///
  /// In en, this message translates to:
  /// **'Continue'**
  String get actionContinue;

  /// No description provided for @actionBack.
  ///
  /// In en, this message translates to:
  /// **'Back'**
  String get actionBack;

  /// No description provided for @actionDone.
  ///
  /// In en, this message translates to:
  /// **'Done'**
  String get actionDone;

  /// No description provided for @actionApply.
  ///
  /// In en, this message translates to:
  /// **'Apply'**
  String get actionApply;

  /// No description provided for @actionClearAll.
  ///
  /// In en, this message translates to:
  /// **'Clear all'**
  String get actionClearAll;

  /// No description provided for @actionSearch.
  ///
  /// In en, this message translates to:
  /// **'Search'**
  String get actionSearch;

  /// No description provided for @actionConfirm.
  ///
  /// In en, this message translates to:
  /// **'Confirm'**
  String get actionConfirm;

  /// No description provided for @actionSubmit.
  ///
  /// In en, this message translates to:
  /// **'Submit'**
  String get actionSubmit;

  /// No description provided for @actionSkip.
  ///
  /// In en, this message translates to:
  /// **'Skip'**
  String get actionSkip;

  /// No description provided for @actionSeeAll.
  ///
  /// In en, this message translates to:
  /// **'See all'**
  String get actionSeeAll;

  /// No description provided for @actionShowMore.
  ///
  /// In en, this message translates to:
  /// **'Show more'**
  String get actionShowMore;

  /// No description provided for @actionShowLess.
  ///
  /// In en, this message translates to:
  /// **'Show less'**
  String get actionShowLess;

  /// No description provided for @labelOptional.
  ///
  /// In en, this message translates to:
  /// **'Optional'**
  String get labelOptional;

  /// No description provided for @labelRequired.
  ///
  /// In en, this message translates to:
  /// **'Required'**
  String get labelRequired;

  /// No description provided for @labelYes.
  ///
  /// In en, this message translates to:
  /// **'Yes'**
  String get labelYes;

  /// No description provided for @labelNo.
  ///
  /// In en, this message translates to:
  /// **'No'**
  String get labelNo;

  /// No description provided for @labelLoading.
  ///
  /// In en, this message translates to:
  /// **'Loading…'**
  String get labelLoading;

  /// No description provided for @labelNone.
  ///
  /// In en, this message translates to:
  /// **'None'**
  String get labelNone;

  /// No description provided for @errorGeneric.
  ///
  /// In en, this message translates to:
  /// **'Something went wrong. Please try again.'**
  String get errorGeneric;

  /// No description provided for @errorOffline.
  ///
  /// In en, this message translates to:
  /// **'No connection. Check your network and try again.'**
  String get errorOffline;

  /// No description provided for @errorTimeout.
  ///
  /// In en, this message translates to:
  /// **'The server took too long to answer. Try again.'**
  String get errorTimeout;

  /// No description provided for @errorServer.
  ///
  /// In en, this message translates to:
  /// **'The server is having trouble. Try again in a moment.'**
  String get errorServer;

  /// Shown under an error the app has no specific message for, so support can find the request.
  ///
  /// In en, this message translates to:
  /// **'Reference {traceId}'**
  String errorReference(String traceId);

  /// No description provided for @authSignIn.
  ///
  /// In en, this message translates to:
  /// **'Sign in'**
  String get authSignIn;

  /// No description provided for @authSignUp.
  ///
  /// In en, this message translates to:
  /// **'Create account'**
  String get authSignUp;

  /// No description provided for @authSignOut.
  ///
  /// In en, this message translates to:
  /// **'Sign out'**
  String get authSignOut;

  /// No description provided for @authSignOutEverywhere.
  ///
  /// In en, this message translates to:
  /// **'Sign out on all devices'**
  String get authSignOutEverywhere;

  /// No description provided for @authWelcomeTitle.
  ///
  /// In en, this message translates to:
  /// **'Welcome back'**
  String get authWelcomeTitle;

  /// No description provided for @authWelcomeSubtitle.
  ///
  /// In en, this message translates to:
  /// **'Sign in to manage your bookings.'**
  String get authWelcomeSubtitle;

  /// No description provided for @authCreateAccountTitle.
  ///
  /// In en, this message translates to:
  /// **'Create your account'**
  String get authCreateAccountTitle;

  /// No description provided for @authCreateAccountSubtitle.
  ///
  /// In en, this message translates to:
  /// **'You need one to book a car. Browsing is open to everyone.'**
  String get authCreateAccountSubtitle;

  /// No description provided for @authEmail.
  ///
  /// In en, this message translates to:
  /// **'Email address'**
  String get authEmail;

  /// No description provided for @authPassword.
  ///
  /// In en, this message translates to:
  /// **'Password'**
  String get authPassword;

  /// No description provided for @authNewPassword.
  ///
  /// In en, this message translates to:
  /// **'New password'**
  String get authNewPassword;

  /// No description provided for @authCurrentPassword.
  ///
  /// In en, this message translates to:
  /// **'Current password'**
  String get authCurrentPassword;

  /// No description provided for @authConfirmPassword.
  ///
  /// In en, this message translates to:
  /// **'Confirm password'**
  String get authConfirmPassword;

  /// No description provided for @authFullName.
  ///
  /// In en, this message translates to:
  /// **'Full name'**
  String get authFullName;

  /// No description provided for @authPhone.
  ///
  /// In en, this message translates to:
  /// **'Phone number'**
  String get authPhone;

  /// No description provided for @authPhoneHint.
  ///
  /// In en, this message translates to:
  /// **'07XXXXXXXX'**
  String get authPhoneHint;

  /// No description provided for @authDateOfBirth.
  ///
  /// In en, this message translates to:
  /// **'Date of birth'**
  String get authDateOfBirth;

  /// No description provided for @authForeignNational.
  ///
  /// In en, this message translates to:
  /// **'I am not a Jordanian national'**
  String get authForeignNational;

  /// No description provided for @authForeignNationalHelp.
  ///
  /// In en, this message translates to:
  /// **'You will upload a passport instead of a national ID.'**
  String get authForeignNationalHelp;

  /// The age comes from /app-config, never from a number typed into the app.
  ///
  /// In en, this message translates to:
  /// **'You must be at least {age} to rent a car on Khadra.'**
  String authMinimumAge(int age);

  /// No description provided for @authForgotPassword.
  ///
  /// In en, this message translates to:
  /// **'Forgot your password?'**
  String get authForgotPassword;

  /// No description provided for @authForgotPasswordTitle.
  ///
  /// In en, this message translates to:
  /// **'Reset your password'**
  String get authForgotPasswordTitle;

  /// No description provided for @authForgotPasswordSubtitle.
  ///
  /// In en, this message translates to:
  /// **'We will email you a link to set a new one.'**
  String get authForgotPasswordSubtitle;

  /// No description provided for @authSendResetLink.
  ///
  /// In en, this message translates to:
  /// **'Send the link'**
  String get authSendResetLink;

  /// No description provided for @authResetSent.
  ///
  /// In en, this message translates to:
  /// **'If that address has an account, a reset link is on its way.'**
  String get authResetSent;

  /// No description provided for @authResetPasswordTitle.
  ///
  /// In en, this message translates to:
  /// **'Choose a new password'**
  String get authResetPasswordTitle;

  /// No description provided for @authResetPasswordDone.
  ///
  /// In en, this message translates to:
  /// **'Your password has been changed. Sign in with it.'**
  String get authResetPasswordDone;

  /// No description provided for @authChangePassword.
  ///
  /// In en, this message translates to:
  /// **'Change password'**
  String get authChangePassword;

  /// No description provided for @authChangePasswordDone.
  ///
  /// In en, this message translates to:
  /// **'Your password has been changed.'**
  String get authChangePasswordDone;

  /// No description provided for @authChangePasswordSignsOutOthers.
  ///
  /// In en, this message translates to:
  /// **'Changing your password signs you out everywhere else'**
  String get authChangePasswordSignsOutOthers;

  /// No description provided for @authChangePasswordSignsOutOthersBody.
  ///
  /// In en, this message translates to:
  /// **'Every other phone or computer signed in to this account will have to sign in again. This one stays signed in.'**
  String get authChangePasswordSignsOutOthersBody;

  /// No description provided for @authAlreadyHaveAccount.
  ///
  /// In en, this message translates to:
  /// **'Already have an account?'**
  String get authAlreadyHaveAccount;

  /// No description provided for @authNoAccount.
  ///
  /// In en, this message translates to:
  /// **'New to Khadra?'**
  String get authNoAccount;

  /// No description provided for @authPasswordRules.
  ///
  /// In en, this message translates to:
  /// **'At least 8 characters, with a letter and a number.'**
  String get authPasswordRules;

  /// No description provided for @authVerifyEmailTitle.
  ///
  /// In en, this message translates to:
  /// **'Verify your email'**
  String get authVerifyEmailTitle;

  /// No description provided for @authVerifyEmailBody.
  ///
  /// In en, this message translates to:
  /// **'We sent a link to {email}. Open it to finish setting up your account.'**
  String authVerifyEmailBody(String email);

  /// No description provided for @authVerifyEmailWhy.
  ///
  /// In en, this message translates to:
  /// **'Your booking updates go to this address, so it has to work before you can book.'**
  String get authVerifyEmailWhy;

  /// No description provided for @authResendVerification.
  ///
  /// In en, this message translates to:
  /// **'Send it again'**
  String get authResendVerification;

  /// No description provided for @authVerificationResent.
  ///
  /// In en, this message translates to:
  /// **'Sent. Check your inbox, and your spam folder.'**
  String get authVerificationResent;

  /// No description provided for @authVerifiedTitle.
  ///
  /// In en, this message translates to:
  /// **'Email verified'**
  String get authVerifiedTitle;

  /// No description provided for @authVerifiedBody.
  ///
  /// In en, this message translates to:
  /// **'You can book a car now.'**
  String get authVerifiedBody;

  /// No description provided for @authEmailNotDelivered.
  ///
  /// In en, this message translates to:
  /// **'We could not send that email just now. Try again in a moment.'**
  String get authEmailNotDelivered;

  /// No description provided for @authBrowseInstead.
  ///
  /// In en, this message translates to:
  /// **'Look around first'**
  String get authBrowseInstead;

  /// No description provided for @authSignInToContinue.
  ///
  /// In en, this message translates to:
  /// **'Sign in to continue'**
  String get authSignInToContinue;

  /// No description provided for @authSignInToBook.
  ///
  /// In en, this message translates to:
  /// **'Sign in to book this car'**
  String get authSignInToBook;

  /// No description provided for @authSessionExpired.
  ///
  /// In en, this message translates to:
  /// **'Your session ended. Sign in again.'**
  String get authSessionExpired;

  /// No description provided for @navBrowse.
  ///
  /// In en, this message translates to:
  /// **'Browse'**
  String get navBrowse;

  /// No description provided for @navBookings.
  ///
  /// In en, this message translates to:
  /// **'Bookings'**
  String get navBookings;

  /// No description provided for @navNotifications.
  ///
  /// In en, this message translates to:
  /// **'Alerts'**
  String get navNotifications;

  /// No description provided for @navProfile.
  ///
  /// In en, this message translates to:
  /// **'Profile'**
  String get navProfile;

  /// No description provided for @searchTitle.
  ///
  /// In en, this message translates to:
  /// **'Find a car'**
  String get searchTitle;

  /// No description provided for @searchHint.
  ///
  /// In en, this message translates to:
  /// **'Make or model'**
  String get searchHint;

  /// No description provided for @searchFilters.
  ///
  /// In en, this message translates to:
  /// **'Filters'**
  String get searchFilters;

  /// No description provided for @searchFiltersApplied.
  ///
  /// In en, this message translates to:
  /// **'{count, plural, =0{No filters} =1{1 filter} other{{count} filters}}'**
  String searchFiltersApplied(int count);

  /// No description provided for @searchCity.
  ///
  /// In en, this message translates to:
  /// **'City'**
  String get searchCity;

  /// No description provided for @searchAnyCity.
  ///
  /// In en, this message translates to:
  /// **'Any city'**
  String get searchAnyCity;

  /// No description provided for @searchCarType.
  ///
  /// In en, this message translates to:
  /// **'Car type'**
  String get searchCarType;

  /// No description provided for @searchAnyCarType.
  ///
  /// In en, this message translates to:
  /// **'Any type'**
  String get searchAnyCarType;

  /// No description provided for @searchTransmission.
  ///
  /// In en, this message translates to:
  /// **'Transmission'**
  String get searchTransmission;

  /// No description provided for @searchAnyTransmission.
  ///
  /// In en, this message translates to:
  /// **'Any'**
  String get searchAnyTransmission;

  /// No description provided for @searchSeats.
  ///
  /// In en, this message translates to:
  /// **'Seats'**
  String get searchSeats;

  /// No description provided for @searchMinimumSeats.
  ///
  /// In en, this message translates to:
  /// **'At least {count} seats'**
  String searchMinimumSeats(int count);

  /// No description provided for @searchAnySeats.
  ///
  /// In en, this message translates to:
  /// **'Any'**
  String get searchAnySeats;

  /// No description provided for @searchPriceRange.
  ///
  /// In en, this message translates to:
  /// **'Price per day'**
  String get searchPriceRange;

  /// No description provided for @searchDeliveryOnly.
  ///
  /// In en, this message translates to:
  /// **'Delivered to me only'**
  String get searchDeliveryOnly;

  /// No description provided for @searchDates.
  ///
  /// In en, this message translates to:
  /// **'Dates'**
  String get searchDates;

  /// No description provided for @searchAnyDates.
  ///
  /// In en, this message translates to:
  /// **'Any dates'**
  String get searchAnyDates;

  /// No description provided for @searchPickup.
  ///
  /// In en, this message translates to:
  /// **'Pick-up'**
  String get searchPickup;

  /// No description provided for @searchReturn.
  ///
  /// In en, this message translates to:
  /// **'Return'**
  String get searchReturn;

  /// No description provided for @searchChooseDates.
  ///
  /// In en, this message translates to:
  /// **'Choose your dates'**
  String get searchChooseDates;

  /// No description provided for @searchDatesHelp.
  ///
  /// In en, this message translates to:
  /// **'Choosing dates shows only the cars that are free, and lets us price the rental.'**
  String get searchDatesHelp;

  /// No description provided for @searchClearDates.
  ///
  /// In en, this message translates to:
  /// **'Clear the dates'**
  String get searchClearDates;

  /// No description provided for @searchResults.
  ///
  /// In en, this message translates to:
  /// **'{count, plural, =0{No cars} =1{1 car} other{{count} cars}}'**
  String searchResults(int count);

  /// No description provided for @searchEmptyTitle.
  ///
  /// In en, this message translates to:
  /// **'No cars match that'**
  String get searchEmptyTitle;

  /// No description provided for @searchEmptyBody.
  ///
  /// In en, this message translates to:
  /// **'Try widening the dates, the price, or the city.'**
  String get searchEmptyBody;

  /// No description provided for @searchEmptyNoListings.
  ///
  /// In en, this message translates to:
  /// **'No cars are listed on Khadra yet. A rental office has to publish one before it can appear here.'**
  String get searchEmptyNoListings;

  /// No description provided for @searchLoadMore.
  ///
  /// In en, this message translates to:
  /// **'Load more'**
  String get searchLoadMore;

  /// No description provided for @vehiclePerDay.
  ///
  /// In en, this message translates to:
  /// **'{amount} / day'**
  String vehiclePerDay(String amount);

  /// No description provided for @vehicleSeats.
  ///
  /// In en, this message translates to:
  /// **'{count, plural, =1{1 seat} other{{count} seats}}'**
  String vehicleSeats(int count);

  /// No description provided for @vehicleDeliveryAvailable.
  ///
  /// In en, this message translates to:
  /// **'Delivery available'**
  String get vehicleDeliveryAvailable;

  /// No description provided for @vehicleNoPhoto.
  ///
  /// In en, this message translates to:
  /// **'No photo'**
  String get vehicleNoPhoto;

  /// No description provided for @vehicleSpecifications.
  ///
  /// In en, this message translates to:
  /// **'Specifications'**
  String get vehicleSpecifications;

  /// No description provided for @vehicleYear.
  ///
  /// In en, this message translates to:
  /// **'Year'**
  String get vehicleYear;

  /// No description provided for @vehicleColour.
  ///
  /// In en, this message translates to:
  /// **'Colour'**
  String get vehicleColour;

  /// No description provided for @vehicleFuel.
  ///
  /// In en, this message translates to:
  /// **'Fuel'**
  String get vehicleFuel;

  /// No description provided for @vehicleAbout.
  ///
  /// In en, this message translates to:
  /// **'About this car'**
  String get vehicleAbout;

  /// No description provided for @vehicleMileage.
  ///
  /// In en, this message translates to:
  /// **'Mileage'**
  String get vehicleMileage;

  /// No description provided for @vehicleMileageUnlimited.
  ///
  /// In en, this message translates to:
  /// **'Unlimited mileage'**
  String get vehicleMileageUnlimited;

  /// No description provided for @vehicleMileageLimited.
  ///
  /// In en, this message translates to:
  /// **'{limit} km per day'**
  String vehicleMileageLimited(int limit);

  /// No description provided for @vehicleMileageExcess.
  ///
  /// In en, this message translates to:
  /// **'{amount} for every extra kilometre'**
  String vehicleMileageExcess(String amount);

  /// No description provided for @vehicleFuelPolicy.
  ///
  /// In en, this message translates to:
  /// **'Fuel policy'**
  String get vehicleFuelPolicy;

  /// No description provided for @vehicleFuelPolicyFullToFull.
  ///
  /// In en, this message translates to:
  /// **'Return it with the same fuel you collected it with.'**
  String get vehicleFuelPolicyFullToFull;

  /// No description provided for @vehicleFuelPolicySameToSame.
  ///
  /// In en, this message translates to:
  /// **'Return it with the same fuel you collected it with.'**
  String get vehicleFuelPolicySameToSame;

  /// No description provided for @vehicleFuelPolicyPrepaid.
  ///
  /// In en, this message translates to:
  /// **'Fuel is paid for in advance.'**
  String get vehicleFuelPolicyPrepaid;

  /// No description provided for @vehicleSecurityDeposit.
  ///
  /// In en, this message translates to:
  /// **'Security deposit'**
  String get vehicleSecurityDeposit;

  /// No description provided for @vehicleSecurityDepositHelp.
  ///
  /// In en, this message translates to:
  /// **'Held by the rental office, not by Khadra, and returned when the car comes back.'**
  String get vehicleSecurityDepositHelp;

  /// No description provided for @vehicleAvailableForDates.
  ///
  /// In en, this message translates to:
  /// **'Free for your dates'**
  String get vehicleAvailableForDates;

  /// No description provided for @vehicleUnavailableForDates.
  ///
  /// In en, this message translates to:
  /// **'Not free for those dates'**
  String get vehicleUnavailableForDates;

  /// No description provided for @vehicleChooseDatesToBook.
  ///
  /// In en, this message translates to:
  /// **'Choose dates to see the price'**
  String get vehicleChooseDatesToBook;

  /// No description provided for @vehicleSeePrice.
  ///
  /// In en, this message translates to:
  /// **'See the price'**
  String get vehicleSeePrice;

  /// No description provided for @vehicleNotFoundTitle.
  ///
  /// In en, this message translates to:
  /// **'This car is not available'**
  String get vehicleNotFoundTitle;

  /// No description provided for @vehicleNotFoundBody.
  ///
  /// In en, this message translates to:
  /// **'It may have been taken off the platform, or the rental office is no longer trading.'**
  String get vehicleNotFoundBody;

  /// No description provided for @galleryTitle.
  ///
  /// In en, this message translates to:
  /// **'Rental office'**
  String get galleryTitle;

  /// No description provided for @galleryAbout.
  ///
  /// In en, this message translates to:
  /// **'About'**
  String get galleryAbout;

  /// No description provided for @galleryOpeningHours.
  ///
  /// In en, this message translates to:
  /// **'Opening hours'**
  String get galleryOpeningHours;

  /// No description provided for @galleryClosed.
  ///
  /// In en, this message translates to:
  /// **'Closed'**
  String get galleryClosed;

  /// No description provided for @galleryOpenToday.
  ///
  /// In en, this message translates to:
  /// **'Open today {opens} – {closes}'**
  String galleryOpenToday(String opens, String closes);

  /// No description provided for @galleryClosedToday.
  ///
  /// In en, this message translates to:
  /// **'Closed today'**
  String get galleryClosedToday;

  /// No description provided for @galleryLocation.
  ///
  /// In en, this message translates to:
  /// **'Where they are'**
  String get galleryLocation;

  /// No description provided for @galleryOpenInMaps.
  ///
  /// In en, this message translates to:
  /// **'Open in maps'**
  String get galleryOpenInMaps;

  /// No description provided for @galleryDelivery.
  ///
  /// In en, this message translates to:
  /// **'Delivery'**
  String get galleryDelivery;

  /// No description provided for @galleryDeliveryOffered.
  ///
  /// In en, this message translates to:
  /// **'They deliver within {radius} km for {fee}.'**
  String galleryDeliveryOffered(String radius, String fee);

  /// No description provided for @galleryDeliveryNotOffered.
  ///
  /// In en, this message translates to:
  /// **'This office does not deliver. You collect the car from them.'**
  String get galleryDeliveryNotOffered;

  /// No description provided for @bookDeliveryNotForThisCar.
  ///
  /// In en, this message translates to:
  /// **'This car is not offered for delivery. You collect it from the office.'**
  String get bookDeliveryNotForThisCar;

  /// No description provided for @galleryCars.
  ///
  /// In en, this message translates to:
  /// **'Their cars'**
  String get galleryCars;

  /// No description provided for @galleryRating.
  ///
  /// In en, this message translates to:
  /// **'{rating} out of 5'**
  String galleryRating(String rating);

  /// No description provided for @galleryReviewCount.
  ///
  /// In en, this message translates to:
  /// **'{count, plural, =0{No reviews yet} =1{1 review} other{{count} reviews}}'**
  String galleryReviewCount(int count);

  /// No description provided for @galleryNotRatedYet.
  ///
  /// In en, this message translates to:
  /// **'Not rated yet'**
  String get galleryNotRatedYet;

  /// No description provided for @bookTitle.
  ///
  /// In en, this message translates to:
  /// **'Your rental'**
  String get bookTitle;

  /// No description provided for @bookPickupMethod.
  ///
  /// In en, this message translates to:
  /// **'How will you get the car?'**
  String get bookPickupMethod;

  /// No description provided for @bookDeliveryLocation.
  ///
  /// In en, this message translates to:
  /// **'Where should they bring it?'**
  String get bookDeliveryLocation;

  /// No description provided for @bookChooseOnMap.
  ///
  /// In en, this message translates to:
  /// **'Choose the spot on the map'**
  String get bookChooseOnMap;

  /// No description provided for @bookLocationChosen.
  ///
  /// In en, this message translates to:
  /// **'Location chosen'**
  String get bookLocationChosen;

  /// No description provided for @bookLocationRequired.
  ///
  /// In en, this message translates to:
  /// **'Choose where the car should be delivered.'**
  String get bookLocationRequired;

  /// No description provided for @bookUseMyLocation.
  ///
  /// In en, this message translates to:
  /// **'Use my current location'**
  String get bookUseMyLocation;

  /// No description provided for @bookPriceTitle.
  ///
  /// In en, this message translates to:
  /// **'What it costs'**
  String get bookPriceTitle;

  /// No description provided for @bookDailyRate.
  ///
  /// In en, this message translates to:
  /// **'Daily rate'**
  String get bookDailyRate;

  /// The count is the SERVER's, billed in Amman calendar days. Never computed from two dates in the app.
  ///
  /// In en, this message translates to:
  /// **'{count, plural, =1{1 day} other{{count} days}}'**
  String bookDays(int count);

  /// No description provided for @bookRentalTotal.
  ///
  /// In en, this message translates to:
  /// **'Rental'**
  String get bookRentalTotal;

  /// No description provided for @bookDeliveryFee.
  ///
  /// In en, this message translates to:
  /// **'Delivery'**
  String get bookDeliveryFee;

  /// No description provided for @bookTotal.
  ///
  /// In en, this message translates to:
  /// **'Total'**
  String get bookTotal;

  /// No description provided for @bookDepositNow.
  ///
  /// In en, this message translates to:
  /// **'Deposit ({percent})'**
  String bookDepositNow(String percent);

  /// No description provided for @bookBalanceAtPickup.
  ///
  /// In en, this message translates to:
  /// **'Cash to the office at pick-up'**
  String get bookBalanceAtPickup;

  /// No description provided for @bookCalendarDaysNote.
  ///
  /// In en, this message translates to:
  /// **'Rentals are billed by calendar day in Amman, so a day is counted whether you keep the car for an hour of it or all of it.'**
  String get bookCalendarDaysNote;

  /// No description provided for @bookTermsTitle.
  ///
  /// In en, this message translates to:
  /// **'What you are agreeing to'**
  String get bookTermsTitle;

  /// No description provided for @bookTermsPayAfterApproval.
  ///
  /// In en, this message translates to:
  /// **'Nothing is charged now. The office answers within {hours} hours, and only then does the deposit fall due.'**
  String bookTermsPayAfterApproval(String hours);

  /// No description provided for @bookTermsPaymentWindow.
  ///
  /// In en, this message translates to:
  /// **'Once they approve, you have {hours} hours to pay the deposit or the booking ends and the car goes back on the market.'**
  String bookTermsPaymentWindow(String hours);

  /// No description provided for @bookTermsFreeCancellation.
  ///
  /// In en, this message translates to:
  /// **'Free cancellation for {hours} hours after the deposit clears.'**
  String bookTermsFreeCancellation(String hours);

  /// No description provided for @bookTermsCancellationPenalty.
  ///
  /// In en, this message translates to:
  /// **'Cancelling after that is assessed at {percent} of the deposit. Nothing is taken without a dispute being opened and settled.'**
  String bookTermsCancellationPenalty(String percent);

  /// No description provided for @bookRequest.
  ///
  /// In en, this message translates to:
  /// **'Request this car'**
  String get bookRequest;

  /// No description provided for @bookRequesting.
  ///
  /// In en, this message translates to:
  /// **'Sending your request…'**
  String get bookRequesting;

  /// No description provided for @bookDoneTitle.
  ///
  /// In en, this message translates to:
  /// **'Request sent'**
  String get bookDoneTitle;

  /// No description provided for @bookDoneBody.
  ///
  /// In en, this message translates to:
  /// **'{gallery} has your request and will answer within {hours} hours. We will tell you as soon as they do.'**
  String bookDoneBody(String gallery, String hours);

  /// No description provided for @bookDoneReference.
  ///
  /// In en, this message translates to:
  /// **'Your reference is {reference}.'**
  String bookDoneReference(String reference);

  /// No description provided for @bookViewBooking.
  ///
  /// In en, this message translates to:
  /// **'View the booking'**
  String get bookViewBooking;

  /// No description provided for @bookDocumentsNeededTitle.
  ///
  /// In en, this message translates to:
  /// **'Upload your documents first'**
  String get bookDocumentsNeededTitle;

  /// No description provided for @bookDocumentsNeededBody.
  ///
  /// In en, this message translates to:
  /// **'Jordanian law requires the rental office to check your driving licence and identity before handing over a car.'**
  String get bookDocumentsNeededBody;

  /// No description provided for @bookDocumentsNeededAction.
  ///
  /// In en, this message translates to:
  /// **'Upload them now'**
  String get bookDocumentsNeededAction;

  /// No description provided for @bookVerifyEmailFirst.
  ///
  /// In en, this message translates to:
  /// **'Verify your email address before booking. Your booking updates go there.'**
  String get bookVerifyEmailFirst;

  /// No description provided for @bookingsTitle.
  ///
  /// In en, this message translates to:
  /// **'My bookings'**
  String get bookingsTitle;

  /// No description provided for @bookingsTabAll.
  ///
  /// In en, this message translates to:
  /// **'All'**
  String get bookingsTabAll;

  /// No description provided for @bookingsTabPending.
  ///
  /// In en, this message translates to:
  /// **'Waiting'**
  String get bookingsTabPending;

  /// No description provided for @bookingsTabUpcoming.
  ///
  /// In en, this message translates to:
  /// **'Upcoming'**
  String get bookingsTabUpcoming;

  /// No description provided for @bookingsTabActive.
  ///
  /// In en, this message translates to:
  /// **'Out now'**
  String get bookingsTabActive;

  /// No description provided for @bookingsTabReturned.
  ///
  /// In en, this message translates to:
  /// **'Returned'**
  String get bookingsTabReturned;

  /// No description provided for @bookingsTabCompleted.
  ///
  /// In en, this message translates to:
  /// **'Finished'**
  String get bookingsTabCompleted;

  /// No description provided for @bookingsTabClosed.
  ///
  /// In en, this message translates to:
  /// **'Closed'**
  String get bookingsTabClosed;

  /// No description provided for @bookingsTabDisputed.
  ///
  /// In en, this message translates to:
  /// **'Disputed'**
  String get bookingsTabDisputed;

  /// No description provided for @bookingsEmptyTitle.
  ///
  /// In en, this message translates to:
  /// **'Nothing here yet'**
  String get bookingsEmptyTitle;

  /// No description provided for @bookingsEmptyBody.
  ///
  /// In en, this message translates to:
  /// **'Bookings you make will show up here.'**
  String get bookingsEmptyBody;

  /// No description provided for @bookingsEmptyAction.
  ///
  /// In en, this message translates to:
  /// **'Find a car'**
  String get bookingsEmptyAction;

  /// No description provided for @bookingsSignedOutTitle.
  ///
  /// In en, this message translates to:
  /// **'Sign in to see your bookings'**
  String get bookingsSignedOutTitle;

  /// No description provided for @bookingsSignedOutBody.
  ///
  /// In en, this message translates to:
  /// **'Your bookings, documents and alerts live in your account.'**
  String get bookingsSignedOutBody;

  /// No description provided for @statusRequested.
  ///
  /// In en, this message translates to:
  /// **'Waiting for the office'**
  String get statusRequested;

  /// No description provided for @statusApproved.
  ///
  /// In en, this message translates to:
  /// **'Approved — deposit due'**
  String get statusApproved;

  /// No description provided for @statusConfirmed.
  ///
  /// In en, this message translates to:
  /// **'Confirmed'**
  String get statusConfirmed;

  /// No description provided for @statusPickedUp.
  ///
  /// In en, this message translates to:
  /// **'Out on rental'**
  String get statusPickedUp;

  /// No description provided for @statusReturned.
  ///
  /// In en, this message translates to:
  /// **'Returned'**
  String get statusReturned;

  /// No description provided for @statusCompleted.
  ///
  /// In en, this message translates to:
  /// **'Finished'**
  String get statusCompleted;

  /// No description provided for @statusCancelled.
  ///
  /// In en, this message translates to:
  /// **'Cancelled'**
  String get statusCancelled;

  /// No description provided for @statusRejected.
  ///
  /// In en, this message translates to:
  /// **'Declined'**
  String get statusRejected;

  /// No description provided for @statusNoShow.
  ///
  /// In en, this message translates to:
  /// **'Not collected'**
  String get statusNoShow;

  /// No description provided for @statusExpired.
  ///
  /// In en, this message translates to:
  /// **'Expired'**
  String get statusExpired;

  /// No description provided for @bookingReference.
  ///
  /// In en, this message translates to:
  /// **'Reference'**
  String get bookingReference;

  /// No description provided for @bookingWhen.
  ///
  /// In en, this message translates to:
  /// **'When'**
  String get bookingWhen;

  /// No description provided for @bookingWhere.
  ///
  /// In en, this message translates to:
  /// **'Pick-up'**
  String get bookingWhere;

  /// No description provided for @bookingWhereDelivery.
  ///
  /// In en, this message translates to:
  /// **'Delivered to you'**
  String get bookingWhereDelivery;

  /// No description provided for @bookingWhereSelfPickup.
  ///
  /// In en, this message translates to:
  /// **'You collect it'**
  String get bookingWhereSelfPickup;

  /// No description provided for @bookingGallery.
  ///
  /// In en, this message translates to:
  /// **'Rental office'**
  String get bookingGallery;

  /// No description provided for @bookingCar.
  ///
  /// In en, this message translates to:
  /// **'Car'**
  String get bookingCar;

  /// No description provided for @bookingPlate.
  ///
  /// In en, this message translates to:
  /// **'Plate'**
  String get bookingPlate;

  /// No description provided for @bookingHistory.
  ///
  /// In en, this message translates to:
  /// **'What has happened'**
  String get bookingHistory;

  /// No description provided for @bookingPrice.
  ///
  /// In en, this message translates to:
  /// **'Price'**
  String get bookingPrice;

  /// No description provided for @bookingTermsFrozen.
  ///
  /// In en, this message translates to:
  /// **'These are the terms this booking was made under. Changing a setting today never re-prices a booking already made.'**
  String get bookingTermsFrozen;

  /// No description provided for @bookingAwaitingDecisionTitle.
  ///
  /// In en, this message translates to:
  /// **'Waiting on {gallery}'**
  String bookingAwaitingDecisionTitle(String gallery);

  /// No description provided for @bookingAwaitingDecisionBody.
  ///
  /// In en, this message translates to:
  /// **'They have until {deadline} to answer. The car is held for you until then.'**
  String bookingAwaitingDecisionBody(String deadline);

  /// No description provided for @bookingAwaitingPaymentTitle.
  ///
  /// In en, this message translates to:
  /// **'Deposit of {amount} is due'**
  String bookingAwaitingPaymentTitle(String amount);

  /// No description provided for @bookingAwaitingPaymentBy.
  ///
  /// In en, this message translates to:
  /// **'Due by {deadline}'**
  String bookingAwaitingPaymentBy(String deadline);

  /// No description provided for @bookingPaymentNotAvailableTitle.
  ///
  /// In en, this message translates to:
  /// **'Paying in the app is not available yet'**
  String get bookingPaymentNotAvailableTitle;

  /// No description provided for @bookingPaymentNotAvailableBody.
  ///
  /// In en, this message translates to:
  /// **'Khadra cannot take card payments in this version. This booking will end at the deadline above and the car will go back on the market. Nothing is owed when that happens.'**
  String get bookingPaymentNotAvailableBody;

  /// No description provided for @bookingExpiredTitle.
  ///
  /// In en, this message translates to:
  /// **'This booking ran out of time'**
  String get bookingExpiredTitle;

  /// No description provided for @bookingExpiredBody.
  ///
  /// In en, this message translates to:
  /// **'Nothing is owed. The car went back on the market.'**
  String get bookingExpiredBody;

  /// No description provided for @bookingRejectedTitle.
  ///
  /// In en, this message translates to:
  /// **'The office declined this request'**
  String get bookingRejectedTitle;

  /// No description provided for @bookingCancelledTitle.
  ///
  /// In en, this message translates to:
  /// **'This booking was cancelled'**
  String get bookingCancelledTitle;

  /// No description provided for @bookingCancelledBy.
  ///
  /// In en, this message translates to:
  /// **'Cancelled by {party}'**
  String bookingCancelledBy(String party);

  /// No description provided for @bookingNoShowTitle.
  ///
  /// In en, this message translates to:
  /// **'The car was not collected'**
  String get bookingNoShowTitle;

  /// No description provided for @bookingCompletedTitle.
  ///
  /// In en, this message translates to:
  /// **'This rental is finished'**
  String get bookingCompletedTitle;

  /// No description provided for @bookingPenaltyAssessed.
  ///
  /// In en, this message translates to:
  /// **'An amount of {amount} has been assessed against {party}.'**
  String bookingPenaltyAssessed(String amount, String party);

  /// No description provided for @bookingPenaltyRange.
  ///
  /// In en, this message translates to:
  /// **'Between {min} and {max} has been assessed against {party}.'**
  String bookingPenaltyRange(String min, String max, String party);

  /// No description provided for @bookingPenaltyNotCharged.
  ///
  /// In en, this message translates to:
  /// **'Nothing has been charged. An assessment only becomes money if a dispute is opened and Khadra settles it.'**
  String get bookingPenaltyNotCharged;

  /// No description provided for @bookingPartyCustomer.
  ///
  /// In en, this message translates to:
  /// **'you'**
  String get bookingPartyCustomer;

  /// No description provided for @bookingPartyDealer.
  ///
  /// In en, this message translates to:
  /// **'the rental office'**
  String get bookingPartyDealer;

  /// No description provided for @bookingPartyAdmin.
  ///
  /// In en, this message translates to:
  /// **'Khadra'**
  String get bookingPartyAdmin;

  /// No description provided for @bookingPartySystem.
  ///
  /// In en, this message translates to:
  /// **'Khadra'**
  String get bookingPartySystem;

  /// No description provided for @bookingPartyUnattributed.
  ///
  /// In en, this message translates to:
  /// **'nobody'**
  String get bookingPartyUnattributed;

  /// No description provided for @bookingHandoverPickup.
  ///
  /// In en, this message translates to:
  /// **'Collected'**
  String get bookingHandoverPickup;

  /// No description provided for @bookingHandoverReturn.
  ///
  /// In en, this message translates to:
  /// **'Returned'**
  String get bookingHandoverReturn;

  /// No description provided for @bookingOdometer.
  ///
  /// In en, this message translates to:
  /// **'Odometer {km} km'**
  String bookingOdometer(String km);

  /// No description provided for @bookingFuelLevel.
  ///
  /// In en, this message translates to:
  /// **'Fuel {percent}%'**
  String bookingFuelLevel(String percent);

  /// No description provided for @bookingCashCollected.
  ///
  /// In en, this message translates to:
  /// **'Cash taken: {amount}'**
  String bookingCashCollected(String amount);

  /// No description provided for @bookingCountdownDays.
  ///
  /// In en, this message translates to:
  /// **'{days}d {hours}h left'**
  String bookingCountdownDays(int days, int hours);

  /// No description provided for @bookingCountdownHours.
  ///
  /// In en, this message translates to:
  /// **'{hours}h {minutes}m left'**
  String bookingCountdownHours(int hours, int minutes);

  /// No description provided for @bookingCountdownMinutes.
  ///
  /// In en, this message translates to:
  /// **'{minutes}m left'**
  String bookingCountdownMinutes(int minutes);

  /// No description provided for @bookingCountdownOver.
  ///
  /// In en, this message translates to:
  /// **'The time has run out'**
  String get bookingCountdownOver;

  /// No description provided for @cancelTitle.
  ///
  /// In en, this message translates to:
  /// **'Cancel this booking'**
  String get cancelTitle;

  /// No description provided for @cancelReasonQuestion.
  ///
  /// In en, this message translates to:
  /// **'Why are you cancelling?'**
  String get cancelReasonQuestion;

  /// No description provided for @cancelDetailsLabel.
  ///
  /// In en, this message translates to:
  /// **'Anything you want to add'**
  String get cancelDetailsLabel;

  /// No description provided for @cancelDetailsHint.
  ///
  /// In en, this message translates to:
  /// **'The rental office will see this.'**
  String get cancelDetailsHint;

  /// No description provided for @cancelFreeNotice.
  ///
  /// In en, this message translates to:
  /// **'Cancelling now costs you nothing.'**
  String get cancelFreeNotice;

  /// No description provided for @cancelPenaltyNotice.
  ///
  /// In en, this message translates to:
  /// **'Cancelling now assesses {amount} against you. Nothing is charged unless a dispute is opened and settled.'**
  String cancelPenaltyNotice(String amount);

  /// No description provided for @cancelConfirm.
  ///
  /// In en, this message translates to:
  /// **'Cancel the booking'**
  String get cancelConfirm;

  /// No description provided for @cancelKeep.
  ///
  /// In en, this message translates to:
  /// **'Keep it'**
  String get cancelKeep;

  /// No description provided for @cancelDone.
  ///
  /// In en, this message translates to:
  /// **'Your booking has been cancelled.'**
  String get cancelDone;

  /// No description provided for @cancelExpiredInstead.
  ///
  /// In en, this message translates to:
  /// **'The time on this booking had already run out, so it ended on its own. Nothing is owed.'**
  String get cancelExpiredInstead;

  /// No description provided for @cancelNotPossible.
  ///
  /// In en, this message translates to:
  /// **'This booking can no longer be cancelled.'**
  String get cancelNotPossible;

  /// No description provided for @nonDeliveryTitle.
  ///
  /// In en, this message translates to:
  /// **'The car was never handed over'**
  String get nonDeliveryTitle;

  /// No description provided for @nonDeliveryBody.
  ///
  /// In en, this message translates to:
  /// **'Tell us what happened. The rental office will be asked to answer, and Khadra will look at it if you open a dispute afterwards.'**
  String get nonDeliveryBody;

  /// No description provided for @nonDeliveryDetails.
  ///
  /// In en, this message translates to:
  /// **'What happened'**
  String get nonDeliveryDetails;

  /// No description provided for @nonDeliveryTooEarly.
  ///
  /// In en, this message translates to:
  /// **'The rental has not started yet, so there is nothing to report.'**
  String get nonDeliveryTooEarly;

  /// No description provided for @nonDeliveryReport.
  ///
  /// In en, this message translates to:
  /// **'Report it'**
  String get nonDeliveryReport;

  /// No description provided for @nonDeliveryDone.
  ///
  /// In en, this message translates to:
  /// **'Reported. The booking is closed and the rental office has been told.'**
  String get nonDeliveryDone;

  /// No description provided for @disputeTitle.
  ///
  /// In en, this message translates to:
  /// **'Open a dispute'**
  String get disputeTitle;

  /// No description provided for @disputeBody.
  ///
  /// In en, this message translates to:
  /// **'Tell us what went wrong. The rental office can answer, and Khadra decides.'**
  String get disputeBody;

  /// No description provided for @disputeReason.
  ///
  /// In en, this message translates to:
  /// **'What went wrong'**
  String get disputeReason;

  /// No description provided for @disputeEvidence.
  ///
  /// In en, this message translates to:
  /// **'Photos or documents'**
  String get disputeEvidence;

  /// The server names evidence by its generated storage key, so a number is the only readable label available.
  ///
  /// In en, this message translates to:
  /// **'Photo {number}'**
  String disputeEvidenceNumbered(int number);

  /// No description provided for @disputeAddEvidence.
  ///
  /// In en, this message translates to:
  /// **'Add a photo'**
  String get disputeAddEvidence;

  /// No description provided for @disputeOpen.
  ///
  /// In en, this message translates to:
  /// **'Open the dispute'**
  String get disputeOpen;

  /// No description provided for @disputeOpened.
  ///
  /// In en, this message translates to:
  /// **'Your dispute is open. Khadra will look at it within {hours} hours.'**
  String disputeOpened(String hours);

  /// No description provided for @disputeViewTitle.
  ///
  /// In en, this message translates to:
  /// **'Dispute'**
  String get disputeViewTitle;

  /// No description provided for @disputeStatements.
  ///
  /// In en, this message translates to:
  /// **'The conversation'**
  String get disputeStatements;

  /// No description provided for @disputeAddStatement.
  ///
  /// In en, this message translates to:
  /// **'Add something'**
  String get disputeAddStatement;

  /// No description provided for @disputeStatementHint.
  ///
  /// In en, this message translates to:
  /// **'Anything that helps'**
  String get disputeStatementHint;

  /// No description provided for @disputeWithdraw.
  ///
  /// In en, this message translates to:
  /// **'Withdraw the dispute'**
  String get disputeWithdraw;

  /// No description provided for @disputeWithdrawConfirm.
  ///
  /// In en, this message translates to:
  /// **'Withdraw this? The rental office will be told, and nothing will be charged to anyone.'**
  String get disputeWithdrawConfirm;

  /// No description provided for @disputeWithdrawn.
  ///
  /// In en, this message translates to:
  /// **'Withdrawn. Nothing has been charged.'**
  String get disputeWithdrawn;

  /// No description provided for @disputeStatusOpen.
  ///
  /// In en, this message translates to:
  /// **'Open'**
  String get disputeStatusOpen;

  /// No description provided for @disputeStatusUnderReview.
  ///
  /// In en, this message translates to:
  /// **'Being looked at'**
  String get disputeStatusUnderReview;

  /// No description provided for @disputeStatusResolved.
  ///
  /// In en, this message translates to:
  /// **'Settled'**
  String get disputeStatusResolved;

  /// No description provided for @disputeStatusWithdrawn.
  ///
  /// In en, this message translates to:
  /// **'Withdrawn'**
  String get disputeStatusWithdrawn;

  /// No description provided for @disputeYou.
  ///
  /// In en, this message translates to:
  /// **'You'**
  String get disputeYou;

  /// No description provided for @disputeGallery.
  ///
  /// In en, this message translates to:
  /// **'The rental office'**
  String get disputeGallery;

  /// No description provided for @disputeKhadra.
  ///
  /// In en, this message translates to:
  /// **'Khadra'**
  String get disputeKhadra;

  /// No description provided for @disputeCannotOpen.
  ///
  /// In en, this message translates to:
  /// **'A dispute can only be opened while the settlement window is open.'**
  String get disputeCannotOpen;

  /// No description provided for @disputeExisting.
  ///
  /// In en, this message translates to:
  /// **'You already have a dispute open on this booking.'**
  String get disputeExisting;

  /// No description provided for @disputeView.
  ///
  /// In en, this message translates to:
  /// **'View the dispute'**
  String get disputeView;

  /// No description provided for @reviewTitle.
  ///
  /// In en, this message translates to:
  /// **'Rate this rental'**
  String get reviewTitle;

  /// No description provided for @reviewQuestion.
  ///
  /// In en, this message translates to:
  /// **'How was {gallery}?'**
  String reviewQuestion(String gallery);

  /// No description provided for @reviewRatingLabel.
  ///
  /// In en, this message translates to:
  /// **'Your rating'**
  String get reviewRatingLabel;

  /// No description provided for @reviewStars.
  ///
  /// In en, this message translates to:
  /// **'{count, plural, =1{1 star} other{{count} stars}}'**
  String reviewStars(int count);

  /// No description provided for @reviewCommentLabel.
  ///
  /// In en, this message translates to:
  /// **'Anything to add'**
  String get reviewCommentLabel;

  /// No description provided for @reviewCommentHint.
  ///
  /// In en, this message translates to:
  /// **'What other renters should know.'**
  String get reviewCommentHint;

  /// No description provided for @reviewSubmit.
  ///
  /// In en, this message translates to:
  /// **'Post the rating'**
  String get reviewSubmit;

  /// No description provided for @reviewThanks.
  ///
  /// In en, this message translates to:
  /// **'Thanks. Your rating is on their page now.'**
  String get reviewThanks;

  /// No description provided for @reviewYours.
  ///
  /// In en, this message translates to:
  /// **'Your rating'**
  String get reviewYours;

  /// No description provided for @reviewOnlyWhenFinished.
  ///
  /// In en, this message translates to:
  /// **'You can rate a rental once it is finished.'**
  String get reviewOnlyWhenFinished;

  /// No description provided for @reviewAlreadyLeft.
  ///
  /// In en, this message translates to:
  /// **'You have already rated this rental.'**
  String get reviewAlreadyLeft;

  /// No description provided for @reviewsTitle.
  ///
  /// In en, this message translates to:
  /// **'Reviews'**
  String get reviewsTitle;

  /// No description provided for @reviewsEmpty.
  ///
  /// In en, this message translates to:
  /// **'Nobody has rated this office yet.'**
  String get reviewsEmpty;

  /// No description provided for @reviewHidden.
  ///
  /// In en, this message translates to:
  /// **'This comment was removed by Khadra. The rating still counts.'**
  String get reviewHidden;

  /// No description provided for @reviewsRatingIsOfficeNote.
  ///
  /// In en, this message translates to:
  /// **'Khadra rates rental offices, not individual cars.'**
  String get reviewsRatingIsOfficeNote;

  /// No description provided for @documentsTitle.
  ///
  /// In en, this message translates to:
  /// **'My documents'**
  String get documentsTitle;

  /// No description provided for @documentsIntro.
  ///
  /// In en, this message translates to:
  /// **'The rental office has to see your driving licence and your identity before handing over a car. Upload them once and every booking can use them.'**
  String get documentsIntro;

  /// No description provided for @documentsDrivingLicenceFront.
  ///
  /// In en, this message translates to:
  /// **'Driving licence — front'**
  String get documentsDrivingLicenceFront;

  /// No description provided for @documentsDrivingLicenceBack.
  ///
  /// In en, this message translates to:
  /// **'Driving licence — back'**
  String get documentsDrivingLicenceBack;

  /// No description provided for @documentsNationalId.
  ///
  /// In en, this message translates to:
  /// **'National ID'**
  String get documentsNationalId;

  /// No description provided for @documentsPassport.
  ///
  /// In en, this message translates to:
  /// **'Passport'**
  String get documentsPassport;

  /// No description provided for @documentsUpload.
  ///
  /// In en, this message translates to:
  /// **'Upload'**
  String get documentsUpload;

  /// No description provided for @documentsReplace.
  ///
  /// In en, this message translates to:
  /// **'Replace'**
  String get documentsReplace;

  /// No description provided for @documentsView.
  ///
  /// In en, this message translates to:
  /// **'View'**
  String get documentsView;

  /// No description provided for @documentsTakePhoto.
  ///
  /// In en, this message translates to:
  /// **'Take a photo'**
  String get documentsTakePhoto;

  /// No description provided for @documentsChooseFile.
  ///
  /// In en, this message translates to:
  /// **'Choose a file'**
  String get documentsChooseFile;

  /// No description provided for @documentsUploading.
  ///
  /// In en, this message translates to:
  /// **'Uploading…'**
  String get documentsUploading;

  /// No description provided for @documentsUploaded.
  ///
  /// In en, this message translates to:
  /// **'Uploaded {when}'**
  String documentsUploaded(String when);

  /// No description provided for @documentsStatusPendingReview.
  ///
  /// In en, this message translates to:
  /// **'Waiting to be checked'**
  String get documentsStatusPendingReview;

  /// No description provided for @documentsStatusVerified.
  ///
  /// In en, this message translates to:
  /// **'Checked'**
  String get documentsStatusVerified;

  /// No description provided for @documentsStatusRejected.
  ///
  /// In en, this message translates to:
  /// **'Not accepted'**
  String get documentsStatusRejected;

  /// No description provided for @documentsMissing.
  ///
  /// In en, this message translates to:
  /// **'Still needed'**
  String get documentsMissing;

  /// No description provided for @documentsBadgeComplete.
  ///
  /// In en, this message translates to:
  /// **'Complete'**
  String get documentsBadgeComplete;

  /// No description provided for @documentsBadgeMissing.
  ///
  /// In en, this message translates to:
  /// **'Needed'**
  String get documentsBadgeMissing;

  /// No description provided for @documentsComplete.
  ///
  /// In en, this message translates to:
  /// **'You have everything you need to book.'**
  String get documentsComplete;

  /// No description provided for @documentsIncomplete.
  ///
  /// In en, this message translates to:
  /// **'Upload the missing documents before you can book.'**
  String get documentsIncomplete;

  /// No description provided for @documentsNotYetCheckedTitle.
  ///
  /// In en, this message translates to:
  /// **'Nobody has checked these yet'**
  String get documentsNotYetCheckedTitle;

  /// No description provided for @documentsNotYetCheckedBody.
  ///
  /// In en, this message translates to:
  /// **'Khadra does not verify documents in this version. The rental office checks them in person when you collect the car.'**
  String get documentsNotYetCheckedBody;

  /// No description provided for @documentsTooLarge.
  ///
  /// In en, this message translates to:
  /// **'That file is larger than {size}.'**
  String documentsTooLarge(String size);

  /// No description provided for @documentsWrongType.
  ///
  /// In en, this message translates to:
  /// **'That file type is not accepted. Use {types}.'**
  String documentsWrongType(String types);

  /// No description provided for @documentsCameraNote.
  ///
  /// In en, this message translates to:
  /// **'Take the photo in good light with the whole document in frame.'**
  String get documentsCameraNote;

  /// No description provided for @profileTitle.
  ///
  /// In en, this message translates to:
  /// **'Profile'**
  String get profileTitle;

  /// No description provided for @profileSignedInAs.
  ///
  /// In en, this message translates to:
  /// **'Signed in as {email}'**
  String profileSignedInAs(String email);

  /// No description provided for @profilePersonalDetails.
  ///
  /// In en, this message translates to:
  /// **'Your details'**
  String get profilePersonalDetails;

  /// No description provided for @profileEdit.
  ///
  /// In en, this message translates to:
  /// **'Edit your details'**
  String get profileEdit;

  /// No description provided for @profileEmailFixed.
  ///
  /// In en, this message translates to:
  /// **'Your email address cannot be changed here. It is how you sign in and where a password reset is sent, so changing it needs its own verified step, which Khadra has not built yet.'**
  String get profileEmailFixed;

  /// No description provided for @profileSaved.
  ///
  /// In en, this message translates to:
  /// **'Saved.'**
  String get profileSaved;

  /// No description provided for @profileSecurity.
  ///
  /// In en, this message translates to:
  /// **'Security'**
  String get profileSecurity;

  /// No description provided for @profileSessions.
  ///
  /// In en, this message translates to:
  /// **'Where you are signed in'**
  String get profileSessions;

  /// No description provided for @profileSessionsEmpty.
  ///
  /// In en, this message translates to:
  /// **'No other devices.'**
  String get profileSessionsEmpty;

  /// No description provided for @profileSessionThis.
  ///
  /// In en, this message translates to:
  /// **'This device'**
  String get profileSessionThis;

  /// No description provided for @profileSessionLastUsed.
  ///
  /// In en, this message translates to:
  /// **'Last used {when}'**
  String profileSessionLastUsed(String when);

  /// No description provided for @profileSessionRevoke.
  ///
  /// In en, this message translates to:
  /// **'Sign out'**
  String get profileSessionRevoke;

  /// No description provided for @profileSessionRevoked.
  ///
  /// In en, this message translates to:
  /// **'That device has been signed out.'**
  String get profileSessionRevoked;

  /// No description provided for @profileLanguage.
  ///
  /// In en, this message translates to:
  /// **'Language'**
  String get profileLanguage;

  /// No description provided for @profileLanguageEnglish.
  ///
  /// In en, this message translates to:
  /// **'English'**
  String get profileLanguageEnglish;

  /// No description provided for @profileLanguageArabic.
  ///
  /// In en, this message translates to:
  /// **'العربية'**
  String get profileLanguageArabic;

  /// No description provided for @profileAbout.
  ///
  /// In en, this message translates to:
  /// **'About Khadra'**
  String get profileAbout;

  /// No description provided for @profileAboutBody.
  ///
  /// In en, this message translates to:
  /// **'Khadra connects renters with licensed rental offices in Jordan. Every office on the platform holds a green plate licence and is checked before it can list a car.'**
  String get profileAboutBody;

  /// No description provided for @profileVersion.
  ///
  /// In en, this message translates to:
  /// **'Version {version}'**
  String profileVersion(String version);

  /// No description provided for @profileSignOutConfirm.
  ///
  /// In en, this message translates to:
  /// **'Sign out of Khadra?'**
  String get profileSignOutConfirm;

  /// No description provided for @profileSignOutEverywhereConfirm.
  ///
  /// In en, this message translates to:
  /// **'Sign out on every device you have used?'**
  String get profileSignOutEverywhereConfirm;

  /// No description provided for @profileMemberSince.
  ///
  /// In en, this message translates to:
  /// **'With Khadra since {when}'**
  String profileMemberSince(String when);

  /// No description provided for @profileEmailUnverified.
  ///
  /// In en, this message translates to:
  /// **'Your email is not verified yet'**
  String get profileEmailUnverified;

  /// No description provided for @notificationsTitle.
  ///
  /// In en, this message translates to:
  /// **'Alerts'**
  String get notificationsTitle;

  /// No description provided for @notificationsMarkAllRead.
  ///
  /// In en, this message translates to:
  /// **'Mark all as read'**
  String get notificationsMarkAllRead;

  /// No description provided for @notificationsEmptyTitle.
  ///
  /// In en, this message translates to:
  /// **'Nothing to tell you'**
  String get notificationsEmptyTitle;

  /// No description provided for @notificationsEmptyBody.
  ///
  /// In en, this message translates to:
  /// **'We will let you know when a rental office answers one of your bookings.'**
  String get notificationsEmptyBody;

  /// No description provided for @notificationsUnread.
  ///
  /// In en, this message translates to:
  /// **'{count, plural, =1{1 unread} other{{count} unread}}'**
  String notificationsUnread(int count);

  /// No description provided for @notificationYourBookingApproved.
  ///
  /// In en, this message translates to:
  /// **'{actor} approved your booking'**
  String notificationYourBookingApproved(String actor);

  /// No description provided for @notificationYourBookingRejected.
  ///
  /// In en, this message translates to:
  /// **'{actor} declined your booking'**
  String notificationYourBookingRejected(String actor);

  /// No description provided for @notificationYourBookingExpired.
  ///
  /// In en, this message translates to:
  /// **'Your booking with {actor} ran out of time'**
  String notificationYourBookingExpired(String actor);

  /// No description provided for @notificationYourBookingCompleted.
  ///
  /// In en, this message translates to:
  /// **'Your rental with {actor} is finished'**
  String notificationYourBookingCompleted(String actor);

  /// No description provided for @notificationYourBookingMarkedNoShow.
  ///
  /// In en, this message translates to:
  /// **'{actor} recorded that the car was not collected'**
  String notificationYourBookingMarkedNoShow(String actor);

  /// A kind this version of the app does not know. Better than an empty row.
  ///
  /// In en, this message translates to:
  /// **'{actor} updated something on your account'**
  String notificationUnknown(String actor);

  /// No description provided for @notificationAboutBooking.
  ///
  /// In en, this message translates to:
  /// **'Booking {reference}'**
  String notificationAboutBooking(String reference);

  /// No description provided for @reasonPlansChanged.
  ///
  /// In en, this message translates to:
  /// **'My plans changed'**
  String get reasonPlansChanged;

  /// No description provided for @reasonFoundBetterPrice.
  ///
  /// In en, this message translates to:
  /// **'I found a better price'**
  String get reasonFoundBetterPrice;

  /// No description provided for @reasonTravelCancelled.
  ///
  /// In en, this message translates to:
  /// **'My trip was cancelled'**
  String get reasonTravelCancelled;

  /// No description provided for @reasonBookedByMistake.
  ///
  /// In en, this message translates to:
  /// **'I booked this by mistake'**
  String get reasonBookedByMistake;

  /// No description provided for @reasonDealerUnresponsive.
  ///
  /// In en, this message translates to:
  /// **'The rental office did not respond'**
  String get reasonDealerUnresponsive;

  /// No description provided for @reasonOther.
  ///
  /// In en, this message translates to:
  /// **'Another reason'**
  String get reasonOther;

  /// No description provided for @timeJustNow.
  ///
  /// In en, this message translates to:
  /// **'just now'**
  String get timeJustNow;

  /// No description provided for @timeMinutesAgo.
  ///
  /// In en, this message translates to:
  /// **'{count, plural, =1{a minute ago} other{{count} minutes ago}}'**
  String timeMinutesAgo(int count);

  /// No description provided for @timeHoursAgo.
  ///
  /// In en, this message translates to:
  /// **'{count, plural, =1{an hour ago} other{{count} hours ago}}'**
  String timeHoursAgo(int count);

  /// No description provided for @timeDaysAgo.
  ///
  /// In en, this message translates to:
  /// **'{count, plural, =1{yesterday} other{{count} days ago}}'**
  String timeDaysAgo(int count);

  /// No description provided for @timeAmmanNote.
  ///
  /// In en, this message translates to:
  /// **'Times are Amman time.'**
  String get timeAmmanNote;

  /// No description provided for @validationRequired.
  ///
  /// In en, this message translates to:
  /// **'This is needed.'**
  String get validationRequired;

  /// No description provided for @validationEmail.
  ///
  /// In en, this message translates to:
  /// **'That does not look like an email address.'**
  String get validationEmail;

  /// No description provided for @validationPhone.
  ///
  /// In en, this message translates to:
  /// **'Enter a Jordanian mobile number, like 0791234567.'**
  String get validationPhone;

  /// No description provided for @validationPasswordShort.
  ///
  /// In en, this message translates to:
  /// **'Use at least 8 characters.'**
  String get validationPasswordShort;

  /// No description provided for @validationPasswordMatch.
  ///
  /// In en, this message translates to:
  /// **'The two passwords do not match.'**
  String get validationPasswordMatch;

  /// No description provided for @validationDateOfBirth.
  ///
  /// In en, this message translates to:
  /// **'Enter your date of birth.'**
  String get validationDateOfBirth;

  /// No description provided for @validationTooLong.
  ///
  /// In en, this message translates to:
  /// **'That is longer than {max} characters.'**
  String validationTooLong(int max);

  /// No description provided for @validationReturnAfterPickup.
  ///
  /// In en, this message translates to:
  /// **'The return has to come after the pick-up.'**
  String get validationReturnAfterPickup;

  /// No description provided for @validationChooseReason.
  ///
  /// In en, this message translates to:
  /// **'Choose a reason.'**
  String get validationChooseReason;

  /// No description provided for @errorAuthInvalidCredentials.
  ///
  /// In en, this message translates to:
  /// **'That email and password do not match an account.'**
  String get errorAuthInvalidCredentials;

  /// No description provided for @errorAuthEmailTaken.
  ///
  /// In en, this message translates to:
  /// **'There is already an account on that email address.'**
  String get errorAuthEmailTaken;

  /// No description provided for @errorAuthPhoneTaken.
  ///
  /// In en, this message translates to:
  /// **'That phone number is already on another account.'**
  String get errorAuthPhoneTaken;

  /// No description provided for @errorAuthInvalidPhone.
  ///
  /// In en, this message translates to:
  /// **'Enter a Jordanian mobile number, like 0791234567.'**
  String get errorAuthInvalidPhone;

  /// No description provided for @errorAuthInvalidEmail.
  ///
  /// In en, this message translates to:
  /// **'That does not look like an email address.'**
  String get errorAuthInvalidEmail;

  /// No description provided for @errorAuthWeakPassword.
  ///
  /// In en, this message translates to:
  /// **'Choose a longer password with a letter and a number.'**
  String get errorAuthWeakPassword;

  /// No description provided for @errorAuthAccountSuspended.
  ///
  /// In en, this message translates to:
  /// **'This account has been suspended. Contact Khadra.'**
  String get errorAuthAccountSuspended;

  /// No description provided for @errorAuthEmailNotVerified.
  ///
  /// In en, this message translates to:
  /// **'Verify your email address first.'**
  String get errorAuthEmailNotVerified;

  /// No description provided for @errorAuthUnderage.
  ///
  /// In en, this message translates to:
  /// **'You are not old enough to rent a car on Khadra.'**
  String get errorAuthUnderage;

  /// No description provided for @errorAuthInvalidToken.
  ///
  /// In en, this message translates to:
  /// **'That link is no longer valid. Ask for a new one.'**
  String get errorAuthInvalidToken;

  /// No description provided for @errorAuthTooManyAttempts.
  ///
  /// In en, this message translates to:
  /// **'Too many attempts. Wait a few minutes and try again.'**
  String get errorAuthTooManyAttempts;

  /// No description provided for @errorBookingVehicleUnavailable.
  ///
  /// In en, this message translates to:
  /// **'Somebody took that car while you were deciding. Try different dates.'**
  String get errorBookingVehicleUnavailable;

  /// No description provided for @errorBookingDocumentsIncomplete.
  ///
  /// In en, this message translates to:
  /// **'Upload your driving licence and identity document before booking.'**
  String get errorBookingDocumentsIncomplete;

  /// No description provided for @errorBookingEmailNotVerified.
  ///
  /// In en, this message translates to:
  /// **'Verify your email address before booking.'**
  String get errorBookingEmailNotVerified;

  /// No description provided for @errorBookingPeriodInPast.
  ///
  /// In en, this message translates to:
  /// **'Choose dates in the future.'**
  String get errorBookingPeriodInPast;

  /// No description provided for @errorBookingNotFound.
  ///
  /// In en, this message translates to:
  /// **'That booking was not found.'**
  String get errorBookingNotFound;

  /// No description provided for @errorBookingCannotCancel.
  ///
  /// In en, this message translates to:
  /// **'This booking can no longer be cancelled.'**
  String get errorBookingCannotCancel;

  /// No description provided for @errorRateLimited.
  ///
  /// In en, this message translates to:
  /// **'Too many requests. Wait a moment and try again.'**
  String get errorRateLimited;
}

class _AppLocalizationsDelegate
    extends LocalizationsDelegate<AppLocalizations> {
  const _AppLocalizationsDelegate();

  @override
  Future<AppLocalizations> load(Locale locale) {
    return SynchronousFuture<AppLocalizations>(lookupAppLocalizations(locale));
  }

  @override
  bool isSupported(Locale locale) =>
      <String>['ar', 'en'].contains(locale.languageCode);

  @override
  bool shouldReload(_AppLocalizationsDelegate old) => false;
}

AppLocalizations lookupAppLocalizations(Locale locale) {
  // Lookup logic when only language code is specified.
  switch (locale.languageCode) {
    case 'ar':
      return AppLocalizationsAr();
    case 'en':
      return AppLocalizationsEn();
  }

  throw FlutterError(
    'AppLocalizations.delegate failed to load unsupported locale "$locale". This is likely '
    'an issue with the localizations generation tool. Please file an issue '
    'on GitHub with a reproducible sample app and the gen-l10n configuration '
    'that was used.',
  );
}
