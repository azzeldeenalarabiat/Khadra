// ignore: unused_import
import 'package:intl/intl.dart' as intl;
import 'app_localizations.dart';

// ignore_for_file: type=lint

/// The translations for Arabic (`ar`).
class AppLocalizationsAr extends AppLocalizations {
  AppLocalizationsAr([String locale = 'ar']) : super(locale);

  @override
  String get appName => 'خضرا';

  @override
  String get appTagline => 'استأجر سيارة في الأردن';

  @override
  String get actionRetry => 'حاول مرة أخرى';

  @override
  String get actionCancel => 'إلغاء';

  @override
  String get actionClose => 'إغلاق';

  @override
  String get actionSave => 'حفظ';

  @override
  String get actionSaveChanges => 'حفظ التعديلات';

  @override
  String get actionContinue => 'متابعة';

  @override
  String get actionBack => 'رجوع';

  @override
  String get actionDone => 'تم';

  @override
  String get actionApply => 'تطبيق';

  @override
  String get actionClearAll => 'مسح الكل';

  @override
  String get actionSearch => 'بحث';

  @override
  String get actionConfirm => 'تأكيد';

  @override
  String get actionSubmit => 'إرسال';

  @override
  String get actionSkip => 'تخطٍّ';

  @override
  String get actionSeeAll => 'عرض الكل';

  @override
  String get actionShowMore => 'عرض المزيد';

  @override
  String get actionShowLess => 'عرض أقل';

  @override
  String get labelOptional => 'اختياري';

  @override
  String get labelRequired => 'مطلوب';

  @override
  String get labelYes => 'نعم';

  @override
  String get labelNo => 'لا';

  @override
  String get labelLoading => 'جارٍ التحميل…';

  @override
  String get labelNone => 'لا شيء';

  @override
  String get errorGeneric => 'حدث خطأ ما. حاول مرة أخرى.';

  @override
  String get errorOffline => 'لا يوجد اتصال. تحقق من الشبكة وحاول مجدداً.';

  @override
  String get errorTimeout => 'استغرق الخادم وقتاً طويلاً. حاول مرة أخرى.';

  @override
  String get errorServer => 'هناك مشكلة في الخادم. حاول بعد قليل.';

  @override
  String errorReference(String traceId) {
    return 'الرقم المرجعي $traceId';
  }

  @override
  String get authSignIn => 'تسجيل الدخول';

  @override
  String get authSignUp => 'إنشاء حساب';

  @override
  String get authSignOut => 'تسجيل الخروج';

  @override
  String get authSignOutEverywhere => 'تسجيل الخروج من كل الأجهزة';

  @override
  String get authWelcomeTitle => 'أهلاً بعودتك';

  @override
  String get authWelcomeSubtitle => 'سجّل دخولك لإدارة حجوزاتك.';

  @override
  String get authCreateAccountTitle => 'أنشئ حسابك';

  @override
  String get authCreateAccountSubtitle =>
      'تحتاج حساباً للحجز. أما التصفح فمتاح للجميع.';

  @override
  String get authEmail => 'البريد الإلكتروني';

  @override
  String get authPassword => 'كلمة المرور';

  @override
  String get authNewPassword => 'كلمة المرور الجديدة';

  @override
  String get authCurrentPassword => 'كلمة المرور الحالية';

  @override
  String get authConfirmPassword => 'تأكيد كلمة المرور';

  @override
  String get authFullName => 'الاسم الكامل';

  @override
  String get authPhone => 'رقم الهاتف';

  @override
  String get authPhoneHint => '07XXXXXXXX';

  @override
  String get authDateOfBirth => 'تاريخ الميلاد';

  @override
  String get authForeignNational => 'لست أردني الجنسية';

  @override
  String get authForeignNationalHelp =>
      'سترفع جواز سفر بدلاً من الهوية الوطنية.';

  @override
  String authMinimumAge(int age) {
    return 'يجب ألّا يقلّ عمرك عن $age عاماً لاستئجار سيارة عبر خضرا.';
  }

  @override
  String get authForgotPassword => 'نسيت كلمة المرور؟';

  @override
  String get authForgotPasswordTitle => 'إعادة تعيين كلمة المرور';

  @override
  String get authForgotPasswordSubtitle =>
      'سنرسل إليك رابطاً لتعيين كلمة مرور جديدة.';

  @override
  String get authSendResetLink => 'أرسل الرابط';

  @override
  String get authResetSent =>
      'إن كان لهذا العنوان حساب، فالرابط في طريقه إليه.';

  @override
  String get authResetPasswordTitle => 'اختر كلمة مرور جديدة';

  @override
  String get authResetPasswordDone => 'تم تغيير كلمة المرور. سجّل الدخول بها.';

  @override
  String get authChangePassword => 'تغيير كلمة المرور';

  @override
  String get authChangePasswordDone => 'تم تغيير كلمة المرور.';

  @override
  String get authChangePasswordSignsOutOthers =>
      'تغيير كلمة المرور يسجّل خروجك من بقية الأجهزة';

  @override
  String get authChangePasswordSignsOutOthersBody =>
      'كل هاتف أو حاسوب آخر مسجَّل الدخول بهذا الحساب سيحتاج إلى تسجيل الدخول من جديد. أما هذا الجهاز فيبقى مسجَّلاً.';

  @override
  String get authAlreadyHaveAccount => 'لديك حساب بالفعل؟';

  @override
  String get authNoAccount => 'جديد على خضرا؟';

  @override
  String get authPasswordRules => 'ثمانية أحرف على الأقل، تتضمن حرفاً ورقماً.';

  @override
  String get authVerifyEmailTitle => 'فعّل بريدك الإلكتروني';

  @override
  String authVerifyEmailBody(String email) {
    return 'أرسلنا رابطاً إلى $email. افتحه لإكمال إعداد حسابك.';
  }

  @override
  String get authVerifyEmailWhy =>
      'تصلك تحديثات حجوزاتك على هذا العنوان، لذا يجب أن يعمل قبل أن تتمكن من الحجز.';

  @override
  String get authResendVerification => 'أعد الإرسال';

  @override
  String get authVerificationResent =>
      'تم الإرسال. تفقّد بريدك، ومجلد الرسائل غير المرغوبة.';

  @override
  String get authVerifiedTitle => 'تم تفعيل البريد';

  @override
  String get authVerifiedBody => 'يمكنك حجز سيارة الآن.';

  @override
  String get authEmailNotDelivered => 'تعذّر إرسال البريد الآن. حاول بعد قليل.';

  @override
  String get authBrowseInstead => 'تصفّح أولاً';

  @override
  String get authSignInToContinue => 'سجّل الدخول للمتابعة';

  @override
  String get authSignInToBook => 'سجّل الدخول لحجز هذه السيارة';

  @override
  String get authSessionExpired => 'انتهت جلستك. سجّل الدخول من جديد.';

  @override
  String get navBrowse => 'تصفّح';

  @override
  String get navBookings => 'حجوزاتي';

  @override
  String get navNotifications => 'التنبيهات';

  @override
  String get navProfile => 'حسابي';

  @override
  String get searchTitle => 'ابحث عن سيارة';

  @override
  String get searchHint => 'الماركة أو الموديل';

  @override
  String get searchFilters => 'عوامل التصفية';

  @override
  String searchFiltersApplied(int count) {
    String _temp0 = intl.Intl.pluralLogic(
      count,
      locale: localeName,
      other: '$count عامل تصفية',
      many: '$count عامل تصفية',
      few: '$count عوامل تصفية',
      two: 'عاملا تصفية',
      one: 'عامل تصفية واحد',
      zero: 'بدون تصفية',
    );
    return '$_temp0';
  }

  @override
  String get searchCity => 'المدينة';

  @override
  String get searchAnyCity => 'كل المدن';

  @override
  String get searchCarType => 'نوع السيارة';

  @override
  String get searchAnyCarType => 'كل الأنواع';

  @override
  String get searchTransmission => 'ناقل الحركة';

  @override
  String get searchAnyTransmission => 'الكل';

  @override
  String get searchSeats => 'المقاعد';

  @override
  String searchMinimumSeats(int count) {
    return '$count مقاعد على الأقل';
  }

  @override
  String get searchAnySeats => 'الكل';

  @override
  String get searchPriceRange => 'السعر اليومي';

  @override
  String get searchDeliveryOnly => 'التوصيل إليّ فقط';

  @override
  String get searchDates => 'التواريخ';

  @override
  String get searchAnyDates => 'أي تاريخ';

  @override
  String get searchPickup => 'الاستلام';

  @override
  String get searchReturn => 'الإرجاع';

  @override
  String get searchChooseDates => 'اختر تواريخك';

  @override
  String get searchDatesHelp =>
      'اختيار التواريخ يعرض السيارات المتاحة فقط، ويتيح لنا تسعير الإيجار.';

  @override
  String get searchClearDates => 'امسح التواريخ';

  @override
  String searchResults(int count) {
    String _temp0 = intl.Intl.pluralLogic(
      count,
      locale: localeName,
      other: '$count سيارة',
      many: '$count سيارة',
      few: '$count سيارات',
      two: 'سيارتان',
      one: 'سيارة واحدة',
      zero: 'لا توجد سيارات',
    );
    return '$_temp0';
  }

  @override
  String get searchEmptyTitle => 'لا توجد سيارات تطابق ذلك';

  @override
  String get searchEmptyBody => 'جرّب توسيع التواريخ أو السعر أو المدينة.';

  @override
  String get searchEmptyNoListings =>
      'لا توجد سيارات معروضة على خضرا بعد. على مكتب تأجير أن ينشر سيارة قبل أن تظهر هنا.';

  @override
  String get searchLoadMore => 'عرض المزيد';

  @override
  String vehiclePerDay(String amount) {
    return '$amount / اليوم';
  }

  @override
  String vehicleSeats(int count) {
    String _temp0 = intl.Intl.pluralLogic(
      count,
      locale: localeName,
      other: '$count مقعد',
      many: '$count مقعداً',
      few: '$count مقاعد',
      two: 'مقعدان',
      one: 'مقعد واحد',
    );
    return '$_temp0';
  }

  @override
  String get vehicleDeliveryAvailable => 'التوصيل متاح';

  @override
  String get vehicleNoPhoto => 'لا توجد صورة';

  @override
  String get vehicleSpecifications => 'المواصفات';

  @override
  String get vehicleYear => 'سنة الصنع';

  @override
  String get vehicleColour => 'اللون';

  @override
  String get vehicleFuel => 'الوقود';

  @override
  String get vehicleAbout => 'عن هذه السيارة';

  @override
  String get vehicleMileage => 'المسافة المقطوعة';

  @override
  String get vehicleMileageUnlimited => 'كيلومترات غير محدودة';

  @override
  String vehicleMileageLimited(int limit) {
    return '$limit كم في اليوم';
  }

  @override
  String vehicleMileageExcess(String amount) {
    return '$amount عن كل كيلومتر إضافي';
  }

  @override
  String get vehicleFuelPolicy => 'سياسة الوقود';

  @override
  String get vehicleFuelPolicyFullToFull =>
      'أعِد السيارة بالوقود نفسه الذي استلمتها به.';

  @override
  String get vehicleFuelPolicySameToSame =>
      'أعِد السيارة بالوقود نفسه الذي استلمتها به.';

  @override
  String get vehicleFuelPolicyPrepaid => 'الوقود مدفوع مسبقاً.';

  @override
  String get vehicleSecurityDeposit => 'التأمين';

  @override
  String get vehicleSecurityDepositHelp =>
      'يحتفظ به مكتب التأجير لا خضرا، ويُعاد عند إرجاع السيارة.';

  @override
  String get vehicleAvailableForDates => 'متاحة في تواريخك';

  @override
  String get vehicleUnavailableForDates => 'غير متاحة في تلك التواريخ';

  @override
  String get vehicleChooseDatesToBook => 'اختر التواريخ لمعرفة السعر';

  @override
  String get vehicleSeePrice => 'اعرض السعر';

  @override
  String get vehicleNotFoundTitle => 'هذه السيارة غير متاحة';

  @override
  String get vehicleNotFoundBody =>
      'قد تكون أُزيلت من المنصة، أو أن مكتب التأجير لم يعد يعمل.';

  @override
  String get galleryTitle => 'مكتب التأجير';

  @override
  String get galleryAbout => 'نبذة';

  @override
  String get galleryOpeningHours => 'ساعات العمل';

  @override
  String get galleryClosed => 'مغلق';

  @override
  String galleryOpenToday(String opens, String closes) {
    return 'مفتوح اليوم $opens – $closes';
  }

  @override
  String get galleryClosedToday => 'مغلق اليوم';

  @override
  String get galleryLocation => 'الموقع';

  @override
  String get galleryOpenInMaps => 'افتح في الخرائط';

  @override
  String get galleryDelivery => 'التوصيل';

  @override
  String galleryDeliveryOffered(String radius, String fee) {
    return 'يوصّلون ضمن $radius كم مقابل $fee.';
  }

  @override
  String get galleryDeliveryNotOffered =>
      'هذا المكتب لا يوصّل. تستلم السيارة منهم.';

  @override
  String get bookDeliveryNotForThisCar =>
      'هذه السيارة غير متاحة للتوصيل. تستلمها من المكتب.';

  @override
  String get galleryCars => 'سياراتهم';

  @override
  String galleryRating(String rating) {
    return '$rating من 5';
  }

  @override
  String galleryReviewCount(int count) {
    String _temp0 = intl.Intl.pluralLogic(
      count,
      locale: localeName,
      other: '$count تقييم',
      many: '$count تقييماً',
      few: '$count تقييمات',
      two: 'تقييمان',
      one: 'تقييم واحد',
      zero: 'لا توجد تقييمات بعد',
    );
    return '$_temp0';
  }

  @override
  String get galleryNotRatedYet => 'لم يُقيَّم بعد';

  @override
  String get bookTitle => 'إيجارك';

  @override
  String get bookPickupMethod => 'كيف ستحصل على السيارة؟';

  @override
  String get bookDeliveryLocation => 'أين توصَّل السيارة؟';

  @override
  String get bookChooseOnMap => 'حدّد الموقع على الخريطة';

  @override
  String get bookLocationChosen => 'تم تحديد الموقع';

  @override
  String get bookLocationRequired => 'حدّد المكان الذي ستُوصَّل إليه السيارة.';

  @override
  String get bookUseMyLocation => 'استخدم موقعي الحالي';

  @override
  String get bookPriceTitle => 'التكلفة';

  @override
  String get bookDailyRate => 'السعر اليومي';

  @override
  String bookDays(int count) {
    String _temp0 = intl.Intl.pluralLogic(
      count,
      locale: localeName,
      other: '$count يوم',
      many: '$count يوماً',
      few: '$count أيام',
      two: 'يومان',
      one: 'يوم واحد',
    );
    return '$_temp0';
  }

  @override
  String get bookRentalTotal => 'قيمة الإيجار';

  @override
  String get bookDeliveryFee => 'التوصيل';

  @override
  String get bookTotal => 'الإجمالي';

  @override
  String bookDepositNow(String percent) {
    return 'العربون ($percent)';
  }

  @override
  String get bookBalanceAtPickup => 'نقداً للمكتب عند الاستلام';

  @override
  String get bookCalendarDaysNote =>
      'تُحتسب الأيام وفق التقويم في عمّان، فاليوم يُحسب سواء أبقيت السيارة ساعة منه أم كامله.';

  @override
  String get bookTermsTitle => 'ما توافق عليه';

  @override
  String bookTermsPayAfterApproval(String hours) {
    return 'لا يُخصم شيء الآن. يردّ المكتب خلال $hours ساعة، وعندها فقط يُستحق العربون.';
  }

  @override
  String bookTermsPaymentWindow(String hours) {
    return 'بعد الموافقة، أمامك $hours ساعة لدفع العربون وإلا انتهى الحجز وعادت السيارة إلى السوق.';
  }

  @override
  String bookTermsFreeCancellation(String hours) {
    return 'الإلغاء مجاني خلال $hours ساعة من دفع العربون.';
  }

  @override
  String bookTermsCancellationPenalty(String percent) {
    return 'الإلغاء بعد ذلك يُقدَّر بـ $percent من العربون. لا يُخصم شيء دون فتح نزاع وتسويته.';
  }

  @override
  String get bookRequest => 'اطلب هذه السيارة';

  @override
  String get bookRequesting => 'جارٍ إرسال طلبك…';

  @override
  String get bookDoneTitle => 'أُرسل الطلب';

  @override
  String bookDoneBody(String gallery, String hours) {
    return 'وصل طلبك إلى $gallery وسيردّون خلال $hours ساعة. سنخبرك فور ردّهم.';
  }

  @override
  String bookDoneReference(String reference) {
    return 'رقمك المرجعي هو $reference.';
  }

  @override
  String get bookViewBooking => 'عرض الحجز';

  @override
  String get bookDocumentsNeededTitle => 'ارفع مستنداتك أولاً';

  @override
  String get bookDocumentsNeededBody =>
      'يوجب القانون الأردني على مكتب التأجير التحقق من رخصة القيادة والهوية قبل تسليم السيارة.';

  @override
  String get bookDocumentsNeededAction => 'ارفعها الآن';

  @override
  String get bookVerifyEmailFirst =>
      'فعّل بريدك الإلكتروني قبل الحجز. تحديثات حجزك تصل إليه.';

  @override
  String get bookingsTitle => 'حجوزاتي';

  @override
  String get bookingsTabAll => 'الكل';

  @override
  String get bookingsTabPending => 'بانتظار الرد';

  @override
  String get bookingsTabUpcoming => 'قادمة';

  @override
  String get bookingsTabActive => 'جارية';

  @override
  String get bookingsTabReturned => 'مُعادة';

  @override
  String get bookingsTabCompleted => 'منتهية';

  @override
  String get bookingsTabClosed => 'مغلقة';

  @override
  String get bookingsTabDisputed => 'متنازع عليها';

  @override
  String get bookingsEmptyTitle => 'لا شيء هنا بعد';

  @override
  String get bookingsEmptyBody => 'ستظهر هنا الحجوزات التي تنشئها.';

  @override
  String get bookingsEmptyAction => 'ابحث عن سيارة';

  @override
  String get bookingsSignedOutTitle => 'سجّل الدخول لعرض حجوزاتك';

  @override
  String get bookingsSignedOutBody => 'حجوزاتك ومستنداتك وتنبيهاتك في حسابك.';

  @override
  String get statusRequested => 'بانتظار ردّ المكتب';

  @override
  String get statusApproved => 'موافق عليه — العربون مستحق';

  @override
  String get statusConfirmed => 'مؤكد';

  @override
  String get statusPickedUp => 'السيارة معك';

  @override
  String get statusReturned => 'أُعيدت';

  @override
  String get statusCompleted => 'منتهٍ';

  @override
  String get statusCancelled => 'ملغى';

  @override
  String get statusRejected => 'مرفوض';

  @override
  String get statusNoShow => 'لم تُستلم';

  @override
  String get statusExpired => 'منتهي الصلاحية';

  @override
  String get bookingReference => 'الرقم المرجعي';

  @override
  String get bookingWhen => 'المدة';

  @override
  String get bookingWhere => 'الاستلام';

  @override
  String get bookingWhereDelivery => 'يُوصَّل إليك';

  @override
  String get bookingWhereSelfPickup => 'تستلمها بنفسك';

  @override
  String get bookingGallery => 'مكتب التأجير';

  @override
  String get bookingCar => 'السيارة';

  @override
  String get bookingPlate => 'رقم اللوحة';

  @override
  String get bookingHistory => 'ما جرى';

  @override
  String get bookingPrice => 'السعر';

  @override
  String get bookingTermsFrozen =>
      'هذه هي الشروط التي أُبرم الحجز بموجبها. تغيير أي إعداد اليوم لا يُعيد تسعير حجز سابق.';

  @override
  String bookingAwaitingDecisionTitle(String gallery) {
    return 'بانتظار $gallery';
  }

  @override
  String bookingAwaitingDecisionBody(String deadline) {
    return 'أمامهم حتى $deadline للردّ. السيارة محجوزة لك حتى ذلك الحين.';
  }

  @override
  String bookingAwaitingPaymentTitle(String amount) {
    return 'عربون بقيمة $amount مستحق';
  }

  @override
  String bookingAwaitingPaymentBy(String deadline) {
    return 'مستحق قبل $deadline';
  }

  @override
  String get bookingPaymentNotAvailableTitle =>
      'الدفع عبر التطبيق غير متاح بعد';

  @override
  String get bookingPaymentNotAvailableBody =>
      'لا تستطيع خضرا استقبال مدفوعات البطاقات في هذه النسخة. سينتهي هذا الحجز عند الموعد أعلاه وتعود السيارة إلى السوق. لا شيء مستحق عليك عندها.';

  @override
  String get bookingExpiredTitle => 'انتهى وقت هذا الحجز';

  @override
  String get bookingExpiredBody => 'لا شيء مستحق. عادت السيارة إلى السوق.';

  @override
  String get bookingRejectedTitle => 'رفض المكتب هذا الطلب';

  @override
  String get bookingCancelledTitle => 'أُلغي هذا الحجز';

  @override
  String bookingCancelledBy(String party) {
    return 'أُلغي من قِبل $party';
  }

  @override
  String get bookingNoShowTitle => 'لم تُستلم السيارة';

  @override
  String get bookingCompletedTitle => 'انتهى هذا الإيجار';

  @override
  String bookingPenaltyAssessed(String amount, String party) {
    return 'قُدِّر مبلغ $amount على $party.';
  }

  @override
  String bookingPenaltyRange(String min, String max, String party) {
    return 'قُدِّر مبلغ بين $min و$max على $party.';
  }

  @override
  String get bookingPenaltyNotCharged =>
      'لم يُخصم شيء. لا يتحول التقدير إلى مبلغ فعلي إلا بفتح نزاع وتسويته من قِبل خضرا.';

  @override
  String get bookingPartyCustomer => 'عليك';

  @override
  String get bookingPartyDealer => 'مكتب التأجير';

  @override
  String get bookingPartyAdmin => 'خضرا';

  @override
  String get bookingPartySystem => 'خضرا';

  @override
  String get bookingPartyUnattributed => 'لا أحد';

  @override
  String get bookingHandoverPickup => 'الاستلام';

  @override
  String get bookingHandoverReturn => 'الإرجاع';

  @override
  String bookingOdometer(String km) {
    return 'العدّاد $km كم';
  }

  @override
  String bookingFuelLevel(String percent) {
    return 'الوقود $percent%';
  }

  @override
  String bookingCashCollected(String amount) {
    return 'المبلغ النقدي المستلم: $amount';
  }

  @override
  String bookingCountdownDays(int days, int hours) {
    return 'بقي $days يوم و$hours ساعة';
  }

  @override
  String bookingCountdownHours(int hours, int minutes) {
    return 'بقي $hours ساعة و$minutes دقيقة';
  }

  @override
  String bookingCountdownMinutes(int minutes) {
    return 'بقي $minutes دقيقة';
  }

  @override
  String get bookingCountdownOver => 'انتهى الوقت';

  @override
  String get cancelTitle => 'إلغاء هذا الحجز';

  @override
  String get cancelReasonQuestion => 'لماذا تلغي الحجز؟';

  @override
  String get cancelDetailsLabel => 'أي شيء تودّ إضافته';

  @override
  String get cancelDetailsHint => 'سيطّلع عليه مكتب التأجير.';

  @override
  String get cancelFreeNotice => 'الإلغاء الآن لا يكلّفك شيئاً.';

  @override
  String cancelPenaltyNotice(String amount) {
    return 'الإلغاء الآن يُقدِّر عليك مبلغ $amount. لا يُخصم شيء ما لم يُفتح نزاع ويُسوَّى.';
  }

  @override
  String get cancelConfirm => 'ألغِ الحجز';

  @override
  String get cancelKeep => 'أبقِه';

  @override
  String get cancelDone => 'تم إلغاء حجزك.';

  @override
  String get cancelExpiredInstead =>
      'كان وقت هذا الحجز قد انتهى فعلاً، فأُغلق من تلقاء نفسه. لا شيء مستحق.';

  @override
  String get cancelNotPossible => 'لم يعد بالإمكان إلغاء هذا الحجز.';

  @override
  String get nonDeliveryTitle => 'لم تُسلَّم السيارة';

  @override
  String get nonDeliveryBody =>
      'أخبرنا بما حدث. سيُطلب من مكتب التأجير الرد، وستنظر خضرا في الأمر إن فتحت نزاعاً بعد ذلك.';

  @override
  String get nonDeliveryDetails => 'ما الذي حدث';

  @override
  String get nonDeliveryTooEarly =>
      'الوقت مبكر على الإبلاغ. امنح مكتب التأجير مهلة السماح القصيرة المتاحة له بعد الموعد المتفق عليه.';

  @override
  String nonDeliveryNotYet(String from) {
    return 'يمكنك الإبلاغ عن ذلك اعتباراً من $from، بعد انقضاء مهلة السماح المتاحة لمكتب التأجير.';
  }

  @override
  String get nonDeliveryReport => 'أبلغ عن ذلك';

  @override
  String get nonDeliveryDone => 'تم الإبلاغ. أُغلق الحجز وأُبلغ مكتب التأجير.';

  @override
  String get disputeTitle => 'افتح نزاعاً';

  @override
  String get disputeBody =>
      'أخبرنا بما حدث. يمكن لمكتب التأجير الرد، وخضرا تقرر.';

  @override
  String get disputeReason => 'ما الذي حدث';

  @override
  String get disputeEvidence => 'صور أو مستندات';

  @override
  String disputeEvidenceNumbered(int number) {
    return 'صورة $number';
  }

  @override
  String get disputeAddEvidence => 'أضف صورة';

  @override
  String get disputeOpen => 'افتح النزاع';

  @override
  String disputeOpened(String hours) {
    return 'نزاعك مفتوح. ستنظر فيه خضرا خلال $hours ساعة.';
  }

  @override
  String get disputeViewTitle => 'النزاع';

  @override
  String get disputeStatements => 'المحادثة';

  @override
  String get disputeAddStatement => 'أضف شيئاً';

  @override
  String get disputeStatementHint => 'أي شيء يساعد';

  @override
  String get disputeWithdraw => 'اسحب النزاع';

  @override
  String get disputeWithdrawConfirm =>
      'هل تسحبه؟ سيُبلَّغ مكتب التأجير ولن يُخصم شيء من أحد.';

  @override
  String get disputeWithdrawn => 'تم السحب. لم يُخصم شيء.';

  @override
  String get disputeStatusOpen => 'مفتوح';

  @override
  String get disputeStatusUnderReview => 'قيد النظر';

  @override
  String get disputeStatusResolved => 'مسوّى';

  @override
  String get disputeStatusWithdrawn => 'مسحوب';

  @override
  String get disputeYou => 'أنت';

  @override
  String get disputeGallery => 'مكتب التأجير';

  @override
  String get disputeKhadra => 'خضرا';

  @override
  String get disputeCannotOpen => 'لا يمكن فتح نزاع إلا خلال مهلة التسوية.';

  @override
  String get disputeExisting => 'لديك نزاع مفتوح على هذا الحجز بالفعل.';

  @override
  String get disputeView => 'عرض النزاع';

  @override
  String get reviewTitle => 'قيّم هذا الإيجار';

  @override
  String reviewQuestion(String gallery) {
    return 'كيف كانت تجربتك مع $gallery؟';
  }

  @override
  String get reviewRatingLabel => 'تقييمك';

  @override
  String reviewStars(int count) {
    String _temp0 = intl.Intl.pluralLogic(
      count,
      locale: localeName,
      other: '$count نجمة',
      many: '$count نجمة',
      few: '$count نجوم',
      two: 'نجمتان',
      one: 'نجمة واحدة',
    );
    return '$_temp0';
  }

  @override
  String get reviewCommentLabel => 'أي شيء تودّ إضافته';

  @override
  String get reviewCommentHint => 'ما ينبغي أن يعرفه المستأجرون الآخرون.';

  @override
  String get reviewSubmit => 'انشر التقييم';

  @override
  String get reviewThanks => 'شكراً لك. تقييمك ظاهر الآن على صفحتهم.';

  @override
  String get reviewYours => 'تقييمك';

  @override
  String get reviewOnlyWhenFinished => 'يمكنك تقييم الإيجار بعد انتهائه.';

  @override
  String get reviewAlreadyLeft => 'لقد قيّمت هذا الإيجار من قبل.';

  @override
  String get reviewsTitle => 'التقييمات';

  @override
  String get reviewsEmpty => 'لم يقيّم أحد هذا المكتب بعد.';

  @override
  String get reviewHidden => 'أزالت خضرا هذا التعليق. التقييم لا يزال محتسباً.';

  @override
  String get reviewsRatingIsOfficeNote =>
      'تقيّم خضرا مكاتب التأجير لا السيارات المفردة.';

  @override
  String get documentsTitle => 'مستنداتي';

  @override
  String get documentsIntro =>
      'على مكتب التأجير الاطلاع على رخصة قيادتك وهويتك قبل تسليم السيارة. ارفعها مرة واحدة ليستفيد منها كل حجز.';

  @override
  String get documentsDrivingLicenceFront => 'رخصة القيادة — الوجه الأمامي';

  @override
  String get documentsDrivingLicenceBack => 'رخصة القيادة — الوجه الخلفي';

  @override
  String get documentsNationalId => 'الهوية الوطنية';

  @override
  String get documentsPassport => 'جواز السفر';

  @override
  String get documentsUpload => 'رفع';

  @override
  String get documentsReplace => 'استبدال';

  @override
  String get documentsView => 'عرض';

  @override
  String get documentsTakePhoto => 'التقط صورة';

  @override
  String get documentsChooseFile => 'اختر ملفاً';

  @override
  String get documentsUploading => 'جارٍ الرفع…';

  @override
  String documentsUploaded(String when) {
    return 'رُفع $when';
  }

  @override
  String get documentsStatusPendingReview => 'بانتظار التدقيق';

  @override
  String get documentsStatusVerified => 'مدقّق';

  @override
  String get documentsStatusRejected => 'غير مقبول';

  @override
  String get documentsMissing => 'لا يزال مطلوباً';

  @override
  String get documentsBadgeComplete => 'مكتملة';

  @override
  String get documentsBadgeMissing => 'ناقصة';

  @override
  String get documentsComplete => 'لديك كل ما تحتاجه للحجز.';

  @override
  String get documentsIncomplete =>
      'ارفع المستندات الناقصة قبل أن تتمكن من الحجز.';

  @override
  String get documentsNotYetCheckedTitle => 'لم يدقّق أحد هذه المستندات بعد';

  @override
  String get documentsNotYetCheckedBody =>
      'لا تدقّق خضرا المستندات في هذه النسخة. يتحقق منها مكتب التأجير شخصياً عند استلام السيارة.';

  @override
  String documentsTooLarge(String size) {
    return 'حجم هذا الملف أكبر من $size.';
  }

  @override
  String documentsWrongType(String types) {
    return 'نوع هذا الملف غير مقبول. استخدم $types.';
  }

  @override
  String get documentsCameraNote =>
      'التقط الصورة في إضاءة جيدة مع ظهور المستند كاملاً.';

  @override
  String get profileTitle => 'حسابي';

  @override
  String profileSignedInAs(String email) {
    return 'مسجّل الدخول باسم $email';
  }

  @override
  String get profilePersonalDetails => 'بياناتك';

  @override
  String get profileEdit => 'تعديل بياناتك';

  @override
  String get profileEmailFixed =>
      'لا يمكن تغيير بريدك الإلكتروني هنا. فهو وسيلة دخولك وعنوان إعادة تعيين كلمة المرور، لذا يحتاج تغييره إلى خطوة تحقق خاصة لم تبنِها خضرا بعد.';

  @override
  String get profileSaved => 'تم الحفظ.';

  @override
  String get profileSecurity => 'الأمان';

  @override
  String get profileSessions => 'أجهزتك المسجّلة';

  @override
  String get profileSessionsEmpty => 'لا توجد أجهزة أخرى.';

  @override
  String get profileSessionThis => 'هذا الجهاز';

  @override
  String profileSessionLastUsed(String when) {
    return 'آخر استخدام $when';
  }

  @override
  String get profileSessionRevoke => 'تسجيل الخروج';

  @override
  String get profileSessionRevoked => 'تم تسجيل خروج ذلك الجهاز.';

  @override
  String get profileLanguage => 'اللغة';

  @override
  String get profileLanguageEnglish => 'English';

  @override
  String get profileLanguageArabic => 'العربية';

  @override
  String get profileAbout => 'عن خضرا';

  @override
  String get profileAboutBody =>
      'تربط خضرا المستأجرين بمكاتب تأجير مرخّصة في الأردن. كل مكتب على المنصة يحمل رخصة اللوحة الخضراء ويُدقَّق قبل أن يتمكن من عرض سيارة.';

  @override
  String profileVersion(String version) {
    return 'الإصدار $version';
  }

  @override
  String get profileSignOutConfirm => 'تسجيل الخروج من خضرا؟';

  @override
  String get profileSignOutEverywhereConfirm =>
      'تسجيل الخروج من كل جهاز استخدمته؟';

  @override
  String profileMemberSince(String when) {
    return 'معنا منذ $when';
  }

  @override
  String get profileEmailUnverified => 'بريدك الإلكتروني غير مفعّل بعد';

  @override
  String get notificationsTitle => 'التنبيهات';

  @override
  String get notificationsMarkAllRead => 'تعليم الكل كمقروء';

  @override
  String get notificationsEmptyTitle => 'لا جديد';

  @override
  String get notificationsEmptyBody =>
      'سنخبرك حين يردّ مكتب تأجير على أحد حجوزاتك.';

  @override
  String notificationsUnread(int count) {
    String _temp0 = intl.Intl.pluralLogic(
      count,
      locale: localeName,
      other: '$count غير مقروء',
      many: '$count غير مقروءة',
      few: '$count غير مقروءة',
      two: 'غير مقروءين',
      one: 'غير مقروء واحد',
    );
    return '$_temp0';
  }

  @override
  String notificationYourBookingApproved(String actor) {
    return 'وافق $actor على حجزك';
  }

  @override
  String notificationYourBookingRejected(String actor) {
    return 'رفض $actor حجزك';
  }

  @override
  String notificationYourBookingExpired(String actor) {
    return 'انتهى وقت حجزك مع $actor';
  }

  @override
  String notificationYourBookingCompleted(String actor) {
    return 'انتهى إيجارك مع $actor';
  }

  @override
  String notificationYourBookingMarkedNoShow(String actor) {
    return 'سجّل $actor أن السيارة لم تُستلم';
  }

  @override
  String notificationUnknown(String actor) {
    return 'حدّث $actor شيئاً في حسابك';
  }

  @override
  String notificationAboutBooking(String reference) {
    return 'الحجز $reference';
  }

  @override
  String get reasonPlansChanged => 'تغيّرت خططي';

  @override
  String get reasonFoundBetterPrice => 'وجدت سعراً أفضل';

  @override
  String get reasonTravelCancelled => 'أُلغيت رحلتي';

  @override
  String get reasonBookedByMistake => 'حجزت عن طريق الخطأ';

  @override
  String get reasonDealerUnresponsive => 'لم يستجب مكتب التأجير';

  @override
  String get reasonOther => 'سبب آخر';

  @override
  String get timeJustNow => 'الآن';

  @override
  String timeMinutesAgo(int count) {
    String _temp0 = intl.Intl.pluralLogic(
      count,
      locale: localeName,
      other: 'قبل $count دقيقة',
      many: 'قبل $count دقيقة',
      few: 'قبل $count دقائق',
      two: 'قبل دقيقتين',
      one: 'قبل دقيقة',
    );
    return '$_temp0';
  }

  @override
  String timeHoursAgo(int count) {
    String _temp0 = intl.Intl.pluralLogic(
      count,
      locale: localeName,
      other: 'قبل $count ساعة',
      many: 'قبل $count ساعة',
      few: 'قبل $count ساعات',
      two: 'قبل ساعتين',
      one: 'قبل ساعة',
    );
    return '$_temp0';
  }

  @override
  String timeDaysAgo(int count) {
    String _temp0 = intl.Intl.pluralLogic(
      count,
      locale: localeName,
      other: 'قبل $count يوم',
      many: 'قبل $count يوماً',
      few: 'قبل $count أيام',
      two: 'قبل يومين',
      one: 'أمس',
    );
    return '$_temp0';
  }

  @override
  String get timeAmmanNote => 'الأوقات بتوقيت عمّان.';

  @override
  String get validationRequired => 'هذا الحقل مطلوب.';

  @override
  String get validationEmail => 'هذا لا يبدو بريداً إلكترونياً.';

  @override
  String get validationPhone => 'أدخل رقم هاتف أردني، مثل 0791234567.';

  @override
  String get validationPasswordShort => 'استخدم ثمانية أحرف على الأقل.';

  @override
  String get validationPasswordMatch => 'كلمتا المرور غير متطابقتين.';

  @override
  String get validationDateOfBirth => 'أدخل تاريخ ميلادك.';

  @override
  String validationTooLong(int max) {
    return 'هذا أطول من $max حرفاً.';
  }

  @override
  String get validationReturnAfterPickup => 'يجب أن يكون الإرجاع بعد الاستلام.';

  @override
  String get validationChooseReason => 'اختر سبباً.';

  @override
  String get errorAuthInvalidCredentials =>
      'البريد الإلكتروني وكلمة المرور لا يطابقان أي حساب.';

  @override
  String get errorAuthEmailTaken => 'يوجد حساب مسجّل بهذا البريد الإلكتروني.';

  @override
  String get errorAuthPhoneTaken => 'رقم الهاتف هذا مسجّل على حساب آخر.';

  @override
  String get errorAuthInvalidPhone => 'أدخل رقم هاتف أردني، مثل 0791234567.';

  @override
  String get errorAuthInvalidEmail => 'هذا لا يبدو بريداً إلكترونياً.';

  @override
  String get errorAuthWeakPassword => 'اختر كلمة مرور أطول تتضمن حرفاً ورقماً.';

  @override
  String get errorAuthAccountSuspended => 'هذا الحساب موقوف. تواصل مع خضرا.';

  @override
  String get errorAuthEmailNotVerified => 'فعّل بريدك الإلكتروني أولاً.';

  @override
  String get errorAuthUnderage => 'عمرك لا يسمح باستئجار سيارة عبر خضرا.';

  @override
  String get errorAuthInvalidToken =>
      'لم يعد هذا الرابط صالحاً. اطلب رابطاً جديداً.';

  @override
  String get errorAuthTooManyAttempts =>
      'محاولات كثيرة. انتظر بضع دقائق وحاول مجدداً.';

  @override
  String get errorBookingVehicleUnavailable =>
      'حجز شخص آخر تلك السيارة بينما كنت تفكر. جرّب تواريخ أخرى.';

  @override
  String get errorBookingDocumentsIncomplete =>
      'ارفع رخصة القيادة ومستند الهوية قبل الحجز.';

  @override
  String get errorBookingEmailNotVerified => 'فعّل بريدك الإلكتروني قبل الحجز.';

  @override
  String get errorBookingPeriodInPast => 'اختر تواريخ في المستقبل.';

  @override
  String get errorBookingNotFound => 'لم يُعثر على هذا الحجز.';

  @override
  String get errorBookingCannotCancel => 'لم يعد بالإمكان إلغاء هذا الحجز.';

  @override
  String get errorRateLimited => 'طلبات كثيرة. انتظر لحظة وحاول مجدداً.';
}
