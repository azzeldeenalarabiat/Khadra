import { Message } from './language';

/**
 * Every English string the console says, keyed.
 *
 * This file defines the key type; `ar.ts` is typed against it, so a key added here and forgotten
 * there is a compile error rather than an English word sitting in the middle of an Arabic screen.
 *
 * Keys are flat and dotted, prefixed by the screen that owns them. `common.*` is only for wording
 * that is genuinely the same everywhere — a Cancel button — because a shared key is a promise that
 * one word can be changed without checking everywhere else it lands, and in Arabic that promise is
 * usually false: agreement and gender follow the noun.
 *
 * Nothing here is data. These are captions; the figures beside them still come from the API.
 */
export const EN = {
  // The product name. Deliberately the same in both dictionaries -- a brand is not translated --
  // which is why the parity spec exempts it from the identical-text check.
  'app.name': 'Khadra',
  'screen.submitGallery': 'Submit your gallery',

  // The switcher itself. Each language is named in its own script, which is the one convention
  // every language picker follows: you look for the words you can read.
  'lang.switch': 'Switch language',
  'lang.en': 'English',
  'lang.ar': 'العربية',
  'lang.toArabic': 'Switch to Arabic',
  'lang.toEnglish': 'Switch to English',

  // Wording that really is shared.
  'common.cancel': 'Cancel',
  'common.close': 'Close',
  'common.working': 'Working…',
  'common.tryAgain': 'Try again',
  'common.noResponse': 'The service did not respond. Try again shortly.',
  'common.tooManyAttempts': 'Too many attempts. Wait a few minutes before trying again.',

  // Sidebar groups.
  'nav.group.main': 'Main',
  'nav.group.business': 'Business',
  'nav.group.finance': 'Finance',
  'nav.group.operations': 'Operations',
  'nav.group.platform': 'Platform',
  'nav.group.system': 'System',
  'nav.group.team': 'Team',

  // Sidebar items.
  'nav.dashboard': 'Dashboard',
  'nav.dealers': 'Dealers',
  'nav.bookings': 'Bookings',
  'nav.customers': 'Customers',
  'nav.reviews': 'Reviews',
  'nav.finance': 'Finance',
  'nav.payments': 'Payments',
  'nav.payouts': 'Payouts',
  'nav.disputes': 'Disputes',
  'nav.notifications': 'Notifications',
  'nav.cities': 'Cities & Regions',
  'nav.carTypes': 'Car Types',
  'nav.settings': 'Settings',
  'nav.auditLogs': 'Audit Logs',
  'nav.adminUsers': 'Admin Users',
  'nav.security': 'Security',
  'nav.fleet': 'Fleet',
  'nav.employees': 'Employees',
  'nav.dealerProfile': 'Dealer Profile',
  'nav.customerPage': 'Customer page',
  'nav.delivery': 'Delivery',
  'nav.reports': 'Reports',
  'nav.activity': 'Activity',
  'nav.myBusiness': 'My Business',

  // Screen titles that are not simply their nav item.
  'screen.dealerApplication': 'Dealer application',
  'screen.bookingDetails': 'Booking details',
  'screen.customerProfile': 'Customer profile',
  'screen.paymentDetails': 'Payment details',
  'screen.financialDocuments': 'Financial documents',
  'screen.financialDocumentHolds': 'Documents on hold',
  'screen.financialDocument': 'Document details',
  'screen.disputeResolution': 'Dispute resolution',
  'screen.platformSettings': 'Platform settings',
  'screen.addVehicle': 'Add vehicle',
  'screen.submitYourGallery': 'Submit your gallery',
  'screen.vehicleDetails': 'Vehicle details',
  'screen.editVehicle': 'Edit vehicle',
  'screen.publicPreview': 'Public page preview',
  'screen.dispute': 'Dispute',
  'screen.myBusiness': 'My business',
  'screen.dealerProfile': 'Dealer profile',
  'screen.auditLogs': 'Audit logs',
  'screen.adminUsers': 'Admin users',

  // The wordmark: the name on the first line, what the platform is on the second. Two keys rather
  // than a <br> inside one sentence because Arabic breaks in a different place — and because the
  // name itself is NOT translated (see 'app.name'), only the description under it is.
  'brand.line1': 'KHADRA',
  'brand.line2': 'CAR RENTAL SYSTEM',

  // Sidebar chrome.
  'sidebar.sections': 'Admin sections',
  'sidebar.captionDealer': 'Dealer',
  'sidebar.captionEmployee': 'Employee',
  'sidebar.permissions': 'Your permissions',
  'sidebar.permBookings': 'Booking operations',
  'sidebar.permBookingsPaused': 'Booking operations — paused',
  'sidebar.permFleet': 'Fleet access — read only',
  'sidebar.permReports': 'Financial reports',
  'sidebar.permNote': 'Granted by the dealer owner. Enforced server-side.',
  'sidebar.logout': 'Logout',

  // How a person's role is written where the frame shows it.
  'role.admin': 'Administrator',
  'role.dealerOwner': 'Dealer owner',
  'role.dealerEmployee': 'Dealer employee',
  'role.customer': 'Customer',
  'area.employee': 'Employee',

  // Topbar and account menu.
  'topbar.myDealership': 'My dealership',
  'topbar.admin': 'Admin',
  'topbar.accountMenu': 'Account menu for {name}',
  'topbar.yourAccount': 'Your account',
  'topbar.signOut': 'Sign out',
  'topbar.breadcrumb': 'Breadcrumb',

  // Notifications menu.
  'notif.needsAttention': 'Needs your attention',
  'notif.nothingWaiting': 'Nothing waiting',
  'notif.allAnswered': 'Everything inside the platform service windows has been answered.',
  'notif.loadFailed': 'This could not be loaded. Nothing has been missed.',
  'notif.openDashboard': 'Open the dashboard',
  // The aria-label on the bell. English needs two forms; Arabic asks for six, and the dictionary
  // is where that difference lives rather than in a ternary at the call site.
  // The billed length of a rental. The count is the server's frozen figure, never one the browser
  // worked out from the two instants -- those give elapsed time, not calendar days.
  'booking.days': {
    one: '{count} day',
    other: '{count} days',
  },

  'notif.count': {
    zero: 'Nothing needs your attention',
    one: '{count} item needs your attention',
    other: '{count} items need your attention',
  },

  // Screens whose context does not exist yet. The copy names precisely what is missing, so nobody
  // has to guess whether an empty screen is broken or simply not built.
  'notBuilt.badge': 'Not built yet',
  'notBuilt.noData': 'There is no data behind this screen yet',
  'notBuilt.fallback.title': 'Not built yet',
  'notBuilt.fallback.purpose': 'This screen is part of the design but has no data behind it yet.',
  'notBuilt.fallback.blocked': 'The context that would supply it has not been built.',

  'notBuilt.reviews.purpose':
    'Ratings customers leave for dealers and dealers leave for customers, and the moderation queue for them.',
  'notBuilt.reviews.blocked':
    'The Review aggregate exists in the domain but has no table, no repository and no data. Until it does, a dealer with no reviews reads "No reviews yet" rather than showing a rating nobody gave.',
  'notBuilt.notifications.purpose':
    'A feed of what needs an administrator: overdue reviews, breached SLAs, failed jobs.',
  'notBuilt.notifications.blocked':
    'No notification feed has been built. Email is the only channel the platform sends on today.',
  'notBuilt.notifications.instead':
    'The dashboard already ranks what needs attention, by the deadline each item froze.',

  // The dealer's counterpart: built, but not switched on yet.
  'notLive.badge': 'Not live yet',
  'notLive.heading': '{screen} are not live yet',
  'notLive.back': 'Back to dashboard',
  'notLive.reviews.body':
    'Customer reviews are not live yet. When they are, every completed booking lets the customer rate your dealership and lets you rate the customer, and the ratings appear here and on your public page.',
  'notLive.reviews.point1': 'Reviews are tied to completed bookings only — no anonymous ratings.',
  'notLive.reviews.point2':
    'Until then, your public page says "No reviews yet" rather than showing a number.',
  'notLive.reviews.point3': 'Nothing you do now affects a future rating.',

  // The gate every dealer screen sits behind. Each state is a badge, a title, a paragraph and a
  // short list, and the owner and an employee are told different things about the same state:
  // "your customers were notified" is the owner's sentence, and staff can act on none of it.
  'gate.notSubmitted.badge': 'Not submitted',
  'gate.notSubmitted.title': 'Tell us about your gallery',
  'gate.notSubmitted.body':
    'Your owner account is ready. Before you can list cars, the platform has to check that the gallery is a licensed rental office — so we need its details and its papers.',
  'gate.notSubmitted.listTitle': 'What you will need',
  'gate.notSubmitted.point1': "The gallery's name and commercial registration number.",
  'gate.notSubmitted.point2': 'Where it is, and the hours it opens.',
  'gate.notSubmitted.point3':
    'Three documents: the commercial registration, proof of green-plate vehicle registration, and your own ID.',
  'gate.notSubmitted.submit': 'Submit my gallery',
  'gate.accountSettings': 'Account settings',

  'gate.noDealership.badge': 'No access',
  'gate.noDealership.title': 'You are not part of a dealership',
  'gate.noDealership.body':
    'No dealership on the platform currently lists you as a member of staff. If you were expecting access, the owner of the gallery you work for can restore it from their Employees screen.',

  'gate.unreachable.badge': 'Unavailable',
  'gate.unreachable.title': 'We could not load your dealership',
  'gate.unreachable.body':
    'Your console is not showing because the platform could not confirm your dealership’s standing just now. Nothing about your bookings or your fleet has changed.',

  'gate.suspended.badge': 'Suspended',
  'gate.suspended.titleOwner': 'Your dealer account is suspended',
  'gate.suspended.titleStaff': '{name} is suspended',
  'gate.suspended.bodyOwner':
    '{name} cannot take new bookings or change fleet data while suspended. Rentals already under way continue, and your customers were notified by the platform.',
  'gate.suspended.bodyStaff':
    '{name} cannot take new bookings while it is suspended, so the console is closed to you apart from the rentals already under way. Those continue, and handing those cars back is still yours to record.',
  'gate.suspended.listTitle': 'What still works',
  'gate.suspended.point1': 'Cars already out on rental can still be handed back and recorded.',
  'gate.suspended.point2': 'New booking requests, fleet edits and staff changes are blocked.',
  'gate.suspended.reason': 'Reason on file: {reason}',
  'gate.suspended.noReason': 'No reason was recorded with the suspension.',
  'gate.suspended.goToBookings': 'Go to bookings',

  'gate.rejected.badge': 'Rejected',
  'gate.rejected.title': 'Your dealer application was rejected',
  'gate.rejected.body':
    '{name} is not approved to operate on the platform, so the dealer console is unavailable. You can correct your details and submit the application again.',
  'gate.rejected.listTitle': 'Reviewer notes',
  'gate.rejected.noNote': 'No note was recorded with the decision.',
  'gate.rejected.reapply': 'You may update your dealer page and reapply at any time.',

  'gate.clarification.badge': 'Clarification needed',
  'gate.clarification.title': 'The platform needs something from you',
  'gate.clarification.body':
    'An administrator reviewed {name}’s application and sent it back. Fix what they asked for on your dealer page, then resubmit.',
  'gate.clarification.listTitle': 'What they asked',
  'gate.clarification.noNote': 'No note was recorded with the request.',
  'gate.clarification.stillEditable': 'Your dealer page stays editable while this is open.',

  'gate.pending.badge': 'Pending review',
  'gate.pending.title': 'Your application is being reviewed',
  'gate.pending.body':
    "{name} was submitted and is waiting for the platform's licence check. Nothing else is needed from you right now; you will be able to list cars the moment it is approved.",
  'gate.pending.listTitle': 'What happens next',
  'gate.pending.due': 'A decision is due by {due}.',
  'gate.pending.prepare': 'You can prepare your dealer page in the meantime.',
  'gate.openDealerPage': 'Open my dealer page',

  // Sign in.
  'auth.signIn.title': 'Sign in',
  'auth.signIn.subtitle': 'For platform administrators and rental offices.',
  'auth.signIn.submit': 'Sign in',
  'auth.signIn.busy': 'Signing in…',
  'auth.signIn.forgot': 'Forgot password?',
  'auth.signIn.registerCta': 'Run a rental office? Register your gallery',

  // What sign-in says when it refuses. Each is a title and a sentence, because a banner that only
  // names the problem leaves the person guessing what to do about it.
  'auth.signIn.needBoth.title': 'Enter your email and password',
  'auth.signIn.needBoth.text': 'Both are needed to sign in.',
  'auth.signIn.invalid.title': 'Invalid email or password',
  'auth.signIn.invalid.text': 'Check both and try again.',
  'auth.signIn.suspended.title': 'Your account has been suspended',
  'auth.signIn.suspended.text':
    'Contact support to have it reviewed. Resetting your password will not restore access.',
  'auth.signIn.wrongConsole.title': 'This is the Khadra business console',
  'auth.signIn.wrongConsole.text': 'Customer accounts sign in on the Khadra website or in the Khadra app.',
  'auth.signIn.unverified.title': 'Verify your email address first',
  'auth.signIn.unverified.text':
    'We sent a verification link when the account was created. Open it, then sign in.',
  'auth.signIn.unverified.action': 'Send a new link',
  'auth.signIn.rateLimited.title': 'Too many attempts',
  'auth.signIn.rateLimited.vague':
    'Too many sign-in attempts from this network. Wait a few minutes before trying again.',
  // Only said when the server actually sends Retry-After; the minute count is never invented here.
  'auth.signIn.rateLimited.minutes': {
    one: 'Try again in about a minute.',
    other: 'Try again in about {count} minutes.',
  },
  'auth.signIn.unavailable.title': 'Sign-in is unavailable',
  'auth.signIn.unavailable.text':
    'The service did not respond. Nothing about your account has changed; try again shortly.',

  // Field labels shared by the auth cards.
  'auth.field.email': 'Email',
  'auth.field.password': 'Password',
  'auth.field.newPassword': 'New password',
  'auth.field.fullName': 'Full name',
  'auth.field.mobile': 'Mobile number',
  'auth.field.yourEmail': 'Your email address',
  'auth.backToSignIn': 'Back to sign in',

  // Forgot password.
  'auth.forgot.title': 'Reset your password',
  'auth.forgot.subtitle': 'We will email you a link to choose a new one.',
  'auth.forgot.submit': 'Email me a reset link',
  'auth.forgot.busy': 'Sending…',
  'auth.forgot.sentTitle': 'Check your email',
  'auth.forgot.sentBody':
    'If that address has an account, a reset link is on its way. The link expires in one hour.',
  'auth.forgot.spamHint': 'Nothing arrived? Check the spam folder, then request another link.',
  'auth.forgot.needEmail': 'Enter the email address on your account.',
  'auth.forgot.notSent':
    'We could not send the reset link just now — the mail service refused it. Your account is fine and nothing has changed. Try again in a few minutes.',

  // Reset password.
  'auth.reset.incompleteTitle': 'This link is incomplete',
  'auth.reset.incompleteBody': 'Open the reset link straight from the email, or request a new one.',
  'auth.reset.doneTitle': 'Password changed',
  'auth.reset.doneBody': 'Every other session has been signed out. Taking you back to sign in…',
  'auth.reset.title': 'Choose a new password',
  'auth.reset.subtitle': 'Pick something other than the password you have now.',
  'auth.reset.submit': 'Set new password',
  'auth.reset.busy': 'Saving…',

  // Accept an invitation.
  'auth.invite.incompleteTitle': 'This link is incomplete',
  'auth.invite.incompleteBody':
    'Open the invitation link straight from the email, or ask the dealer owner to send it again.',
  'auth.invite.doneTitle': 'You are in',
  'auth.invite.doneBody': 'Your account is ready. Taking you to sign in…',
  'auth.invite.title': 'Accept your invitation',
  'auth.invite.subtitle': 'Choose the password you will sign in with.',
  'auth.invite.submit': 'Accept and continue',
  'auth.invite.busy': 'Setting up…',

  // Register a gallery.
  'auth.register.title': 'Register your gallery',
  'auth.register.subtitle':
    'This creates your owner account. You submit the gallery and its papers after signing in.',
  'auth.register.submit': 'Create account',
  'auth.register.busy': 'Creating your account…',
  'auth.register.sentTitle': 'Confirm your email address',
  'auth.register.sentBody':
    'Your account is created. We sent a verification link to {email} — open it, and you can sign in.',
  'auth.register.notSentTitle': 'Your account is created',
  'auth.register.notSentBody':
    'We could not send the verification email to {email} just now. Nothing is wrong with your account — it is saved and waiting.',
  'auth.register.notSentBanner': 'No email was sent',
  'auth.register.notSentHint':
    'Check the address is right, then ask for a new link. You cannot sign in until the address is confirmed.',
  'auth.register.sendLink': 'Send a verification link',
  'auth.register.galleryNextTitle': 'Your gallery comes next',
  'auth.register.galleryNextBody':
    'Once you sign in, you submit the gallery itself — its name, its commercial registration and its licence papers. An administrator checks them before you can list cars.',
  // Why a registration was refused. Keyed off the server's stable `code`; the sentence is ours.
  'auth.register.err.noResponse':
    'The service did not respond. Nothing was created; try again shortly.',
  'auth.register.err.emailTaken':
    'An account already exists for this email address. Sign in instead, or use another address.',
  'auth.register.err.phoneTaken': 'An account already exists for this phone number.',
  'auth.register.err.invalidPhone':
    'Enter a Jordanian mobile number, as 07XXXXXXXX or +9627XXXXXXXX.',
  'auth.register.err.rateLimited':
    'Too many attempts from this network. Wait a few minutes before trying again.',
  'auth.register.err.rejected': 'The details were rejected. Check them and try again.',

  'auth.register.goToSignIn': 'Go to sign in',
  'auth.register.alreadyHave': 'Already have an account? Sign in',

  // Verify an email address.
  'auth.verify.title': 'Verify your email address',
  'auth.verify.subtitle':
    'You cannot sign in until the address on your account is confirmed. If the link never arrived or has since expired, we will send another.',
  'auth.verify.inboxTitle': 'Check your inbox',
  'auth.verify.inboxBody':
    'If an unverified account exists for that address, a new verification link is on its way.',
  'auth.verify.submit': 'Send a verification link',
  'auth.verify.busy': 'Sending…',
  'auth.verify.verifyingTitle': 'Verifying your email…',
  'auth.verify.verifyingBody': 'This takes a moment.',
  'auth.verify.verifiedTitle': 'Your email address is verified',
  'auth.verify.verifiedBody': 'You can sign in now.',
  'auth.verify.failedTitle': 'That link did not work',
  'auth.verify.sendNewTo': 'Send a new link to',
  'auth.verify.sendNew': 'Send a new link',
  'auth.verify.err.invalidToken':
    'This link is no longer valid. A verification link can only be used once, and expires if it is left too long.',
  'auth.verify.err.notSent':
    'We could not send the email just now — the mail service refused it. Nothing is wrong with your account; try again in a few minutes.',
  'auth.verify.needEmail': 'Enter the email address you registered with.',

  // Copy that appears on more than one screen, folded onto one key so the Arabic matches.
  'common.account': 'Account',
  'common.addVehicle': 'Add vehicle',
  'common.approve': 'Approve',
  'common.auditLog': 'audit log',
  'common.backToBookings': 'Back to bookings',
  'common.backToFleet': 'Back to fleet',
  'common.booking': 'Booking',
  'common.bookings': 'Bookings',
  'common.businessInformation': 'Business information',
  'common.changePassword': 'Change password',
  'common.changingItSignsYou': 'Changing it signs you out everywhere else.',
  'common.closesAt': 'Closes at',
  'common.colour': 'Colour',
  'common.confirmNewPassword': 'Confirm new password',
  'common.contact': 'Contact',
  'common.coordinates': 'Coordinates',
  'common.couldntLoadThisBooking': "Couldn't load this booking",
  'common.couldntLoadThisDispute': "Couldn't load this dispute",
  'common.couldntLoadYourDashboard': "Couldn't load your dashboard",
  'common.currentPassword': 'Current password',
  'common.deactivate': 'Deactivate',
  'common.decisionRecordedNoFunds':
    "Decision recorded. The customer's share is refunded to their original payment method automatically; what the platform keeps and what goes to the office are settled by hand, because no payout rail exists yet.",
  'common.delivery': 'Delivery',
  'booking.depositRefundInitiated': 'Refund of {amount} to the customer initiated (free cancellation)',
  'booking.depositRefunded': '{amount} refunded to the customer (free cancellation)',
  'booking.depositRefundDelayed': 'Refund of {amount} to the customer delayed — still owed, retrying',
  'adminBooking.depositRefund': 'Deposit refund',
  'adminBooking.paymentRefund': 'Payment refund',
  // Phase 3 (2026-09-26): every refund, with its reason and where it is, on both consoles.
  'booking.refundReason.FreeCancellation': 'Refund — free cancellation',
  'booking.refundReason.PlatformCancellation': 'Refund — cancelled by Khadra',
  'booking.refundReason.EndedBeforePickup': 'Refund — paid above the deposit',
  'booking.refundReason.DisputeWindowClosed': 'Deposit returned — dispute window closed',
  'booking.refundReason.DisputeResolution': 'Refund — dispute decision',
  'booking.refundReason.OrphanedCapture': 'Refund — payment that could not be applied',
  'booking.refundReason.other': 'Refund',
  'booking.refundInitiatedTo': 'Refund of {amount} to the customer initiated',
  'booking.refundedTo': '{amount} refunded to the customer',
  'booking.refundDelayedTo': 'Refund of {amount} to the customer delayed — still owed, retrying',
  'adminBooking.refundedTotal': 'Refunded to the customer',
  'adminBooking.refundOutstanding': 'Refund in progress',
  'adminBooking.cancelRefundsWholePayment':
    'If the customer has paid, the whole payment, the deposit included, goes back to their original payment method. Nothing is charged to anyone.',
  'adminBooking.cancelRefundsAmount':
    'The customer paid {amount}. All of it, the deposit included, goes back to their original payment method. Nothing is charged to anyone.',
  'adminBooking.noShowRefundsAboveDeposit':
    'They paid in full, so everything above the deposit goes back to their original payment method; the deposit stays held.',
  'common.deposit': 'Deposit',
  'common.depositHeld': 'Deposit held',
  'common.description': 'Description',
  /** Heads one language of a bilingual value that is shown RAW, with no fallback. */
  'common.descriptionIn': 'Description ({language})',
  'common.disputed': 'Disputed',
  'common.documentsOnFile': 'Documents on file',
  'common.evidenceOptional': 'Evidence (optional)',
  'common.fuel': 'Fuel',
  'common.fuelPolicy': 'Fuel policy',
  'common.handovers': 'Handovers',
  'common.keptByThePlatform': 'Kept by the platform',
  'common.latitude': 'Latitude',
  'common.longitude': 'Longitude',
  'common.makeCover': 'Make cover',
  'common.model': 'Model',
  'common.money': 'Money',
  'common.newPassword': 'New password',
  'common.next': 'Next',
  'common.openBooking': 'Open booking',
  'common.openDealer': 'Open dealer',
  'common.openDispute': 'Open dispute',
  'common.opensAt': 'Opens at',
  'common.penaltyAssessed': 'Penalty assessed',
  'common.period': 'Period',
  'common.plateNumber': 'Plate number',
  'common.previous': 'Previous',
  'common.reactivate': 'Reactivate',
  'common.refundedToTheCustomer': 'Refunded to the customer',
  'common.reject': 'Reject',
  'common.remove': 'Remove',
  'common.retry': 'Retry',
  'common.role': 'Role',
  'common.seats': 'Seats',
  'common.securityDepositJod': 'Security deposit (JOD)',
  'common.settings': 'Settings',
  'common.showAll': 'Show all',
  'common.signsOutEveryOther': 'Signs out every other session',
  'common.state': 'State',
  'common.statements': 'Statements',
  'common.status': 'Status',
  'common.theTwoPasswordsDo': 'The two passwords do not match.',
  'common.transmission': 'Transmission',
  'common.value': 'Value',
  'common.vehicleType': 'Vehicle type',
  'common.whereItIs': 'Where it is',
  'common.year': 'Year',

  // Admin dashboard.
  'adminDashboard.dashboard': 'Dashboard',
  'adminDashboard.overviewOfPlatformActivity':
    'Overview of platform activity and items requiring attention.',

  // Dealers list.
  'dealersList.clearTheSearchOr': 'Clear the search or choose a different status.',
  'dealersList.couldntLoadTheDealer': "Couldn't load the dealer queue",
  'dealersList.dealers': 'Dealers',
  'dealersList.noDealersMatchThis': 'No dealers match this view',
  'dealersList.rentalOfficesOnThe':
    'Rental offices on the platform. Applications awaiting a decision come first, closest to the review deadline at the top.',
  'dealersList.searchDealerOrRegistration': 'Search dealer or registration no.',
  'dealersList.suspended': 'Suspended',

  // Dealer application review.
  'dealerReview.applicationTimeline': 'Application timeline',
  'dealerReview.approvalSla': 'Approval SLA',
  'dealerReview.approveDealer': 'Approve dealer',
  'dealerReview.couldntLoadThisApplication': "Couldn't load this application",
  'dealerReview.noReasonWasRecorded': 'No reason was recorded with this decision.',
  'dealerReview.reactivateDealer': 'Reactivate dealer',
  'dealerReview.rejectApplication': 'Reject application',
  'dealerReview.requestClarification': 'Request clarification',
  'dealerReview.suspendDealer': 'Suspend dealer',
  'dealerReview.whereItIs': 'Where it is',
  'dealerReview.noAddressRecorded':
    'No address recorded. The pin below is the location the applicant gave.',
  'dealerReview.verificationDocuments': 'Verification documents',

  // Bookings list (admin).
  'bookingsList.bookingReferenceEG': 'Booking reference, e.g. KH-3G96Q4L8',
  'bookingsList.clearTheSearchOr':
    'Clear the search or choose a different tab. Bookings are made by customers in the app; the platform does not create them.',
  'bookingsList.couldntLoadTheBookings': "Couldn't load the bookings",
  'bookingsList.everyBookingOnThe':
    'Every booking on the platform, with the price and terms each one was made under.',
  'bookingsList.noBookingMatchesThis': 'No booking matches this view',
  'bookingsList.open': 'Open',
  'bookingsList.parties': 'Parties',
  'bookingsList.reference': 'Reference',
  'bookingsList.rental': 'Rental',
  'bookingsList.searchByBookingReference': 'Search by booking reference',
  'bookingsList.showAllBookings': 'Show all bookings',
  'bookingsList.total': 'Total',
  'bookingsList.vehicle': 'Vehicle',

  // Booking details (admin).
  'adminBooking.assessedNotChargedMoney':
    'Assessed, not charged. A penalty becomes money only when an administrator resolves a dispute.',
  // Where a penalty stands (payments Phase 8): a customer's penalty of the whole deposit is kept when the window closes.
  'penaltyStanding.keptFromDeposit': 'Kept from the deposit: the dispute window closed with no dispute.',
  'penaltyStanding.resolvedByDispute': 'Resolved through a dispute: its decision is what this assessment became.',
  'penaltyStanding.keptUnlessDisputed':
    'Assessed, not charged yet. It is kept from the deposit when the dispute window closes, unless a dispute decides otherwise.',
  'adminBooking.cancelBooking': 'Cancel booking',
  'adminBooking.expireBooking': 'Expire booking',
  'adminBooking.frozenWhenTheBooking': 'Frozen when the booking was made',
  'adminBooking.history': 'History',
  'adminBooking.markAsNoShow': 'Mark as no-show',
  'adminBooking.noneOfTheseMoves':
    "None of these charges anyone. A cancellation by the platform assesses no penalty against either party and returns the customer's whole payment, the deposit included, if they paid; a no-show returns what they paid above the deposit. The two deadline actions are refused while this booking's own window still has time in it. Every one is attributed to you in the",
  'adminBooking.nothingIsOwed': 'Nothing is owed',
  'adminBooking.openCustomer': 'Open customer',
  'adminBooking.parties': 'Parties',
  'adminBooking.platformActions': 'Platform actions',
  'adminBooking.termsItWasMade': 'Terms it was made under',
  'adminBooking.thisBookingHasReached':
    'This booking has reached a state the platform does not step into. Its record stays exactly as it is; what happened to it is in the',

  // Customers list.
  'customersList.clearTheSearchOr':
    'Clear the search or choose a different filter. Customers register in the app; the platform does not create them.',
  'customersList.customer': 'Customer',
  'customersList.customers': 'Customers',
  'customersList.documents': 'Documents',
  'customersList.lastSeen': 'Last seen',
  'customersList.noCustomerMatchesThis': 'No customer matches this view',
  'customersList.open': 'Open',
  'customersList.searchNameEmailOr': 'Search name, email or phone',
  'customersList.thePeopleWhoRent':
    'The people who rent, their verification state and their history with the platform.',

  // Customer profile.
  'customerProfile.accountStatus': 'Account status',
  'customerProfile.backToCustomers': 'Back to customers',
  'customerProfile.couldntLoadThisCustomer': "Couldn't load this customer",
  'customerProfile.nothingUploadedYet': 'Nothing uploaded yet',
  'customerProfile.reactivateAccount': 'Reactivate account',
  'customerProfile.suspendAccount': 'Suspend account',
  'customerProfile.suspendingEndsEverySession':
    'Suspending ends every session at once. It does not cancel bookings already made, and it moves no money. The decision is attributed to you in the',
  'customerProfile.theirBookings': 'Their bookings',
  'customerProfile.thePlatformHoldsThese':
    'Khadra never marks a document as verified. Open one only on the way to deciding whether it must be replaced: every opening is recorded with your name and the time.',
  'customerProfile.thisCustomerCannotComplete':
    'This customer cannot complete a booking until their licence and identity are on file.',
  'customerProfile.whichRecordsTheAccount':
    ', which records the account by reference rather than by name.',

  // Disputes list.
  'disputesList.backToTheLive': 'Back to the live queue',
  'disputesList.couldntLoadTheDispute': "Couldn't load the dispute queue",
  'disputesList.disputes': 'Disputes',
  'disputesList.overdueOnly': 'Overdue only',
  'disputesList.parties': 'Parties',
  'disputesList.raisedBy': 'Raised by',
  'disputesList.reason': 'Reason',
  'disputesList.sla': 'SLA',
  'disputesList.ticket': 'Ticket',
  'disputesList.ticketsBetweenACustomer':
    'Tickets between a customer and a dealer. Money only moves when one of these is resolved.',

  // Dispute resolution.
  'disputeDetail.adminNoteRequired': 'Admin note (required)',
  'disputeDetail.adminResolution': 'Admin resolution',
  'disputeDetail.age': 'Age',
  'disputeDetail.backToTheQueue': 'Back to the queue',
  'disputeDetail.bothPartiesAndThe': 'Both parties and the platform read everything here',
  'disputeDetail.caseTimeline': 'Case timeline',
  'disputeDetail.chargedToTheDealer': 'Charged to the dealer',
  'disputeDetail.closedWithoutADecision': 'Closed without a decision',
  'disputeDetail.decisionRecorded': 'Decision recorded',
  'disputeDetail.evidence': 'Evidence',
  'disputeDetail.explainTheDecisionAnd': 'Explain the decision and the evidence it rests on…',
  'disputeDetail.overdue': 'Overdue',
  'disputeDetail.platformSla': 'Platform SLA',
  'disputeDetail.resolvingRecordsTheDecision':
    "Resolving records the decision. The customer's share is refunded to their original payment method automatically; what the platform keeps and what goes to the office are settled by hand.",
  'disputeDetail.takeThisOn': 'Take this on',
  'disputeDetail.theDecisionYourNote':
    "The customer is shown the whole decision as Khadra's: your note and the time, never your name. The rental office is shown only its own share and any charge to it, with your note, your name and the time, and all of it is written to the audit log. So leave the customer's and the platform's shares out of the note.",
  'disputeDetail.thisTicketWasWithdrawn':
    'This ticket was withdrawn by the party who opened it, so the booking settles as if no dispute had been raised.',
  'disputeDetail.transferredToTheDealer': 'Transferred to the dealer',

  // Audit log.
  'auditLog.action': 'Action',
  'auditLog.allActions': 'All actions',
  'auditLog.allRecords': 'All records',
  'auditLog.anyone': 'Anyone',
  'auditLog.auditLogs': 'Audit logs',
  'auditLog.change': 'Change',
  'auditLog.clear': 'Clear',
  'auditLog.clearFilters': 'Clear filters',
  'auditLog.filtersDoNotWork': 'Those filters do not work together',
  'auditLog.checkTheDates': 'Check the dates — the end of the range falls before the start.',
  'auditLog.correlation': 'Correlation',
  'auditLog.couldntLoadTheAudit': "Couldn't load the audit log",
  'auditLog.dealerBookingReferenceOr': 'Dealer, booking reference or person…',
  'auditLog.everyPrivilegedActionOn':
    'Every privileged action on the platform, who took it, and on what grounds. Entries cannot be edited or removed — by anyone, including an administrator.',
  'auditLog.from': 'From',
  'auditLog.newValue': 'New value',
  'auditLog.previousValue': 'Previous value',
  'auditLog.reasonGiven': 'Reason given',
  'auditLog.record': 'Record',
  'auditLog.recordedAt': 'Recorded at',
  'auditLog.search': 'Search',
  'auditLog.to': 'To',
  'auditLog.when': 'When',
  'auditLog.who': 'Who',

  // Admin users.
  'adminUsers.administrator': 'Administrator',
  'adminUsers.adminUsers': 'Admin users',
  'adminUsers.couldntLoadTheAdministrators': "Couldn't load the administrators",
  'adminUsers.inviteAdministrator': 'Invite administrator',
  'adminUsers.lastSignedIn': 'Last signed in',
  'adminUsers.recordedActions': 'Recorded actions',
  'adminUsers.theLastActiveAdministrator':
    'The last active administrator cannot be deactivated, and nobody can deactivate their own account: nothing in the platform creates an administrator except another administrator, so an empty list would be permanent. Deactivating does not delete the account — that would burn the email address for ever.',
  'adminUsers.whoCanAdministerThe':
    'Who can administer the platform. There is one administrator role, and it can do everything this console can.',
  'adminUsers.you': 'You',

  // Cities and car types.
  'lookups.arabic': 'Arabic',
  'lookups.centre': 'Centre',
  'lookups.couldntLoadTheList': "Couldn't load the list",
  'lookups.english': 'English',
  'lookups.nothingOnThisList': 'Nothing on this list yet',
  'lookups.order': 'Order',
  'lookups.rename': 'Rename',
  'lookups.restore': 'Restore',
  'lookups.retire': 'Retire',
  'lookups.thereIsNoDelete':
    'There is no delete. Vehicles and bookings reference these entries by id across bounded contexts, so nothing can warn you what removing one would break; retiring keeps every existing record readable while taking the entry off new listings and searches.',

  // Platform settings.
  'platformSettings.aPenaltyIsWhat':
    'A penalty is what the platform records as owed. Nothing is taken from anyone until an administrator resolves a dispute, and the dealer tier above is one of the figures the owner has still to settle.',
  'platformSettings.assessedNeverCharged': 'Assessed, never charged',
  'platformSettings.couldntLoadTheSettings': "Couldn't load the settings",
  'platformSettings.notFromARecord':
    ', not from a record an administrator can edit: the settings aggregate is not wired to a table yet, and two of the penalties below are still open business decisions. A form here would let an administrator settle one of those by typing into a box.',
  'platformSettings.penalties': 'Penalties',
  'platformSettings.platformSettings': 'Platform settings',
  'platformSettings.theNumbersTheWhole':
    'The numbers the whole platform runs on. Every booking freezes the ones in force when it was made, so changing one here can never re-judge a booking that already exists.',
  'platformSettings.theseValuesAreRead': 'These values are read-only here. They come from',
  'platformSettings.whoMayRent': 'Who may rent',
  'platformSettings.windowsAndDeadlines': 'Windows and deadlines',

  // Security.
  'security.blockedSignIns': 'Blocked sign-ins.',
  'security.couldntLoadYourSessions': "Couldn't load your sessions",
  'security.endSession': 'End session',
  'security.noSessionsOnRecord': 'No sessions on record',
  'security.notBuilt': 'Not built',
  'security.notBuiltAPassword':
    'Not built. A password and the email that proves the address are the only factors today.',
  'security.security': 'Security',
  'security.sessionsAppearHereOnce': 'Sessions appear here once you have signed in.',
  'security.sessionsAreRecordedIndividual':
    'Sessions are recorded; individual sign-in attempts, successful or not, are not logged anywhere, so there is no history to show.',
  'security.signedInOn': 'Signed in on',
  'security.signInHistory': 'Sign-in history.',
  'security.thereIsNoLockout':
    'There is no lockout and no attempt log, so nothing counts failed attempts or blocks an address.',
  'security.thisScreenShowsYour':
    "This screen shows your own account only. Another administrator's devices and addresses are not something the platform lets anyone read.",
  'security.twoFactorAuthentication': 'Two-factor authentication.',
  'security.yourAccount': 'Your account',
  'security.yourOwnAccountYour': 'Your own account: your password and where you are signed in.',

  // Dealer dashboard.
  'dealerDashboard.addVehicle': 'Add Vehicle',
  'dealerDashboard.viewBookings': 'View Bookings',

  // Dealer bookings list.
  'dealerBookings.couldntLoadYourBookings': "Couldn't load your bookings",
  'dealerBookings.customer': 'Customer',
  'dealerBookings.customersTotal': "customer's total",
  'dealerBookings.everyBookingForYour':
    'Every booking for your dealership. Scoped server-side to your dealer.',
  'dealerBookings.rentalPeriod': 'Rental period',
  'dealerBookings.vehicle': 'Vehicle',
  'dealerBookings.view': 'View',
  'dealerBookings.viewFleet': 'View fleet',

  // Dealer booking details.
  'dealerBooking.actorRecordedOnEvery': 'Actor recorded on every step',
  'dealerBooking.amount': 'Amount',
  'dealerBooking.answerWithin': 'Answer within',
  'dealerBooking.assessedNotChargedMoney':
    'Assessed, not charged. Money only moves when the platform resolves a dispute ticket.',
  'dealerBooking.attributedTo': 'Attributed to',
  'dealerBooking.commission': 'Commission',
  // Payments Phase 8: the ledger works the payout out, so the net IS shown above this note. It names no payment
  // method: how an office is paid waits on the acquiring arrangement (owner, 2026-09-24).
  'dealerBooking.commissionIsDeductedFrom':
    'Commission is deducted from the card payment at the rate frozen when this booking was made, and never more than the booking brings you. What Khadra owes you, and each payment of it, is recorded on your Payouts page.',
  'dealerBooking.customer': 'Customer',
  'dealerBooking.history': 'History on Khadra',
  'dealerBooking.historyHint': 'What the platform recorded. Not shared by other galleries.',
  'dealerBooking.noHistory':
    'No history on Khadra yet. This is their first booking on the platform.',
  'dealerBooking.historyClosed':
    'A customer\u2019s history is shown only while this booking is live.',
  'dealerBooking.ratedByGalleries': 'Rated by galleries',
  'dealerBooking.notRatedYet': 'Not rated yet',
  'dealerBooking.completedRentals': 'Completed rentals',
  'dealerBooking.noShows': 'No-shows',
  'dealerBooking.lateCancellations': 'Late cancellations',
  'dealerBooking.disputesAgainst': 'Disputes decided against them',
  'dealerBooking.customerSince': 'On Khadra since',
  'dealerBooking.rateCustomer': 'Rate this customer',
  'dealerBooking.rateCustomerHint':
    'A score only \u2014 there is no comment. Other galleries see the average, never who gave it. Yours stays hidden until the customer rates you or the window closes.',
  'dealerBooking.youRated': 'You rated this customer',
  'dealerBooking.rateSaved': 'Rating saved.',
  'dealerBooking.financial': 'Financial',
  'dealerBooking.freeCancellation': 'Free cancellation',
  'dealerBooking.openADispute': 'Open a dispute',
  'dealerBooking.openVehicle': 'Open vehicle',
  'dealerBooking.photosOrAPdf':
    'Photos or a PDF. Stored privately; only the two parties and the platform can open them.',
  'dealerBooking.pickup': 'Pickup',
  'dealerBooking.recordPickup': 'Record pickup',
  'dealerBooking.recordReturn': 'Record return',
  // The pickup and return windows (Wave 3 D4, owner 2026-10-05): the server sends each moment; the console only says it.
  'dealerBooking.pickupFrom': 'Pickup can be recorded from {when}.',
  'dealerBooking.returnFrom': 'The return can be recorded from {when}, when the rental starts.',
  'dealerBooking.rental': 'Rental',
  'dealerBooking.rulesVersion': 'Rules version',
  'dealerBooking.sayWhatHappenedWith':
    'Say what happened, with times. Both parties and the platform will read this.',
  'dealerBooking.settlementWindow': 'Settlement window',
  'dealerBooking.termsFrozenOnThis': 'Terms frozen on this booking',
  'dealerBooking.thePlatformAnswersWithin':
    'The platform answers within its SLA',
  'dealerBooking.timeline': 'Timeline',
  'dealerBooking.vehicle': 'Vehicle',
  'dealerBooking.whatWentWrong': 'What went wrong',
  'dealerBooking.yourDealershipCannotTrade':
    'Your dealership cannot trade right now, so this request cannot be answered.',

  // Dealer employees.
  'dealerEmployees.accountSettings': 'Account settings',
  'dealerEmployees.active': 'Active',
  'dealerEmployees.couldntLoadYourStaff': "Couldn't load your staff",
  'dealerEmployees.deactivated': 'Deactivated',
  'dealerEmployees.employee': 'Employee',
  'dealerEmployees.employees': 'Employees',
  'dealerEmployees.everyEmployeeCanApprove':
    'Every employee can approve and reject requests and record pickups and returns. Report access is the only extra permission, and it is per person.',
  'dealerEmployees.hasNotSetA': 'Has not set a password yet',
  'dealerEmployees.invited': 'Invited',
  'dealerEmployees.inviteEmployee': 'Invite employee',
  'dealerEmployees.inviteThePeopleWho':
    'Invite the people who hand over cars for you. They approve requests, record pickups and returns, and only see revenue if you say so.',
  'dealerEmployees.lastSignIn': 'Last sign-in',
  'dealerEmployees.noStaffYet': 'No staff yet',
  'dealerEmployees.openMyDealerPage': 'Open my dealer page',
  'dealerEmployees.reportAccess': 'Report access',
  'dealerEmployees.resendInvite': 'Resend invite',
  'dealerEmployees.staffWhoActOn':
    'Staff who act on bookings for your dealership. Every action they take is recorded under their own name.',

  // Delivery settings.
  'dealerDelivery.1Km': '1 km',
  'dealerDelivery.aBookingFreezesThe':
    'A booking freezes the fee it was made under. Changing the radius never changes an existing booking.',
  'dealerDelivery.chargedToTheCustomer':
    'Charged to the customer on every delivery booking you take. It is not part of the deposit and the platform takes no commission on it. Unless the customer paid the whole booking online, your driver collects it in cash at handover, on top of the rental balance.',
  'dealerDelivery.couldntLoadDeliverySettings': "Couldn't load delivery settings",
  'dealerDelivery.customersInsideYourRadius':
    'Customers inside your radius can ask for the car to be brought to them. A booking outside the radius is only offered as pickup.',
  'dealerDelivery.deliveryRadius': 'Delivery radius',
  'dealerDelivery.deliveryRadiusInKilometres': 'Delivery radius in kilometres',
  'dealerDelivery.exactRadiusKm': 'Exact radius (km)',
  'dealerDelivery.howTheRadiusIs': 'How the radius is used',
  'dealerDelivery.offerDelivery': 'Offer delivery',
  'dealerDelivery.carsNotOfferedTitle': 'Your listed cars are not offered for delivery',
  'dealerDelivery.carsNotOfferedBody':
    '{count} of your listed car(s) can only be collected from your location, because they were saved before you offered delivery. Customers will not see them as deliverable until you change that.',
  'dealerDelivery.offerOnListedCars': 'Offer delivery on these cars',
  'dealerDelivery.offerOnListedCarsHint':
    'You can still switch delivery off for any single car from its own page afterwards.',
  'dealerDelivery.offeringOnListedCars': 'Updating your cars\u2026',
  'dealerDelivery.offeredOnListedCars': 'Delivery is now offered on {count} car(s).',
  'dealerDelivery.saving': 'Saving\u2026',
  'dealerDelivery.saveSettings': 'Save delivery settings',
  'dealerDelivery.couldntLoadNothingChanged':
    'Delivery settings could not be loaded. Nothing has been changed.',
  'dealerDelivery.radiusMustBeBetween': 'The radius must be between 0 and {max} km.',
  'dealerDelivery.serviceDidNotRespond': 'The service did not respond. Nothing has been changed.',
  'dealerDelivery.switchedOnTitle': 'Delivery switched on',
  'dealerDelivery.switchedOnBody':
    'Customers within {radius} km of your location can ask for delivery.',
  'dealerDelivery.switchedOffTitle': 'Delivery switched off',
  'dealerDelivery.switchedOffBody': 'Customers will collect from your location only.',
  'dealerDelivery.unsavedChanges': 'Unsaved changes',
  'dealerDelivery.whenACustomerDrops':
    'When a customer drops a pin, the platform measures the straight-line distance from your dealer location. Inside the radius, delivery is offered on your cars; outside, pickup only.',
  'dealerDelivery.whetherYouDeliverHow':
    'Whether you deliver, how far, and what you charge for it.',
  'dealerDelivery.yourDeliveryFee': 'Your delivery fee',
  'dealerDelivery.yoursToSetEnter': 'Yours to set. Enter 0 if you deliver free of charge.',

  // The customer page: what an office writes for its customers, and what of it it shows.
  'dealerCustomerPage.title': 'Customer page',
  'dealerCustomerPage.subtitle':
    'What you tell customers in your own words, and which of it they see.',
  'dealerCustomerPage.about': 'About',
  'dealerCustomerPage.aboutHint': 'Who you are, in a sentence or two',
  'dealerCustomerPage.aboutPlaceholder':
    'What you rent, what you are known for, anything a customer should know before booking.',
  'dealerCustomerPage.rentalConditions': 'Rental conditions',
  'dealerCustomerPage.rentalConditionsHint': 'Your own conditions for renting from you',
  'dealerCustomerPage.rentalConditionsPlaceholder':
    'Extra drivers, smoking, where the car may be taken — whatever you ask of a renter that Khadra does not.',
  'dealerCustomerPage.insurance': 'Insurance',
  'dealerCustomerPage.insuranceHint': 'What your cover includes, in your words',
  'dealerCustomerPage.insurancePlaceholder':
    'What the cover on your cars includes, and what a renter is liable for.',
  'dealerCustomerPage.pickupInstructions': 'Pickup instructions',
  'dealerCustomerPage.pickupInstructionsHint': 'How to find you, and what to bring',
  'dealerCustomerPage.pickupInstructionsPlaceholder':
    'Where to park, which floor the office is on, what to bring to the counter.',
  'dealerCustomerPage.deliveryNotes': 'Delivery notes',
  'dealerCustomerPage.deliveryNotesHint': 'Shown inside the delivery card on your page',
  'dealerCustomerPage.deliveryNotesPlaceholder':
    'The hours you deliver in, and anything you need from a customer to find them.',
  'dealerCustomerPage.customerNotes': 'Notes for customers',
  'dealerCustomerPage.customerNotesHint': 'Anything else worth saying',
  'dealerCustomerPage.customerNotesPlaceholder':
    'Anything else a customer should know that does not belong above.',
  'dealerCustomerPage.shown': 'Shown',
  'dealerCustomerPage.hidden': 'Hidden',
  'dealerCustomerPage.hiddenMeans':
    'A hidden section is left off your page entirely. Customers are not told that anything is missing.',
  'dealerCustomerPage.deliveryIsOff':
    'You do not offer delivery, so this is not shown on your page. Switch delivery on and it appears.',
  // The limit is the server's (`ProfileText.MaxLength`), and Arabic words a counted noun four
  // different ways across the range a limit could take — so these are plural messages keyed on the
  // limit rather than sentences with a number dropped into them. English needs only two forms; the
  // point of the shape is that Arabic gets to pick.
  'dealerCustomerPage.charactersUsed': {
    one: '{used} of {count} character',
    other: '{used} of {count} characters',
  },
  'dealerCustomerPage.tooLong': {
    one: 'Shorten this to {count} character or fewer. Nothing is cut for you — a condition trimmed mid-sentence says something you did not write.',
    other:
      'Shorten this to {count} characters or fewer. Nothing is cut for you — a condition trimmed mid-sentence says something you did not write.',
  },
  'dealerCustomerPage.savePage': 'Save customer page',
  'dealerCustomerPage.saving': 'Saving…',
  'dealerCustomerPage.savedTitle': 'Customer page saved',
  'dealerCustomerPage.savedBody': 'Customers see the new page from now on.',
  'dealerCustomerPage.couldntLoad': "Couldn't load your customer page",
  'dealerCustomerPage.couldntLoadNothingChanged':
    'Your customer page could not be loaded. Nothing has been changed.',
  'dealerCustomerPage.serviceDidNotRespond': 'The service did not respond. Nothing has been saved.',
  'dealerCustomerPage.onlyTheDealerOwner':
    'Only the dealer owner can edit this page. You can read it.',
  'dealerCustomerPage.staleConsoleTitle': 'Saving is turned off on this page',
  'dealerCustomerPage.staleConsoleBody':
    'Your page has a section this version of the console cannot show, and saving now would erase what is written in it. Reload the console to get the current version.',
  'dealerCustomerPage.asCustomersSeeIt': 'As customers see it',
  'dealerCustomerPage.previewIsSaved':
    'This is what is on your page now. It follows when you save.',
  // A preview row standing in from the other language: the office has not written this section in
  // the language being previewed, so the customer is shown what there is rather than a heading with
  // nothing under it. Worded as a fact about the TEXT, not as a warning about the office.
  'dealerCustomerPage.shownIn': 'shown in {language}',
  'dealerCustomerPage.nothingShownYet':
    'You have not written anything customers can see yet. Your opening hours, delivery, location, rating and cars are on your page regardless.',
  'dealerCustomerPage.yourWordsNotKhadras':
    'Customers are told these are your words, not Khadra’s.',
  'dealerCustomerPage.whatYouDoNotWrite': 'What you do not write here',
  'dealerCustomerPage.khadrasRules':
    'Cancellation, payment and the documents a renter needs are Khadra’s, and every booking quote states them.',
  'dealerCustomerPage.perCarTerms':
    'Fuel, mileage and the security deposit belong to each car, and you set them in Fleet.',
  'dealerCustomerPage.alwaysShown':
    'Your opening hours, delivery terms, address, rating and cars are always on your page. They cannot be hidden.',
  'dealerCustomerPage.whereElse': 'The rest of your page',
  'dealerCustomerPage.brandingAndHours':
    'Your name, logo, cover, location and opening hours are on the dealer page.',
  'dealerCustomerPage.openDealerPage': 'Open dealer page',
  'dealerCustomerPage.openDelivery': 'Open delivery settings',

  // Dealer profile.
  'dealerProfile.applyToAll': 'Apply to all',
  'dealerProfile.aboutMovedTitle': 'What you say about your office moved',
  'dealerProfile.aboutMovedBody':
    'It is on the customer page now, with the rest of what you tell customers and a switch for showing or hiding each part.',
  'dealerProfile.openCustomerPage': 'Open customer page',
  'dealerProfile.asACustomerSees': 'As a customer sees it',
  'dealerProfile.branding': 'Branding',
  'dealerProfile.businessName': 'Business name',
  'dealerProfile.commercialRegistration': 'Commercial registration',
  'dealerProfile.couldntLoadYourDealer': "Couldn't load your dealer page",
  'dealerProfile.cover': 'Cover',
  'dealerProfile.coverImage': 'Cover image',
  'dealerProfile.customerReviewsAreNot':
    'Customer reviews are not live yet; the page says so rather than showing a rating.',
  'dealerProfile.dealerProfile': 'Dealer profile',
  'dealerProfile.discard': 'Discard',
  'dealerProfile.dragThePinOr':
    'Drag the pin, or click the map, to set where customers collect cars. Your delivery radius is drawn around it to scale — zoom out to see the whole circle.',
  'dealerProfile.cityListNotLoaded': 'The city list has not loaded, so the city stays as it is.',
  'dealerProfile.currentCityNotOffered': 'Your current city (no longer offered)',
  'dealerProfile.cityNotOfferedHint':
    'The platform no longer offers this city. Your office stays filed under it — keep it, or move to a city from the list.',
  'dealerProfile.cityDecidesSearch': 'Customers find your cars when they search for this city.',
  'dealerProfile.addTheAreaForThisStreet': 'Add the area this street is in, or clear the street.',
  'dealerProfile.enterALatitudeBetween':
    'Enter a latitude between −90 and 90 and a longitude between −180 and 180.',
  'dealerProfile.fromYourLicenceNot': 'From your licence. Not editable.',
  'dealerProfile.hours': 'Hours',
  'dealerProfile.jpegPngOrWebp': 'JPEG, PNG or WebP · shown on your public page',
  'dealerProfile.location': 'Location',
  'dealerProfile.lockedAfterApprovalThis':
    'Locked after approval — this is the name your licence was verified against. Ask the platform to change it.',
  'dealerProfile.logo': 'Logo',
  'dealerProfile.onlyTheDealerOwner': 'Only the dealer owner can edit this page. You can read it.',
  'dealerProfile.operatingHours': 'Operating hours',
  'dealerProfile.hoursDecideSearches':
    'Customers searching for a time outside these hours see your cars only if they can be delivered then, since nobody is at the counter to hand them over.',
  'dealerProfile.publicPreview': 'Public preview',
  'dealerProfile.staff': 'Staff',
  'dealerProfile.theMapIsWaiting':
    'The map is waiting for coordinates it can place. Correct the numbers below and it will follow.',
  'dealerProfile.to': 'to',
  'dealerProfile.verification': 'Verification',
  'dealerProfile.whatCustomersSeeAbout':
    'What customers see about your dealership, and where and when to find you.',
  'dealerProfile.whatYouRentWhat':
    'What you rent, what you are known for, anything a customer should know before booking.',
  'dealerProfile.whereCustomersCollectCars':
    'Where customers collect cars, and the centre of your delivery radius',

  // The gallery application form.
  'dealerApply.allThreeAreRequired':
    'All three are required. JPEG, PNG or PDF. They are stored privately and only the administrator reviewing your application can open them.',
  'dealerApply.chooseACity': 'Choose a city…',
  'dealerApply.chooseACityOr':
    'Choose a city, or type a latitude and longitude, and the map will appear so you can place the pin exactly.',
  'dealerApply.clearThePin': 'Clear the pin',
  'dealerApply.clickTheMapTo':
    'Click the map where your gallery is, or use your current location. You can drag the pin afterwards to put it on the door.',
  'dealerApply.couldNotFindYou':
    'Your device could not work out where you are. Click the map instead.',
  'dealerApply.findingYou': 'Finding you…',
  'dealerApply.locationPermissionRefused':
    'This browser was not given permission to share your location. Click the map instead, or allow location for this site and try again.',
  'dealerApply.thisBrowserCannot':
    'This browser cannot share your location. Click the map to place the pin.',
  'dealerApply.useMyLocation': 'Use my current location',
  'dealerApply.whereYourGalleryIs': 'Where your gallery is',
  'dealerApply.area': 'Area',
  'dealerApply.street': 'Street',
  'dealerApply.theNeighbourhoodACustomer':
    'The neighbourhood a customer would name to a taxi driver.',
  'dealerApply.manyStreetsHaveNo':
    'Many streets have no recorded name. Leave it blank if yours does not.',
  'dealerApply.lookingUpThatSpot': 'Looking up that spot…',
  'dealerApply.city': 'City',
  'dealerApply.commercialRegistrationNumber': 'Commercial registration number',
  'dealerApply.galleryName': 'Gallery name',
  'dealerApply.oneWindowAppliedTo':
    'One window, applied to every day. You can set different hours per day on your gallery page once you are approved.',
  'dealerApply.optional': '(optional)',
  'dealerApply.pickingACityMoves':
    'Picking a city moves the map there. Then drag the pin to the door.',
  'dealerApply.submittingStartsThePlatforms':
    "Submitting starts the platform's licence check. You can still edit your gallery page while it is under review.",
  'dealerApply.submitYourGallery': 'Submit your gallery',
  'dealerApply.theApplicationWasNot': 'The application was not submitted',
  'dealerApply.theBusiness': 'The business',
  'dealerApply.theNameOnThe':
    'The name on the licence. It is locked once the gallery is approved, because it is the name the licence was checked against.',
  'dealerApply.thePlatformChecksThat':
    'The platform checks that every gallery is a licensed rental office before it can list cars. Nothing here is published until an administrator approves it.',
  'dealerApply.thePlatformHasNot':
    'The platform has not pinned this city yet, so the map cannot start there. Place the pin yourself, or type the coordinates.',
  'dealerApply.whatCustomersReadOn': 'What customers read on your gallery page.',
  'dealerApply.whenItOpens': 'When it opens',
  'dealerApply.yourPapers': 'Your papers',

  // Dealer reports.
  'dealerReports.atEachBookingsFrozen': "At each booking's frozen rate",
  'dealerReports.backToDashboard': 'Back to dashboard',
  'dealerReports.bookingsReturned': 'Bookings returned',
  'dealerReports.carsCurrentlyRented': 'Cars currently rented',
  'dealerReports.countedAsRevenueOnce': 'Counted as revenue once the car is back.',
  'dealerReports.daysRentedInThe': 'Days rented in the period',
  'dealerReports.moneyInThisPeriod': 'Money in this period',
  'dealerReports.netPayout': 'Net payout',
  'dealerReports.noListedVehiclesIn': 'No listed vehicles in this period.',
  'dealerReports.occupancy': 'Occupancy',
  'dealerReports.occupancyByVehicle': 'Occupancy by vehicle',
  // Names no payment method: how an office is paid waits on the acquiring arrangement (owner, 2026-09-24; item 217).
  'dealerReports.howPayoutsWork':
    'What Khadra owes you becomes due booking by booking, once each outcome is final, and is paid together with everything else then due to you: see Payouts. Commission is deducted from the card payment; any balance is collected in cash at handover.',
  'dealerReports.platformCommission': 'Platform commission',
  'dealerReports.rentalRevenue': 'Rental revenue',
  'dealerReports.rentalTotalsOfThose': 'Rental totals of those bookings',
  'dealerReports.rentalValueInProgress': 'Rental value in progress',
  'dealerReports.rentedDaysOverListed': 'Rented days over listed cars',
  'dealerReports.reports': 'Reports',
  'dealerReports.reportsAreNotAvailable': 'Reports are not available to you',
  'dealerReports.reportsAreNotPart': 'Reports are not part of your access',
  'dealerReports.returnedOrCompletedIn': 'Returned or completed in the period',
  'dealerReports.revenueAfterCommission': 'Revenue after commission',
  'dealerReports.revenueCommissionAndOccupancy':
    "Revenue, commission and occupancy are the dealer owner's to see, and theirs to share. Your owner can turn this on for you from their Employees screen — until they do, the figures stay hidden rather than showing you a total that is missing what you cannot see.",
  'dealerReports.stillOut': 'Still out',
  'dealerReports.yourDealershipsNumbersAt':
    "Your dealership's numbers, at the rates frozen on each booking.",

  // Dealer account settings.
  'dealerSettings.asThePlatformKnows': 'As the platform knows you',
  'dealerSettings.dealership': 'Dealership',
  'dealerSettings.email': 'Email',
  'dealerSettings.name': 'Name',
  'dealerSettings.nameAndEmailAre':
    'Name and email are changed by the platform on request, so they always match your licence and your sign-in.',
  'dealerSettings.notLive': 'Not live',
  'dealerSettings.notLiveYet': 'Not live yet',
  'dealerSettings.yourAccountDealershipDetails':
    'Your account. Dealership details live on the Dealer Profile page.',

  // Dispute, seen by the dealer.
  'dealerDispute.addToTheDispute': 'Add to the dispute',
  'dealerDispute.bothSidesAndThe': 'Both sides and the platform read everything here',
  'dealerDispute.chargedToYou': 'Charged to you',
  /** The office's own note under a decision: its share and its charge, never the customer's refund. */
  'dealerDispute.decisionRecordedForYou':
    'Decision recorded. What goes to you, and any charge to you, is settled by hand, because no payout rail exists yet.',
  'dealerDispute.thePlatformsDecision': "The platform's decision",
  'dealerDispute.transferredToYou': 'Transferred to you',
  'dealerDispute.whatHappenedFromYour': 'What happened from your side, with times.',
  'dealerDispute.withdraw': 'Withdraw',
  'dealerDispute.yourStatement': 'Your statement',

  // Fleet list.
  'fleetList.addACar': 'Add a car',
  'fleetList.addYourFirstCar':
    'Add your first car. It stays a draft until you publish it, and it needs at least one photo before it can be published.',
  'fleetList.couldntLoadYourFleet': "Couldn't load your fleet",
  'fleetList.editThisCar': 'Edit this car',
  'fleetList.fleet': 'Fleet',
  'fleetList.makeModelPlate': 'Make, model, plate…',
  'fleetList.noCarInThis': 'No car in this state matches your search.',
  'fleetList.noCarsYet': 'No cars yet',
  'fleetList.noPhoto': 'No photo',
  'fleetList.nothingMatches': 'Nothing matches',
  'fleetList.perDay': 'Per day',
  'fleetList.removeFromFleet': 'Remove from fleet',
  'fleetList.searchYourFleet': 'Search your fleet',
  'fleetList.takeOffTheRoad': 'Take off the road',
  'fleetList.theFleetIsThe':
    "The fleet is the dealer owner's to change. You can see every car and what it is doing, and the bookings against it are yours to handle.",
  'fleetList.yourCarsWhatEach':
    'Your cars, what each one is doing, and whether customers can see it.',
  'fleetList.yourDealershipHasNot':
    'Your dealership has not listed a car yet. Once the owner adds one it appears here, and its bookings come to you.',

  // Vehicle details.
  'vehicleDetail.bookingChangesOnThis': 'Booking changes on this car',
  'vehicleDetail.bookingHistory': 'Booking history',
  'vehicleDetail.couldntLoadThisCar': "Couldn't load this car",
  'vehicleDetail.customer': 'Customer',
  'vehicleDetail.derivedFromBookingsApproved':
    'Derived from bookings: approved bookings hold their dates and a car out on hire is marked as such. To block dates, take the car off the road; there is no separate blocked-dates list.',
  'vehicleDetail.editsToTheListing':
    "Edits to the listing itself (price, photos, delivery) are not logged yet. Booking changes shown here are the most recent from your dealership's activity.",
  'vehicleDetail.heldForBooking': 'Held for booking',
  'vehicleDetail.informationAmpSpecifications': 'Information & specifications',
  'vehicleDetail.nextMonth': 'Next month',
  'vehicleDetail.noApprovedBookingIs': 'No approved booking is holding this car.',
  'vehicleDetail.noBookingsOnThis': 'No bookings on this car yet',
  'vehicleDetail.outOnBooking': 'Out on booking',
  'vehicleDetail.previousMonth': 'Previous month',
  'vehicleDetail.pricingAmpDelivery': 'Pricing & delivery',
  'vehicleDetail.vehicleActivity': 'Vehicle activity',
  'vehicleDetail.vehicleAdded': 'Vehicle added',

  // Vehicle edit form.
  'carForm.addAPhoto': 'Add a photo',
  'carForm.availableForDelivery': 'Available for delivery',
  'carForm.brand': 'Brand',
  'carForm.brandModelPriceAnd':
    'Brand, model, price and the policies that travel with every booking of this car.',
  'carForm.carPhoto': 'Car photo',
  'carForm.chargedAgainstTheWhole': "Charged against the whole rental's allowance, not per day.",
  'carForm.cover': 'Cover',
  'carForm.dailyAllowanceKm': 'Daily allowance (km)',
  'carForm.dailyPriceJod': 'Daily price (JOD)',
  'carForm.excessFeePerKm': 'Excess fee per km (JOD)',
  'carForm.greenPlateDigitsOne': 'Green-plate digits. One car, one listing across the platform.',
  'carForm.onlyWithinYourDealerships':
    "Only within your dealership's delivery radius. You set the delivery fee yourself, on your Delivery page.",
  'carForm.photos': 'Photos',
  'carForm.priceAndPolicies': 'Price and policies',
  'carForm.saveTheCarFirst':
    'Save the car first — a photo needs a car to belong to. You will come straight back here.',
  'carForm.theCar': 'The car',
  'carForm.theDamageDepositFor':
    'The damage deposit for this car, separate from the booking deposit.',
  'carForm.theFirstPhotoBecomes':
    'The first photo becomes the cover. A car needs at least one before it can be published.',
  'carForm.unlimitedMileage': 'Unlimited mileage',
  'carForm.whatACustomerShould': 'What a customer should know about this car.',

  // Add-a-vehicle wizard.
  'vehicleWizard.aPublishedCarIs':
    'A published car is bookable whenever no approved booking holds it. To stop offering it for a while, take it off the road from its page; existing bookings are unaffected.',
  'vehicleWizard.availableForBooking': 'Available for booking',
  'vehicleWizard.back': 'Back',
  'vehicleWizard.blockSpecificDates': 'Block specific dates',
  'vehicleWizard.cancel': 'Cancel',
  'vehicleWizard.choose': 'Choose…',
  'vehicleWizard.chooseAYear': 'Choose a year…',
  'vehicleWizard.continueThatDraft': 'Continue that draft',
  'vehicleWizard.cover': 'Cover',
  'vehicleWizard.dailyLimit': 'Daily limit',
  'vehicleWizard.dailyLimitKm': 'Daily limit (km)',
  'vehicleWizard.dailyRentalPriceJod': 'Daily rental price (JOD)',
  'vehicleWizard.dealerProfile': 'dealer profile',
  'vehicleWizard.deliveryFee': 'Delivery fee',
  'vehicleWizard.eGElantra': 'e.g. Elantra',
  'vehicleWizard.eGWhite': 'e.g. White',
  'vehicleWizard.eligibleForDelivery': 'Eligible for delivery',
  'vehicleWizard.feePerKmOver': 'Fee per km over (JOD)',
  'vehicleWizard.fromItsPage': 'From its page',
  'vehicleWizard.fullToFull': 'Full to full',
  'vehicleWizard.heldAgainstTheBooking':
    'Held against the booking and returned after a clean handover.',
  'vehicleWizard.loadingYourDealership': 'Loading your dealership…',
  'vehicleWizard.make': 'Make',
  'vehicleWizard.mileage': 'Mileage',
  'vehicleWizard.notOffered': 'Not offered',
  'vehicleWizard.photosAreUploadedStraight':
    'Photos are uploaded straight to protected storage and served from a public, cacheable path. JPEG, PNG or WebP. At least one is needed before the car can be published.',
  'vehicleWizard.pickupLocation': 'Pickup location',
  'vehicleWizard.publishedCarsAppearIn':
    'Published cars appear in customer search straight away. Leave this off to keep the car as a draft in your fleet.',
  'vehicleWizard.publishWhenISave': 'Publish when I save',
  'vehicleWizard.sameToSame': 'Same to same',
  'vehicleWizard.shownToTheCustomer': 'Shown to the customer only after you approve their booking.',
  'vehicleWizard.steps': 'Steps',
  'vehicleWizard.thePlatformsCategoriesUsed': "The platform's categories, used by customer search.",
  'vehicleWizard.thereIsNoSeparate':
    'There is no separate blocked-dates list. Approved bookings hold their dates; taking the car off the road blocks every day until it is back.',
  'vehicleWizard.unlimited': 'Unlimited',
  'vehicleWizard.whatTheCustomerShould':
    'What the customer should know: extras, condition, long-rental terms…',
  'vehicleWizard.yourOwnAndThe':
    'Your own, and the same for every car you deliver. Change it on your Delivery page.',

  // Employee dashboard.
  'employeeDashboard.allBookings': 'All bookings',
  'employeeDashboard.heresWhatNeedsYour': "Here's what needs your attention today.",
  'employeeDashboard.noRequestsWaitingNo':
    'No requests waiting, no handovers due and nothing overdue. This updates as bookings come in.',
  'employeeDashboard.nothingNeedsYouRight': 'Nothing needs you right now',
  'employeeDashboard.recordedAgainstYou': 'Recorded against you',
  'employeeDashboard.requiresAttention': 'Requires attention',
  'employeeDashboard.reviewRequests': 'Review requests',
  'employeeDashboard.youHaveNotRecorded':
    'You have not recorded anything yet. Requests you answer and handovers you record appear here under your name.',
  'employeeDashboard.yourRecentActivity': 'Your recent activity',

  // My business (employee).
  'employeeBusiness.about': 'About',
  'employeeBusiness.couldntLoadYourDealership': "Couldn't load your dealership",
  'employeeBusiness.customerRating': 'Customer rating',
  'employeeBusiness.myBusiness': 'My business',
  'employeeBusiness.noOpeningHoursHave': 'No opening hours have been set for this dealership.',
  'employeeBusiness.openingHours': 'Opening hours',
  'employeeBusiness.ownerMaintained': 'Owner-maintained',
  'employeeBusiness.ownerOnlySettings': 'Owner-only settings',
  'employeeBusiness.ratingsAndReviewsAre':
    'Ratings and reviews are not built yet. When the Reviews context lands, what customers said about this dealership will appear here.',
  'employeeBusiness.readOnlyTheseDetails':
    'Read-only. These details are maintained by the dealer owner.',
  'employeeBusiness.thePlatformHoldsThis':
    "The platform holds this dealership's location as a map point. A street address, a dealership phone number and a public email address are not recorded, so they are not shown here.",
  'employeeBusiness.theseCannotBeChanged':
    'These cannot be changed from an employee account, and the API refuses them regardless of what this screen shows.',
  'employeeBusiness.whenCustomersExpectYou': 'When customers expect you',

  // Employee notifications.
  'employeeNotifications.aCustomersRequestArriving':
    'A request expiring unanswered and a pickup falling due are not here: your dashboard and your bookings show those live, every time you open them.',
  'employeeNotifications.couldntLoadYourNotifications': "Couldn't load your notifications",
  'employeeNotifications.goToTheDashboard': 'Go to the dashboard',
  'employeeNotifications.markRead': 'Mark read',
  'employeeNotifications.nothingYet': 'Nothing yet',
  'employeeNotifications.notifications': 'Notifications',
  'employeeNotifications.open': 'Open',
  'employeeNotifications.unread': 'Unread',
  'employeeNotifications.whenAColleagueAnswers':
    'When a customer requests, pays for, cancels or disputes a booking, when a colleague answers a request or hands a car over, when Khadra decides something about your bookings, your dealership or your payouts, or when your access changes, it appears here.',

  // Employee account settings.
  'employeeSettings.dealerEmployee': 'Dealer employee',
  'employeeSettings.fullName': 'Full name',
  'employeeSettings.grantedByTheDealer': 'Granted by the dealer owner',
  'employeeSettings.notificationPreferences': 'Notification preferences',
  'employeeSettings.notLiveYetThere':
    'Not live yet. There is no notifications service behind this console, so there is nothing to switch on or off. Invitations and password links are sent by email; everything else reaches you on the dashboard.',
  'employeeSettings.onlyTheDealerOwner':
    'Only the dealer owner can change these, and every endpoint enforces the same answer server-side.',
  'employeeSettings.profile': 'Profile',
  'employeeSettings.settingsSection': 'Settings section',
  'employeeSettings.workEmail': 'Work email',
  'employeeSettings.yourNameAndWork':
    'Your name and work email are held by the platform and are not editable here yet — changing the address you sign in with needs to prove the new mailbox first. Ask your dealer owner or the platform to change either.',
  'employeeSettings.yourOwnAccountPermissions':
    'Your own account. Permissions are set by the dealer owner.',
  'employeeSettings.yourPermissions': 'Your permissions',

  // A verification document tile.
  'docTile.openSecurePreview': 'Open secure preview',

  // What a private document will be served as. From the server's own content type, never guessed.
  'docFormat.pdf': 'PDF',
  'docFormat.jpeg': 'JPEG image',
  'docFormat.png': 'PNG image',
  'docFormat.webp': 'WebP image',

  // The renter's identity papers, on the dealer's booking screen (spec 5.1).
  'renterDocs.title': 'Renter’s documents',
  'renterDocs.hint': 'Check these against the person in front of you before handing over the car.',
  'renterDocs.type.drivingLicenceFront': 'Driving licence — front',
  'renterDocs.type.drivingLicenceBack': 'Driving licence — back',
  'renterDocs.type.nationalId': 'National ID',
  'renterDocs.type.passport': 'Passport',
  'renterDocs.viewLicence': 'View driving licence',
  'renterDocs.hideLicence': 'Hide documents',
  'renterDocs.view': 'View',
  'renterDocs.hide': 'Hide',
  'renterDocs.openInANewTab': 'Open in a new tab',
  'renterDocs.notOnFile': 'Not on file: {documents}',
  'renterDocs.nothingOnFile':
    'This renter has not uploaded any documents. You cannot complete the licence check from the console.',
  'renterDocs.closed': 'A renter’s documents are shown only while this booking is live.',
  'renterDocs.failed': 'The documents could not be loaded. Nothing has changed.',
  'renterDocs.imageFailed': 'That document could not be opened. Try again.',
  'renterDocs.notVerifiedByThePlatform':
    'Khadra does not verify these. What you see is what the renter uploaded.',

  // Recording that the DEALERSHIP checked a document. Never "verified by Khadra": the platform
  // authenticates nothing, and the notice below says so in the same breath as the control.
  'renterDocs.reviewedByDealer': 'Reviewed by dealer',
  'renterDocs.newUploadRequested': 'New upload requested by Khadra',
  'renterDocs.notReviewed': 'Not reviewed',
  'renterDocs.markAsReviewed': 'Mark as reviewed',
  'renterDocs.reviewedAt': 'Reviewed',
  'renterDocs.reviewSaved': 'Recorded as reviewed by your dealership.',
  'renterDocs.reviewFailed': 'That could not be recorded. Nothing has changed.',
  'renterDocs.reviewMeaning':
    'Marking a document as reviewed records that your dealership looked at it. It is not a check by Khadra: the platform does not confirm that a document is genuine, current, or registered with any authority.',

  // Joins a list of names into a sentence. Arabic uses its own comma.
  'common.listSeparator': ', ',

  // Toasts.
  'toast.dismiss': 'Dismiss',

  // The location map.
  'map.centreOnThePin': 'Centre on the pin',
  'map.dragThePinOr': 'Drag the pin, or click the map, to move your location.',
  'map.mapImageryUnavailableThe': 'Map imagery unavailable — the location below is still correct.',

  // The dealer's decisions on a booking: approve, reject, pickup, return.
  'dealerDecide.reason.vehicleUnavailable': 'Vehicle no longer available',
  'dealerDecide.reason.datesConflict': 'Dates conflict with another booking',
  'dealerDecide.reason.outsideRadius': 'Delivery location outside radius',
  'dealerDecide.reason.verificationIncomplete': 'Customer verification incomplete',
  'dealerDecide.reason.other': 'Other',
  'dealerDecide.approve.title': 'Approve booking {reference}?',
  'dealerDecide.approve.body':
    "{customer} is notified, and the car stays held for these dates while they pay. If the booking is not paid by the payment deadline, it expires and the car is free again. The customer's free-cancellation window opens when they pay.",
  'dealerDecide.approve.noteLabel': 'Note to customer (optional)',
  'dealerDecide.approve.notePlaceholder': 'Pickup instructions, delivery window…',
  'dealerDecide.approve.noteHint':
    'Recorded on the booking with you as the actor; the customer can read it.',
  'dealerDecide.approve.note': 'Recorded against your account on the booking history.',
  'dealerDecide.approve.confirm': 'Approve booking',
  'dealerDecide.approve.doneTitle': 'Booking approved',
  'dealerDecide.approve.doneBody': '{reference} · customer notified',
  'dealerDecide.approve.doneToast': '{reference} is held for its dates.',
  'dealerDecide.reject.title': 'Reject booking {reference}?',
  'dealerDecide.reject.body':
    'The customer is notified immediately and the dates are released. Rejection never costs the customer anything. This cannot be undone.',
  'dealerDecide.reject.reasonLabel': 'Reason',
  'dealerDecide.reject.detailsLabel': 'Details for the customer',
  'dealerDecide.reject.detailsPlaceholder':
    'Required — shown to the customer and kept on the booking.',
  'dealerDecide.reject.confirm': 'Reject booking',
  'dealerDecide.reject.doneTitle': 'Booking rejected',
  'dealerDecide.reject.doneBody': 'Dates released · reason sent to customer',
  'dealerDecide.reject.doneToast': '{reference} · dates released, reason sent to the customer',
  'dealerDecide.reject.needDetails': 'Tell the customer why. The reason is required.',
  'dealerDecide.odometerLabel': 'Odometer (km)',
  'dealerDecide.fuelLabel': 'Fuel level (0–1)',
  'dealerDecide.notesLabel': 'Notes',
  'dealerDecide.codeLabel': "Customer's handover code",
  'dealerDecide.codePlaceholder': 'The 6 digits on the customer\'s phone',
  'dealerDecide.unverifiedLabel': 'No code? Say why',
  'dealerDecide.unverifiedPlaceholder': 'e.g. Phone battery dead; licence and ID checked',
  'dealerDecide.codeNote': 'Ask the customer for the handover code — they have it in the Khadra app or on the Khadra website, as six digits and a QR. Without one, the handover is recorded as unverified with your reason, and the platform reviews it.',
  'handover.unverified': 'Handed over without the customer\'s code',
  'handover.unverifiedBadge': 'Unverified',
  'handover.unverifiedAdmin': 'Unverified handover: the dealer recorded it without the customer\'s code. Their reason:',
  'handover.verifiedByCode': 'Verified with the customer\'s handover code',
  'dealerDecide.mustBeANumber': '{field} must be a number.',
  'dealerDecide.codeAndReason':
    "Enter the customer's code, or say why there is none — not both. With a code, the reason would not be kept.",
  'dealerDecide.pickup.title': 'Hand over {vehicle}?',
  'dealerDecide.pickup.body':
    'Records that {reference} started and the keys changed hands. The odometer and fuel level protect both sides if the return is disputed.',
  'dealerDecide.pickup.odometerPlaceholder': 'e.g. 41200',
  'dealerDecide.pickup.fuelPlaceholder': 'e.g. 1 for a full tank, 0.5 for half',
  'dealerDecide.pickup.cashPlaceholder': 'The balance paid in cash at handover, if any',
  'dealerDecide.pickup.notesPlaceholder': 'Condition, accessories, anything worth writing down',
  'dealerDecide.pickup.note': 'Recorded with you as the person who handed over the car.',
  'dealerDecide.pickup.confirm': 'Record pickup',
  'dealerDecide.pickup.doneTitle': 'Pickup recorded',
  'dealerDecide.pickup.doneBody': '{reference} is now an active rental.',
  'dealerDecide.return.title': 'Take {vehicle} back?',
  'dealerDecide.return.body':
    'Records that {reference} ended and the car is back with you. The settlement window starts from this moment; either side can open a dispute inside it.',
  'dealerDecide.return.odometerPlaceholder': 'e.g. 41650',
  'dealerDecide.return.fuelPlaceholder': 'e.g. 0.75',
  'dealerDecide.return.cashPlaceholder': 'Any balance settled in cash at return',
  'dealerDecide.return.notesPlaceholder': 'Damage, cleanliness, missing items',
  'dealerDecide.return.note': 'Recorded with you as the person who took the car back.',
  'dealerDecide.return.confirm': 'Record return',
  'dealerDecide.return.doneTitle': 'Return recorded',
  'dealerDecide.return.doneBody': '{reference} is back; the settlement window has started.',

  // Dashboard KPI cards, the attention queue, activity verbs and relative time.
  'kpi.totalDealers': 'Total dealers',
  'kpi.trading': 'Trading',
  'kpi.pendingReview': 'Pending review',
  'kpi.suspended': 'Suspended',
  'kpi.bookings': 'Bookings',
  'kpi.today': 'Today',
  'kpi.active': 'Active',
  'kpi.pending': 'Pending',
  'kpi.customers': 'Customers',
  'kpi.verified': 'Verified',
  'kpi.pendingVerification': 'Pending verification',
  'kpi.disputes': 'Disputes',
  'kpi.pendingAdmin': 'Pending admin',
  'kpi.overdue': 'Overdue',
  'kpi.resolvedInDays': 'Resolved {days}d',
  'queue.applicationsOverdue': {
    one: '{count} dealer application is past the review SLA',
    other: '{count} dealer applications are past the review SLA',
  },
  'queue.applicationsApproaching': {
    one: '{count} dealer application is approaching the review SLA',
    other: '{count} dealer applications are approaching the review SLA',
  },
  'queue.newDispute': 'New dispute opened',
  'queue.disputeOpenFor': {
    one: 'Dispute open for {count} hour',
    other: 'Dispute open for {count} hours',
  },
  'queue.needsAttention': 'Needs attention',
  // The dashboard's activity strip: one whole sentence per action (owner, 2026-09-27). {subject} is
  // a booking reference for bookings and disputes, a short reference for a customer, and otherwise
  // the name that was recorded.
  'activity.dealerApproved': '{actor} approved dealer {subject}',
  'activity.dealerRejected': '{actor} rejected dealer {subject}',
  'activity.dealerClarification': '{actor} asked for clarification from {subject}',
  'activity.dealerSuspended': '{actor} suspended dealer {subject}',
  'activity.dealerReactivated': '{actor} reactivated dealer {subject}',
  'activity.customerSuspended': '{actor} suspended customer {subject}',
  'activity.customerReactivated': '{actor} reactivated customer {subject}',
  'activity.customerDocumentRejected': '{actor} rejected a document of customer {subject}',
  'activity.disputeOpened': '{actor} opened a dispute on booking {subject}',
  'activity.disputeAssigned': '{actor} took the dispute on booking {subject}',
  'activity.disputeResolved': '{actor} resolved the dispute on booking {subject}',
  'activity.settingChanged': '{actor} changed setting {subject}',
  'activity.reviewHidden': '{actor} hid review {subject}',
  'activity.reviewRestored': '{actor} restored review {subject}',
  'activity.adminInvited': '{actor} invited admin {subject}',
  'activity.adminInvitationResent': '{actor} resent the invitation to {subject}',
  'activity.handoverVerified': '{actor} verified the handover of {subject}',
  'activity.handoverUnverified': '{actor} recorded an unverified handover of {subject}',
  'activity.handoverCodeLocked': '{actor} locked the handover code of {subject}',
  'activity.financialDocumentVoided': '{actor} voided document {subject}',
  'activity.financialDocumentEmailRequested': '{actor} queued an email of receipt {subject}',
  // The office payables ledger (payments Phase 8): a settlement by its number, a payable by its booking's reference.
  'activity.officeSettlementRecorded': '{actor} recorded settlement {subject}',
  'activity.officeSettlementVoided': '{actor} voided settlement {subject}',
  'activity.officePayableHeld': "{actor} held the office's payable for booking {subject}",
  'activity.officePayableReleased': "{actor} released the office's payable for booking {subject}",
  'activity.adminDeactivated': '{actor} deactivated admin {subject}',
  'activity.adminReactivated': '{actor} reactivated admin {subject}',
  'activity.bookingCancelled': '{actor} cancelled booking {subject}',
  'activity.bookingExpired': '{actor} expired booking {subject}',
  'activity.bookingNoShow': '{actor} recorded a no-show on {subject}',
  'activity.cityAdded': '{actor} added city {subject}',
  'activity.cityRenamed': '{actor} renamed city {subject}',
  'activity.cityRetired': '{actor} retired city {subject}',
  'activity.cityRestored': '{actor} restored city {subject}',
  'activity.carTypeAdded': '{actor} added car type {subject}',
  'activity.carTypeRenamed': '{actor} renamed car type {subject}',
  'activity.carTypeRetired': '{actor} retired car type {subject}',
  'activity.carTypeRestored': '{actor} restored car type {subject}',
  // An action this build has no sentence for: its name spelled out, never an empty line.
  'activity.other': '{actor}: {action}, {subject}',

  // Dealer activity feed.
  'dealerActivity.everyChangeOnYour':
    'Every change on your bookings, newest first, with who made it.',
  'dealerActivity.couldntLoadActivity': "Couldn't load activity",
  'dealerActivity.nothingYet': 'Nothing yet',
  'dealerActivity.bookingRequestsApprovalsPickups':
    'Booking requests, approvals, pickups and returns will appear here as they happen.',

  // Second pass: literal runs that sit beside control flow.
  'dealerDashboard.aRequestExpiresWhen':
    'An unanswered request expires at its answer deadline, shown on the booking',
  'dealerDashboard.upcomingPickups': 'Upcoming pickups',
  'dealerDashboard.noPickupsDueIn': 'No pickups due in this window.',
  'dealerDashboard.upcomingReturns': 'Upcoming returns',
  'dealerDashboard.noReturnsDueIn': 'No returns due in this window.',
  'dealerDashboard.returnedToYourLocation': 'Returned to your location',
  'dealerDashboard.fleetStatus': 'Fleet status',
  'dealerDashboard.noVehiclesYet': 'No vehicles yet.',
  'dealerDashboard.addYourFirstCar': 'Add your first car.',
  'dealerDashboard.recentActivity': 'Recent activity',
  'dealerDashboard.viewAll': 'View all',
  'dealerDashboard.bookingDecisionsAndHandovers':
    'Changes on your bookings will appear here: requests, payments, your answers and handovers.',
  'disputesList.noDisputeIsOpen':
    'No dispute is open. A customer or a dealer can open one from a finished booking, within the window that booking froze.',
  'disputesList.noTicketMatchesThis': 'No ticket matches this view right now.',
  'vehicleWizard.deliveryIsSwitchedOff':
    'Delivery is switched off for your dealership, so this only takes effect once you switch it on from the Delivery page.',
  'vehicleWizard.customersInsideYourDelivery':
    'Customers inside your delivery radius can ask for this car to be delivered.',
  'vehicleWizard.notSet': 'Not set',
  'dealerDelivery.deliveryCanBeChanged':
    'Delivery can be changed once your dealership is approved and trading.',
  'dealerDelivery.onlyTheDealerOwner':
    'Only the dealer owner can change delivery settings. You can read them here.',
  'customersList.couldntLoadTheCustomers': "Couldn't load the customers",
  'customersList.joined': 'Joined',
  'dealerReview.rejectionClarificationAndSuspensio':
    'Rejection, clarification and suspension all require a written reason. Every decision is attributed to you in the audit log, which cannot be edited afterwards.',
  'dealerReview.whoDecidedThisAnd': 'Who decided this and when is in the',
  'dealerReview.whichCannotBeEdited': ', which cannot be edited afterwards.',
  'vehicleDetail.editVehicle': 'Edit vehicle',
  'vehicleDetail.changingThisCarIts':
    "Changing this car — its details, its photos, whether it is listed — is the dealer owner's. Everything about it is here for you to work from, and its bookings are yours to handle.",
  'security.signedIn': 'Signed in',
  'security.lastUsed': 'Last used',
  'security.endingASessionStops':
    'Ending a session stops it being refreshed. A request already holding a valid token can keep working for up to',
  'security.minutesAfterThat': 'minutes after that.',
  'auditLog.noEntryMatchesThis':
    'No entry matches this combination. Widen the range or clear the filters.',
  'auditLog.theLogFillsAs':
    'The log fills as administrators act: approving a dealer, resolving a dispute, changing a platform setting. Each entry is written in the same transaction as the action itself.',
  'dealerBookings.onceYourVehiclesAre':
    'Once your vehicles are published, customer booking requests land here. Answer each one before its answer deadline, shown on the booking, or it expires.',
  'dealerBookings.noBookingsMatchThis': 'No bookings match this tab right now.',
  'employeeNotifications.whatHappenedAtYour': 'What happened at your dealership',
  'fleetList.cars': 'cars',
  'lookups.dealersPinTheirLocation':
    'Dealers pin their location on a map, and distance is measured from the coordinates, so the platform works without this list. Adding cities gives customers something to filter by.',
  'lookups.everyCarCurrentlyCarries':
    'Every car currently carries a placeholder type. Adding real types here gives dealers something to choose from and customers something to filter by.',
  'adminUsers.added': 'Added',
  'dealerReports.ammanTime': '· Amman time',
  'common.pageOf': 'Page {page} of {total}',

  // Admin dashboard panels.
  'adminDashboard.retry': 'Retry',
  'adminDashboard.platformFiguresCouldNot': 'Platform figures could not be loaded',
  'adminDashboard.nothingHasBeenChanged': 'Nothing has been changed.',
  'adminDashboard.requiresAttention': 'Requires attention',
  'adminDashboard.theWorkQueueCould': 'The work queue could not be loaded',
  'adminDashboard.nothingHasBeenChanged2':
    'Nothing has been changed. The rest of the page is unaffected.',
  'adminDashboard.sla': 'SLA',
  'adminDashboard.nothingNeedsAttention': 'Nothing needs attention',
  'adminDashboard.noDisputeOrDealer':
    'No dispute or dealer application is approaching its review deadline.',
  'adminDashboard.theBookingTrendCould': 'The booking trend could not be loaded.',
  'adminDashboard.noBookingsWereMade': 'No bookings were made in this period.',
  'adminDashboard.busiestDayWith': {
    one: 'busiest day {count} booking',
    other: 'busiest day {count} bookings',
  },
  'adminDashboard.seeTheBookings': 'see the bookings',
  'adminDashboard.moneyInMotion': 'Money in motion',
  'adminDashboard.recentActivity': 'Recent activity',
  'adminDashboard.theActivityFeedCould': 'The activity feed could not be loaded.',
  'adminDashboard.noRecordedActivityYet': 'No recorded activity yet.',

  // Copy that lives in a component: dialogs, toasts and field labels.
  'dealerReview.noDecisionOutstanding': 'No decision outstanding.',
  'dealerReview.theDealerWillBe':
    'The dealer will be able to publish cars and receive bookings immediately.',
  'dealerReview.thisDecisionIsRecorded':
    'This decision is recorded against your account in the audit log.',
  'dealerReview.dealerApproved': 'Dealer approved',
  'dealerReview.theApplicationIsClosed':
    'The application is closed. The dealer can correct it and resubmit.',
  'dealerReview.whatIsWrongWith': 'What is wrong with the application?',
  'dealerReview.theDealerSeesThis': 'The dealer sees this. Be specific enough to act on.',
  'dealerReview.applicationRejected': 'Application rejected',
  'dealerReview.theApplicationGoesBack':
    'The application goes back to the dealer with your note. They fix it and resubmit.',
  'dealerReview.eGTheVehicle': 'e.g. the vehicle registration photo is unreadable',
  'dealerReview.nameTheOneThing': 'Name the one thing to fix.',
  'dealerReview.sendBack': 'Send back',
  'dealerReview.sentBackToThe': 'Sent back to the dealer',
  'dealerReview.theReviewClockRestarts': 'The review clock restarts when they resubmit.',
  'dealerReview.theyStopTradingImmediately':
    'They stop trading immediately. The licence check is not undone, so reactivating does not send them back through review.',
  'dealerReview.whyIsThisDealer': 'Why is this dealer being suspended?',
  'dealerReview.dealerSuspended': 'Dealer suspended',
  'dealerReview.theyCanTradeAgain':
    'They can trade again straight away; their approval was never withdrawn.',
  'dealerReview.dealerReactivated': 'Dealer reactivated',
  'adminBooking.whyIsThePlatform': 'Why is the platform cancelling this booking?',
  'adminBooking.bookingCancelled': 'Booking cancelled',
  'adminBooking.recordedAgainstYourAccount': 'Recorded against your account in the audit log.',
  'adminBooking.refusedIfTheBookings':
    'Refused if the booking’s own window has not run out yet — the deadline is the one it froze, not today’s setting.',
  'adminBooking.bookingExpired': 'Booking expired',
  'adminBooking.refusedUntilTheNo':
    'Refused until the no-show window this booking froze has elapsed.',
  'adminBooking.markNoShow': 'Mark no-show',
  'adminBooking.recordedAsANo': 'Recorded as a no-show',
  'adminBooking.aPenaltyIsAssessed': 'A penalty is assessed, not charged.',
  'disputeDetail.refundTheCustomer': 'Refund the customer',
  'disputeDetail.applyThePenaltyIn': 'Apply the penalty in full',
  'disputeDetail.waiveEverything': 'Waive everything',
  'disputeDetail.moneyOnThisBooking': 'Money on this booking',
  'disputeDetail.recordThisDecision': 'Record this decision?',
  'disputeDetail.decisionRecordedNoFunds':
    "The customer's share is refunded to their original payment method automatically. What the platform keeps and what goes to the office are settled by hand; no payout rail exists yet.",
  'disputeDetail.resolveDispute': 'Resolve dispute',
  'disputeDetail.disputeResolved': 'Dispute resolved',
  'disputeDetail.decisionRecordedNoFunds2': "Decision recorded. Any refund to the customer is on its way; the rest is settled by hand.",
  'dealerDashboard.pendingRequests': 'Pending requests',
  'dealerDashboard.activeRentals': 'Active rentals',
  'dealerDashboard.availableVehicles': 'Available vehicles',
  'dealerDashboard.confirmedNotYetCollected': 'Confirmed, not yet collected',
  // Approved bookings await a PAYMENT: the customer chooses the deposit or the whole amount.
  'dealerDashboard.awaitingPayment': 'Awaiting payment',
  'dealerDashboard.approvedAndUnpaid': 'approved, not paid for yet',
  'dealerDashboard.heldForTheirDates': 'held for their dates',
  'dealerDashboard.revenueThisMonth': 'Revenue · this month',
  'dealerDashboard.occupancyRate': 'Occupancy rate',
  'dealerBooking.theAnswerWindowHasClosed':
    'The answer window has closed. This request can only be rejected now.',
  'dealerEmployees.staffIsTheOwners': 'Staff is the owner’s to manage',
  'dealerEmployees.whoWorksHereWhat':
    'Who works here, what they may see, and who is invited or deactivated are the dealer owner’s decisions. Your own access is on the Settings screen.',
  'dealerEmployees.staffOpensOnceYour': 'Staff opens once your dealership is trading',
  'dealerEmployees.invitingPeopleGrantingReport':
    'Inviting people, granting report access and deactivating them all act on live bookings, so the platform holds them until your dealership is approved and not suspended. Your dealer page shows where the application stands.',
  'dealerEmployees.inviteAStaffMember': 'Invite a staff member',
  'dealerEmployees.theyGetAnEmail':
    'They get an email with a link to set their own password. The link works for a limited time; you can resend it from this page.',
  'dealerEmployees.sendInvitation': 'Send invitation',
  'dealerEmployees.eGAhmadZaid': 'e.g. Ahmad Zaid',
  'dealerEmployees.requiredHowYouReach': 'Required. How you reach them about a handover.',
  'dealerEmployees.whetherTheyCanSee':
    'Whether they can see revenue and the reports page. Bookings are always theirs to handle.',
  'dealerEmployees.invitationSent': 'Invitation sent',
  'dealerEmployees.theyWillFindThe': 'They will find the link in their inbox.',
  'dealerEmployees.theyAreSignedOut':
    'They are signed out everywhere immediately and can no longer open the console or act on bookings. Everything they did stays on record under their name. You can reactivate them later.',
  'dealerEmployees.staffMemberDeactivated': 'Staff member deactivated',
  'vehicleDetail.onHire': 'On hire',
  'vehicleDetail.offTheRoad': 'Off the road',
  'vehicleDetail.requestedOrUnpaid': 'Requested or unpaid',
  'vehicleDetail.backOnTheRoad': 'Back on the road',
  'vehicleDetail.everyDayShowsAs':
    'Every day shows as not offered until you bring it back. Bookings already approved are not affected — tell those customers yourself if the car will not be ready.',
  'fleetList.offTheRoad': 'Off the road',
  'fleetList.blocked': 'Blocked',
  'fleetList.hide': 'Hide',
  'fleetList.publish': 'Publish',
  'fleetList.all': 'All',
  'fleetList.listed': 'Listed',
  'fleetList.hidden': 'Hidden',
  'fleetList.draft': 'Draft',
  'fleetList.inYourFleet': 'In your fleet',
  'fleetList.availableNow': 'Available now',
  'fleetList.onHire': 'On hire',
  'fleetList.outWithACustomer': 'Out with a customer',
  'fleetList.notPublishedYet': 'Not published yet',
  'fleetList.notOfferedUntilBack': 'Not offered until it is back',
  'fleetList.notShownToCustomers': 'Not shown to customers',
  'fleetList.visibleToCustomers': 'Visible to customers',
  'fleetList.cannotTrade': 'Your dealership cannot trade',
  'fleetList.backOnTheRoad': 'Back on the road',
  'fleetList.itStopsBeingOffered':
    'It stops being offered to customers until you bring it back. Bookings already approved on it are not affected — tell those customers yourself if the car will not be ready.',
  'fleetList.itDisappearsFromYour':
    'It disappears from your fleet and from customer search. Bookings already made against it keep their history.',
  'fleetList.removeCar': 'Remove car',
  'fleetList.carRemoved': 'Car removed',
  'employeeDashboard.pendingRequests': 'Pending requests',
  'employeeDashboard.activeRentals': 'Active rentals',
  'employeeDashboard.confirmedNotYetCollected': 'Confirmed, not yet collected',
  'employeeDashboard.heldForTheirDates': 'held for their dates',
  'vehicleWizard.basicInformation': 'Basic information',
  'customerProfile.theyAreSignedOut':
    'They are signed out of every device immediately and cannot book again until the account is reactivated. Bookings already made are not cancelled by this.',
  'customerProfile.theReasonIsRecorded':
    'The reason is recorded permanently in the audit log. Do not include personal details.',
  'customerProfile.whyIsThisAccount': 'Why is this account being suspended?',
  'customerProfile.accountSuspended': 'Account suspended',
  'customerProfile.theirSessionsEndedImmediately': 'Their sessions ended immediately.',
  'customerProfile.theyCanSignIn':
    'They can sign in and book again straight away. Their verification state is unchanged.',
  'customerProfile.accountReactivated': 'Account reactivated',
  // Rejecting a renter's document (Wave 4, W4-9; owner D3): the file is opened from inside the decision about it.
  'customerProfile.rejectDocumentButton': 'Reject…',
  'customerProfile.rejectDocumentQuestion': 'Reject the {document}?',
  'customerProfile.rejectDocumentBody':
    'Open the file and read it before deciding. If you reject it, the customer is told by push and email, and must upload a new file before their next booking request. Bookings already made are not affected.',
  'customerProfile.openTheFile': 'Open the file in a new tab',
  'customerProfile.rejectDocumentNote':
    'Opening the file is recorded with your name. The reason is shown to the customer exactly as you write it, and kept permanently in the audit log: do not include personal details.',
  'customerProfile.rejectReasonPlaceholder': 'For example: the photo is too blurred to read the licence number.',
  'customerProfile.rejectDocument': 'Reject the file',
  'customerProfile.documentRejected': 'File rejected',
  'customerProfile.documentRejectedBody': 'The customer has been told, and asked for a new upload.',
  'security.changeYourPassword': 'Change your password',
  'security.everyOtherSessionIs':
    'Every other session is signed out when the password changes. The one you are using now stays.',
  'security.yourNewPassword': 'Your new password',
  'security.yourOtherSessionsWere': 'Your other sessions were signed out.',
  'security.endThisSession': 'End this session?',
  'security.sessionEnded': 'Session ended',
  'dealerApply.theRegistrationCertificateFor': 'The registration certificate for the business.',
  'dealerApply.greenPlateVehicleRegistration': 'Green-plate vehicle registration',
  'dealerApply.proofThatYourCars':
    'Proof that your cars are registered as licensed rental vehicles.',
  'dealerApply.theOwnersId': 'The owner’s ID',
  'dealerApply.yourNationalIdOr': 'Your national ID or passport.',
  'lookups.displayOrder': 'Display order',
  'lookups.centreLatitude': 'Centre latitude',
  'lookups.centreLongitude': 'Centre longitude',
  'lookups.bothNamesChangeTogether':
    'Both names change together. Everything already pointing at this entry follows the new name.',
  'dealerDispute.withdrawThisDispute': 'Withdraw this dispute?',
  'dealerDispute.theAmicablePathThe':
    'The amicable path: the ticket closes and the booking settles as if no dispute had been raised. You can open a new one while the window is still open.',
  'dealerDispute.withdrawDispute': 'Withdraw dispute',
  'dealerDispute.disputeWithdrawn': 'Dispute withdrawn',
  'dealerDispute.settlesAsIfNone': 'The booking settles as if no dispute had been raised.',
  'dealerSettings.twoFactorAuthentication': 'Two-factor authentication',
  'dealerSettings.notLiveYetSign': 'Not live yet. Sign-in is email and password.',
  'dealerSettings.notLiveYetInvitations':
    'Not live yet. Invitations and password links go by email; everything else is on the dashboard.',
  'dealerSettings.payoutDetails': 'Payout details',
  'dealerSettings.notLiveYetPayouts':
    'Not live yet. Khadra does not collect payout details here; what it owes you, and each payment of it, is on your Payouts page. Commission is deducted from the card payment and any balance is collected in cash.',
  'dealerSettings.pauseOrCloseThe': 'Pause or close the dealership',
  'dealerSettings.notLiveYetHide':
    'Not live yet. Hide individual cars from the fleet page to stop taking bookings; ask the platform to close the account.',
  'employeeSettings.fleetAccess': 'Fleet access',
  'employeeSettings.seeEveryCarIts':
    'See every car, its status and its calendar. Adding, editing and removing cars are the owner’s.',
  'employeeBusiness.verificationStatus': 'Verification status',
  'employeeBusiness.businessNameAndLocation': 'Business name and location',
  'employeeBusiness.staffAndPermissions': 'Staff and permissions',
  'employeeBusiness.financialSettings': 'Financial settings',
  'dealerReports.thisWeek': 'This week',
  'dealerReports.thisMonth': 'This month',
  'disputesList.liveQueue': 'Live queue',
  'adminDashboard.yourSessionHasExpired': 'Your session has expired',
  'adminDashboard.signInAgainTo': 'Sign in again to see platform figures.',
  'adminDashboard.thisAccountCannotSee': 'This account cannot see the platform dashboard',
  'adminDashboard.platformFiguresAreRestricted':
    'Platform figures are restricted to administrators.',

  // Admin users dialogs.
  'adminUsers.inviteAnAdministrator': 'Invite an administrator',
  'adminUsers.theyGetAOne':
    'They get a one-time link to choose their own password. Nothing about the account works until they accept it.',
  'adminUsers.thereIsOneAdministrator':
    'There is one administrator role, and it can do everything this console can: review dealerships, decide disputes and see every booking.',
  'adminUsers.fullName': 'Full name',
  'adminUsers.eGYousefBarakat': 'e.g. Yousef Barakat',
  'adminUsers.sendInvitation': 'Send invitation',
  'adminUsers.invitationSent': 'Invitation sent',
  'adminUsers.resendInvitation': 'Resend invitation',
  'adminUsers.resendNameQuestion': 'Send {name} their invitation again?',
  'adminUsers.resendBody': 'A new link goes to {email}, valid from now.',
  'adminUsers.resendNote':
    'Any earlier link for this account stops working immediately. If the message cannot be sent, nothing is reported as sent and you can try again.',
  'adminUsers.invitationNotEmailed': 'The account was created — the email was not sent',
  'adminUsers.accountCreatedEmailFailed':
    'The account for {email} was created and its invitation is valid, but the mail service would not accept the message. Nothing reached them. Resend the invitation from the row below once mail is working; inviting the address again will be refused.',
  'adminUsers.theyAreSignedOut':
    'They are signed out everywhere and cannot administer the platform until reactivated. Everything they have already done stays on the record.',
  'adminUsers.reversibleTheAccountIs':
    'Reversible. The account is not deleted — deleting it would burn the email address permanently.',
  'adminUsers.whyIsThisAccount': 'Why is this account being deactivated?',
  'adminUsers.administratorDeactivated': 'Administrator deactivated',
  'adminUsers.theirSessionsEndedImmediately': 'Their sessions ended immediately.',
  'adminUsers.theyCanSignIn': 'They can sign in and administer the platform again straight away.',
  'adminUsers.administratorReactivated': 'Administrator reactivated',

  // List columns, and the status words the server sends as enum names.
  'common.all': 'All',
  'common.sessionExpired': 'Your session has expired. Sign in again.',
  'dealersList.colDealer': 'Dealer',
  'dealersList.colCars': 'Cars',
  'dealersList.colRating': 'Rating',
  'dealersList.colReviewDue': 'Review due',
  'dealersList.colActions': 'Actions',
  'dealersList.adminOnly': 'Only administrators can see the dealer queue.',
  'dealersList.loadFailed': 'The dealer queue could not be loaded. Nothing has been changed.',
  'status.pendingReview': 'Pending review',
  'status.clarificationNeeded': 'Clarification needed',
  'status.approved': 'Approved',
  'status.rejected': 'Rejected',
  'status.suspended': 'Suspended',
  'status.active': 'Active',
  'status.draft': 'Draft',
  'status.listed': 'Listed',
  'status.hidden': 'Hidden',
  'status.offTheRoad': 'Off the road',
  'status.requested': 'Requested',
  'status.approvedBooking': 'Approved',
  'status.rejectedBooking': 'Rejected',
  'status.cancelled': 'Cancelled',
  'status.expired': 'Expired',
  'status.completed': 'Completed',
  'status.returned': 'Returned',
  'status.disputed': 'Disputed',
  'status.noShow': 'No show',
  'status.open': 'Open',
  'status.underReview': 'Under review',
  'status.resolved': 'Resolved',
  'status.withdrawn': 'Withdrawn',
  'status.invited': 'Invited',
  'status.deactivated': 'Deactivated',
  'status.verified': 'Verified',
  'status.unverified': 'Unverified',

  // Status names the SERVER sends as Enumeration.Name, resolved through I18nService.statusLabel. Two of them are scope-qualified: Approved and Rejected mean different things on a dealer and on a booking, and Arabic does not share a word for both.
  'status.pickedUp': 'Picked up',
  'status.confirmed': 'Confirmed',
  'status.pendingDealer': 'Pending',
  'status.awaitingPayment': 'Awaiting payment',
  'status.activeRental': 'Active',
  'status.uploaded': 'Uploaded',
  'status.missing': 'Missing',
  'status.pending': 'Pending',

  // The customer profile's account standing, which is not a server enum.
  'customerProfile.emailUnverified': 'Email unverified',

  // The dealer's booking detail: every label, sentence and status line it assembles in TypeScript. Keyed 2026-09-08; the screen read English under Arabic before this.
  'dealerBooking.thatBookingIsNot': 'That booking is not one of yours, or no longer exists.',
  'dealerBooking.theBookingCouldNot': 'The booking could not be loaded. Nothing has been changed.',
  'dealerBooking.pickupAtYourLocation': 'pickup at your location',
  'dealerBooking.noLongerListed': 'No longer listed',
  'dealerBooking.plate': 'Plate',
  'dealerBooking.dailyPriceOnThis': 'Daily price on this booking',
  'dealerBooking.start': 'Start',
  'dealerBooking.end': 'End',
  'dealerBooking.duration': 'Duration',
  'dealerBooking.method': 'Method',
  'dealerBooking.collectedFromYourLocation': 'Collected from your location',
  'dealerBooking.deliveryFeeYours': 'Delivery fee (yours)',
  'dealerBooking.securityDepositHeldPer': 'Security deposit (held per car)',
  'dealerBooking.balanceToCollectIn': 'Balance to collect in cash at handover',
  'dealerBooking.balanceCollectedInCash': 'Balance collected in cash at handover',
  'dealerBooking.heldPendingSettlementSee': 'Held pending settlement — see the penalty panel',
  'dealerBooking.paymentReceived': 'Payment received',
  // The refund row of a booking paid in full: it returned the whole payment, not a deposit.
  'dealerBooking.payment': 'Payment',
  'dealerBooking.return': 'Return',
  'dealerBooking.disputeOpened': 'Dispute opened',
  'dealerBooking.theVehicle': 'the vehicle',
  'dealerBooking.requestedAwaitingYourAnswer': 'Requested · awaiting your answer',
  'dealerBooking.approvedAwaitingPayment': 'Approved · awaiting payment',
  'dealerBooking.depositPaidBookingConfirmed': 'Deposit paid · booking confirmed',
  'dealerBooking.paidInFullBookingConfirmed': 'Paid in full · booking confirmed',
  'dealerBooking.byYourStaff': 'by your staff',
  'dealerBooking.byYourDealership': 'by your dealership',
  'dealerBooking.byTheCustomer': 'by the customer',
  'dealerBooking.byThePlatform': 'by the platform',
  'dealerBooking.thisBookingCannotBe':
    'This booking cannot be disputed: it has not finished, or its dispute window has closed.',
  'dealerBooking.aDisputeIsAlready': 'A dispute is already open on this booking.',
  'dealerBooking.evidenceMustBeA': 'Evidence must be a photo or a PDF.',

  // The employee dashboard: greeting, workload captions and activity verbs. Item 49 called this the most visible gap, because every stat tile caption on an employee's landing screen was English.
  'employeeDash.goodMorning': 'Good morning',
  'employeeDash.goodAfternoon': 'Good afternoon',
  'employeeDash.goodEvening': 'Good evening',
  'employeeDash.nothingWaiting': 'nothing waiting',
  'employeeDash.noneOverdue': 'none overdue',
  'employeeDash.bookingRequest': 'Booking request',
  'employeeDash.returnOverdue': 'Return overdue',
  'employeeDash.pickupApproaching': 'Pickup approaching',
  'employeeDash.returnDue': 'Return due',
  'employeeDash.thisAccountIsNot': 'This account is not part of a dealership.',
  'employeeDash.yourDashboardCouldNot':
    'Your dashboard could not be loaded. Nothing has been changed.',

  // The dealer dashboard's KPI sub-labels and activity verbs.
  'dealerDash.allWithinTheirDates': 'all within their dates',
  'dealerDash.rentalsReturnedThisMonth': 'rentals returned this month, before commission',
  'dealerDash.notPartOfYour': 'not part of your access',
  'dealerDash.fleetUtilisationLast30': 'fleet utilisation, last 30 days',
  'dealerDash.overdueReturn': 'Overdue return',
  'dealerDash.viewBooking': 'View booking',

  // Keyed by key-copy.js, 2026-09-08.
  'vehicleDetail.thatCarIsNot': 'That car is not in your fleet, or has been removed.',
  'vehicleDetail.theCarCouldNot': 'The car could not be loaded. Nothing has been changed.',
  'vehicleDetail.dailyPrice': 'Daily price',
  'vehicleDetail.securityDeposit': 'Security deposit',
  'vehicleDetail.eligibleButDeliveryIs':
    'Eligible, but delivery is switched off for your dealership',
  'vehicleDetail.pickupOnly': 'Pickup only',
  'vehicleDetail.insurance': 'Insurance',
  'vehicleDetail.pendingPlatformConfiguration': 'Pending platform configuration',
  'vehicleDetail.notListed': 'Not listed',
  'vehicleDetail.customersCanSeeIt': 'Customers can see it now.',
  'vehicleDetail.customersNoLongerSee': 'Customers no longer see it.',
  'vehicleDetail.itIsNotOffered': 'It is not offered while it is off the road.',
  'vehicleDetail.itIsHiddenUntil': 'It is hidden until you publish it again.',
  'vehicleDetail.thatDidNotGo': 'That did not go through',
  'vehicleDetail.addAtLeastOne': 'Add at least one photo before publishing.',
  'vehicleDetail.theServiceDidNot': 'The service did not respond.',

  // Keyed by key-copy.js, 2026-09-08.
  'vehicleWizard.vehicleTypesCouldNot': 'Vehicle types could not be loaded.',
  'vehicleWizard.listing': 'Listing',
  'vehicleWizard.publishedOnSave': 'Published on save',
  'vehicleWizard.keptAsADraft': 'Kept as a draft',
  'vehicleWizard.nothingIsPublishedUntil': 'Nothing is published until you save.',
  'vehicleWizard.savedAsADraft': 'Saved as a draft in your fleet',
  'vehicleWizard.becomesADraftOnce': 'Becomes a draft once you reach Photos',
  'vehicleWizard.useAJpegPng': 'Use a JPEG, PNG or WebP image.',
  'vehicleWizard.theUploadDidNot': 'The upload did not go through.',
  'vehicleWizard.addAtLeastOne': 'Add at least one photo before publishing, or save it as a draft.',
  'vehicleWizard.yourDealershipCannotTrade':
    'Your dealership cannot trade right now, so the car was saved as a draft instead of published.',
  'vehicleWizard.theCarWasSaved': 'The car was saved as a draft; publishing did not go through.',
  'vehicleWizard.vehiclePublished': 'Vehicle published',
  'vehicleWizard.draftSaved': 'Draft saved',
  'vehicleWizard.draftKept': 'Draft kept',
  'vehicleWizard.thatPlateIsAlready': 'That plate is already on another car on the platform.',

  // Keyed by key-copy.js, 2026-09-08.
  'myBooking.thatBookingWasNot': 'That booking was not found.',
  'myBooking.thePlatformBookingRecord': 'The platform booking record is for administrators.',
  'myBooking.rentalTotal': 'Rental total',
  'myBooking.totalPrice': 'Total price',
  'myBooking.balanceDue': 'Balance due',
  'myBooking.freeCancellationWindow': 'Free cancellation window',
  'myBooking.paymentWindow': 'Payment window',
  'myBooking.noShowTimeout': 'No-show timeout',
  'myBooking.settlementWindowAfterReturn': 'Settlement window after return',
  'myBooking.customerCancellationPenalty': 'Customer cancellation penalty',
  'myBooking.dealerNonDeliveryPenalty': 'Dealer non-delivery penalty',
  'myBooking.delistedSinceThisBooking': 'Delisted since this booking was made',
  'myBooking.handover': 'Handover',
  'myBooking.selfPickup': 'Self pickup',
  'myBooking.reason': 'reason',
  'myBooking.theDepositWasNever': 'The deposit was never paid inside the payment window.',
  'myBooking.theDealerNeverAnswered': 'The dealer never answered inside their window.',

  // Keyed by key-copy.js, 2026-09-08.
  'settings.platformSettingsAreFor': 'Platform settings are for administrators.',
  'settings.thePlatformSettingsCould':
    'The platform settings could not be loaded. Nothing has been changed.',
  'settings.bookingDeposit': 'Booking deposit',
  'settings.dealerApplicationReviewSla': 'Dealer application review SLA',
  'settings.customerCancelsAfterThe': 'Customer cancels after the free window',
  'settings.dealerFailsToDeliver': 'Dealer fails to deliver',
  'settings.minimumRenterAge': 'Minimum renter age',
  'settings.notSetNobodyIs': 'Not set — nobody is refused on age',

  // Keyed by key-copy.js, 2026-09-08.
  'lookups.carTypes': 'Car types',
  'lookups.thePlacesCustomersSearch':
    'The places customers search in. A retired city stays on every dealership and booking that already names it.',
  'lookups.theCategoriesACar':
    'The categories a car is listed under. A retired type stays on every vehicle that already names it.',
  'lookups.platformLookupsAreCurated': 'Platform lookups are curated by administrators.',
  'lookups.theListCouldNot': 'The list could not be loaded. Nothing has been changed.',
  'lookups.addACarType': 'Add a car type',
  'lookups.customersFilterTheirSearch':
    'Customers filter their search by this list, in whichever language they are using.',
  'lookups.dealersChooseFromThis':
    'Dealers choose from this list when they list a car, and customers filter by it.',
  'lookups.nameEnglish': 'Name (English)',
  'lookups.nameArabic': 'Name (Arabic)',
  'lookups.addCity': 'Add city',
  'lookups.addCarType': 'Add car type',
  'lookups.renamed': 'Renamed',
  'lookups.itIsOfferedAgain': 'It is offered again on new listings and searches.',
  'lookups.itStopsBeingOffered':
    'It stops being offered on new listings and searches. Everything already using it is untouched — this is not a delete, and there is no delete.',

  // Keyed by key-copy.js, 2026-09-08.
  'dealerProfile.closedAllWeek': 'Closed all week',
  'dealerProfile.yourDealerPageCould':
    'Your dealer page could not be loaded. Nothing has been changed.',
  'dealerProfile.dealerPageSaved': 'Dealer page saved',
  'dealerProfile.customersSeeTheNew': 'Customers see the new details straight away.',
  'dealerProfile.theBusinessNameIs':
    'The business name is locked: it is the name your licence was verified against. Ask the platform if it has to change.',
  'dealerProfile.logoUpdated': 'Logo updated',
  'dealerProfile.coverUpdated': 'Cover updated',
  'dealerProfile.itIsLiveOn': 'It is live on your public page.',
  'dealerProfile.theUploadDidNot': 'The upload did not go through. Nothing has been changed.',

  // Keyed by key-copy.js, 2026-09-08.
  'dealerReview.submitted': 'Submitted',
  'dealerReview.lastReviewNote': 'Last review note',
  'dealerReview.thatDealerApplicationNo': 'That dealer application no longer exists.',
  'dealerReview.onlyAdministratorsCanReview': 'Only administrators can review dealer applications.',
  'dealerReview.theApplicationCouldNot':
    'The application could not be loaded. Nothing has been changed.',
  'dealerReview.note2': 'Note',

  // Keyed by key-copy.js, 2026-09-08.
  'customerProfile.thatCustomerWasNot': 'That customer was not found.',
  'customerProfile.customerRecordsAreFor': 'Customer records are for administrators.',
  'customerProfile.theCustomerCouldNot':
    'The customer could not be loaded. Nothing has been changed.',
  'customerProfile.phone': 'Phone',
  'customerProfile.emailVerified': 'Email verified',
  'customerProfile.dateOfBirth': 'Date of birth',
  'customerProfile.notGiven': 'Not given',
  'customerProfile.foreignNational': 'Foreign national',
  'customerProfile.passwordLastChanged': 'Password last changed',
  'customerProfile.liveNow': 'Live now',

  // Keyed by key-copy.js, 2026-09-08.
  'dealerApply.applicationSubmitted': 'Application submitted',

  // Keyed by key-copy.js, 2026-09-08.
  'dealerStaff.yourDealershipCanNo': 'Your dealership can no longer manage staff just now.',
  'dealerStaff.yourStaffListCould':
    'Your staff list could not be loaded. Nothing has been changed.',
  'dealerStaff.email': 'email',
  'dealerStaff.phone': 'phone',
  'dealerStaff.no': 'No',
  'dealerStaff.yes': 'Yes',
  'dealerStaff.nameEmailAndPhone': 'Name, email and phone are all required.',
  'dealerStaff.invitationResent': 'Invitation resent',
  'dealerStaff.reportAccessGranted': 'Report access granted',
  'dealerStaff.reportAccessRemoved': 'Report access removed',
  'dealerStaff.staffMemberReactivated': 'Staff member reactivated',

  // Keyed by key-copy.js, 2026-09-08.
  'employeeNotif.yourNotificationsCouldNot':
    'Your notifications could not be loaded. Nothing has been changed.',
  'employeeNotif.newRequest': 'New request',
  'employeeNotif.bookingDecision': 'Booking decision',
  'employeeNotif.yourAccess': 'Your access',
  'employeeNotif.markedAsRead': 'Marked as read',

  // Keyed by key-copy.js, 2026-09-08.
  'carForm.addAtLeastOne': 'Add at least one photo before you can publish this car.',
  'carForm.thisCarIsA': 'This car is a draft. Publish it from your fleet when you are ready.',
  'carForm.chooseAVehicleType': 'Choose a vehicle type before saving this car.',
  'carForm.carUpdated': 'Car updated',
  'carForm.carAdded': 'Car added',

  // Keyed by key-copy.js, 2026-09-08.
  'employeeSettings.approveAndRejectRequests':
    'Approve and reject requests, and record pickups and returns.',
  'employeeSettings.yourDealershipCannotTake':
    'Your dealership cannot take new bookings just now, so approving and rejecting are paused. Returns can still be recorded.',
  'employeeSettings.revenueCommissionAndOccupancy':
    'Revenue, commission and occupancy are visible to you.',
  'employeeSettings.revenueCommissionAndOccupancy2':
    'Revenue, commission and occupancy are hidden. Your owner can turn this on.',
  'employeeSettings.everyOtherSessionHas': 'Every other session has been signed out.',
  'employeeSettings.theCurrentPasswordIs': 'The current password is wrong.',

  // Keyed by key-copy.js, 2026-09-08.
  'fleetList.youHaveNotSubmitted':
    'You have not submitted a dealer application yet, so there is no fleet to manage.',
  'fleetList.onlyDealerStaffCan': 'Only dealer staff can manage a fleet.',
  'fleetList.yourFleetCouldNot': 'Your fleet could not be loaded. Nothing has been changed.',

  // Keyed by key-copy.js, 2026-09-08.
  'dealerActivity.activityCouldNotBe': 'Activity could not be loaded. Nothing has been changed.',
  'dealerActivity.paymentReceivedBookingConfirmed': 'Payment received — booking confirmed',
  'dealerActivity.markedNoShow': 'Marked no-show',
  'dealerActivity.expiredUnanswered': 'Expired unanswered',
  'dealerActivity.expiredUnpaid': 'Expired unpaid',

  // Keyed by key-copy.js, 2026-09-08.
  'dealerDispute.thatDisputeIsNot': 'That dispute is not yours to see, or no longer exists.',
  'dealerDispute.theDisputeCouldNot': 'The dispute could not be loaded. Nothing has been changed.',
  'dealerDispute.statementAdded': 'Statement added',
  'dealerDispute.thePlatformAndThe': 'The platform and the customer can read it.',

  // Keyed by key-copy.js, 2026-09-08.

  // Keyed by key-copy.js, 2026-09-08.
  'adminUsers.administratorAccountsAreManaged':
    'Administrator accounts are managed by administrators.',
  'adminUsers.theAdministratorsCouldNot':
    'The administrators could not be loaded. Nothing has been changed.',
  'adminUsers.youCannotDeactivateYour': 'You cannot deactivate your own account.',
  'adminUsers.thisIsTheLast': 'This is the last active administrator. Invite another first.',

  // Keyed by key-copy.js, 2026-09-08.

  // Keyed by key-copy.js, 2026-09-08.

  // Keyed by key-copy.js, 2026-09-08.
  'bookingsList.thePlatformBookingList': 'The platform booking list is for administrators.',
  'bookingsList.theBookingsCouldNot': 'The bookings could not be loaded. Nothing has been changed.',
  'bookingsList.vehicleDelisted': 'Vehicle delisted',

  // Keyed by key-copy.js, 2026-09-08.
  'dealerBookings.yourBookingsCouldNot':
    'Your bookings could not be loaded. Nothing has been changed.',
  'dealerBookings.vehicleNoLongerListed': 'Vehicle no longer listed',

  // The dispute workspace.
  'disputeDetail.thatDisputeWasNot': 'That dispute was not found.',
  'disputeDetail.theDisputeWorkspaceIs': 'The dispute workspace is for administrators.',
  'disputeDetail.handledBy': 'Handled by',
  'disputeDetail.decision': 'Decision',
  'disputeDetail.assignedToYou': 'Assigned to you',
  'disputeDetail.recordedOnTheTicket': 'Recorded on the ticket; any admin can still resolve it.',

  // The resolution presets and the server refusals the dispute workspace maps.
  'disputeDetail.theWholeDepositGoes':
    'The whole deposit goes back. Nothing is kept and nothing reaches the dealer.',
  'disputeDetail.theDepositIsSplit':
    'The deposit is split the way the booking assessed it, against the party at fault.',
  'disputeDetail.partial': 'Partial',
  'disputeDetail.youSetEachLeg': 'You set each leg. The three must add up to the deposit held.',
  'disputeDetail.noPenaltyTheDeposit':
    'No penalty. The deposit returns to the customer and the booking closes clean.',
  'disputeDetail.theThreeAmountsMust': 'The three amounts must add up to exactly the deposit held.',
  'disputeDetail.aNoteIsRequired': 'A note is required so both parties can see the reasoning.',
  'disputeDetail.thisTicketHasAlready': 'This ticket has already been resolved.',

  // test

  // Server refusals mapped by error code, keyed 2026-09-08.
  'dealerApply.theServiceDidNot':
    'The service did not respond. Nothing was submitted; try again shortly.',
  'dealerApply.thisAccountHasAlready':
    'This account has already submitted a gallery. Reload the console to see where it stands.',
  'dealerApply.aGalleryIsAlready':
    'A gallery is already registered with that commercial registration number.',
  'dealerApply.allThreeDocumentsAre':
    'All three documents are required. Attach the missing one and submit again.',
  // Pre-launch item 230: the platform's own name, in either script.
  'dealerApply.nameReserved':
    'That name is the platform’s own, so an office cannot use it. Use your office’s own business name.',
  'dealerApply.oneOfTheFiles':
    'One of the files is larger than the upload limit. Attach a smaller copy.',
  'dealerApply.uploadEachDocumentAs': 'Upload each document as a JPEG, PNG or PDF.',
  'dealerApply.closingTimeMustBe': 'Closing time must be later in the day than opening time.',
  'dealerApply.theDocumentsTogetherAre':
    'The documents together are larger than the upload limit. Attach smaller copies.',
  'dealerApply.theApplicationWasRejected':
    'The application was rejected. Check the details and try again.',

  // Keyed by key-copy.js, 2026-09-08.

  // Server refusals mapped by error code, keyed 2026-09-08.
  'acceptInvite.thisInvitationIsNo':
    'This invitation is no longer valid — it may have expired or already been used. Ask the dealer owner to send a new one.',
  'acceptInvite.thatPasswordDoesNot': 'That password does not meet the policy. Try a longer one.',

  // Keyed by key-copy.js, 2026-09-08.

  // Server refusals mapped by error code, keyed 2026-09-08.
  'resetPassword.thisLinkIsInvalid':
    'This link is invalid or has expired. Request a new one from the sign-in page.',

  // Keyed by key-copy.js, 2026-09-08.

  // Server refusals mapped by error code, keyed 2026-09-08.

  // Keyed by key-copy.js, 2026-09-08.
  'security.yourSessionsCouldNot': 'Your sessions could not be loaded. Nothing has been changed.',
  'security.ifThisIsThe': 'If this is the session you are using now, you will be signed out.',
  'security.deviceNotRecorded': 'Device not recorded',

  // Server refusals mapped by error code, keyed 2026-09-08.
  'dealerDispute.thisDisputeIsClosed': 'This dispute is closed; nothing more can be added to it.',

  // Keyed by key-copy.js, 2026-09-08.

  // Server refusals mapped by error code, keyed 2026-09-08.

  // Keyed by key-copy.js, 2026-09-08.
  'disputesList.theDisputeQueueIs': 'The dispute queue is for administrators.',
  'disputesList.theDisputeQueueCould':
    'The dispute queue could not be loaded. Nothing has been changed.',

  // Server refusals mapped by error code, keyed 2026-09-08.
  'carForm.yourDealershipIsNot':
    'Your dealership is not approved yet, so you cannot manage cars. You will be able to once an administrator approves your application.',
  'carForm.aCarWithThat': 'A car with that plate number is already listed on the platform.',
  'carForm.theServiceDidNot': 'The service did not respond. Nothing has been saved.',

  // Keyed by key-copy.js, 2026-09-08.

  // Server refusals mapped by error code, keyed 2026-09-08.

  // Keyed by key-copy.js, 2026-09-08.
  'employeeBusiness.notShownToStaff': 'Not shown to staff',

  // Server refusals mapped by error code, keyed 2026-09-08.

  // Keyed by key-copy.js, 2026-09-08.

  // Notification sentences. Named parameters rather than concatenation: Arabic does not put the actor and the object where English does, so each is ONE message with holes in it.
  'notifications.you': 'You',
  'notifications.aBooking': 'a booking',
  'notifications.customerRequested': 'A customer requested {what}',
  // One notification for a deposit and for a payment in full; the row does not say which.
  'notifications.customerPaid': 'A customer paid for {what}',
  'notifications.approved': '{who} approved {what}',
  // What the reader did themselves has sentences of its own: Arabic says «أنت من قبِل», never «قبل أنت» (item 218).
  'notifications.approvedByYou': 'You approved {what}',
  'notifications.rejected': '{who} rejected {what}',
  'notifications.rejectedByYou': 'You rejected {what}',
  'notifications.recordedPickup': '{who} recorded the pickup for {what}',
  'notifications.recordedPickupByYou': 'You recorded the pickup for {what}',
  'notifications.recordedReturn': '{who} recorded the return for {what}',
  'notifications.recordedReturnByYou': 'You recorded the return for {what}',
  'notifications.updated': '{who} updated {what}',
  'notifications.updatedByYou': 'You updated {what}',
  'notifications.dealerApproved': 'Your dealership was approved',
  'notifications.dealerRejected': 'Your dealership’s application was rejected',
  'notifications.dealerClarification': 'The platform asked for more on your application',
  'notifications.dealerSuspended': 'Your dealership was suspended',
  'notifications.dealerReactivated': 'Your dealership is trading again',
  'notifications.staffReactivated': '{who} reactivated a member of staff',
  'notifications.staffReactivatedByYou': 'You reactivated a member of staff',
  'notifications.reportAccessGranted': '{who} gave you access to financial reports',
  'notifications.reportAccessRevoked': '{who} removed your access to financial reports',
  // Wave 3 (C5, F55): what a customer did, and what the platform did, worded here; the row stores no sentence.
  'notifications.aCustomer': 'A customer',
  'notifications.customerCancelled': 'A customer cancelled {what}',
  'notifications.customerReportedNonDelivery':
    'A customer reported that the car for {what} was not handed over',
  'notifications.customerOpenedDispute': 'A customer opened a dispute on {what}',
  'notifications.openedDispute': '{who} opened a dispute on {what}',
  'notifications.disputeResolved': 'Khadra decided the dispute on {what}',
  'notifications.completed': 'Khadra completed {what}',
  'notifications.markedNoShow': 'Khadra marked {what} a no-show',
  'notifications.expiredUnpaid': 'Nobody paid for {what} in time, so it expired',
  'notifications.cancelledByKhadra': 'Khadra cancelled {what}',
  'notifications.settlementRecorded': 'Khadra recorded settlement {what}',
  'notifications.settlementVoided': 'Khadra voided settlement {what}',

  // The notification presenter's plural helpers.

  // The attention queue's plural counts. A plural message, not an n === 1 ternary: Arabic has six forms.
  'notifications.carsOverdue': {
    one: '{count} car is overdue back',
    other: '{count} cars are overdue back',
  },
  'notifications.requestsWaiting': {
    one: '{count} booking request is waiting',
    other: '{count} booking requests are waiting',
  },
  'notifications.pastTheEndOf': 'Past the end of the rental period and not yet returned.',
  'notifications.aRequestExpiresWhen': 'An unanswered request expires at its answer deadline.',
  'notifications.oldestAndExpiry':
    'Oldest {when}. The next one expires {deadline} unless it is answered.',

  // Keyed by key-copy.js, 2026-09-08.
  'auditLog.theAuditLogIs': 'The audit log is for administrators.',
  'auditLog.theAuditLogCould': 'The audit log could not be loaded. Nothing has been changed.',

  // Keyed by key-copy.js, 2026-09-08.
  'customersList.theCustomerListIs': 'The customer list is for administrators.',
  'customersList.theCustomersCouldNot':
    'The customers could not be loaded. Nothing has been changed.',

  // Keyed by key-copy.js, 2026-09-08.
  'dealerReports.reportsAreForThe':
    'Reports are for the dealer owner and staff they have granted access to. Ask the owner if you need them.',
  'dealerReports.reportsCouldNotBe': 'Reports could not be loaded. Nothing has been changed.',

  // Keyed by key-copy.js, 2026-09-08.
  'registerDealer.fillInEveryField': 'Fill in every field before continuing.',

  // Keyed by key-copy.js, 2026-09-08.

  // Keyed by key-copy.js, 2026-09-08.

  // Console audit 2026-09-13: shared time phrases, refusals, scoped statuses, and the words for records that no longer resolve
  'time.minutesShort': {
    one: '{count}m',
    other: '{count}m',
  },
  'time.hoursShort': {
    one: '{count}h',
    other: '{count}h',
  },
  'time.daysShort': {
    one: '{count}d',
    other: '{count}d',
  },
  'common.requestRefused': 'That was refused. Nothing has been changed.',
  'common.fieldRejected': 'Check this field.',

  // What a refusal MEANS, worded here rather than left to the server's English. See
  // `problemMessage` in core/i18n/problem.ts for the order these are chosen in. Each says what to
  // do next, because an operator reading one is in the middle of trying to do something.
  'problem.emailTaken':
    'That email address already belongs to an account on Khadra. Use a different address.',
  'problem.phoneTaken':
    'That phone number already belongs to an account on Khadra. Use a different number.',
  'problem.invalidEmail': 'That email address is not valid. Check it and send again.',
  'problem.invalidPhone':
    'That phone number is not valid. Use 07XXXXXXXX, or an international number beginning with +.',
  'problem.invalidName': 'The full name must be between 2 and 150 characters.',
  'problem.accountSuspended': 'That account is deactivated.',
  'problem.invalidToken': 'That link is no longer valid. A new one has to be issued.',
  'problem.invitationAccepted':
    'That administrator has already set a password, so there is nothing to resend.',
  'problem.invitationTargetInactive':
    'That account is deactivated. Reactivate it before sending the invitation again.',
  'problem.invitationEmailNotSent':
    'The mail service would not take the message. The new link is valid — try again in a few minutes.',
  'problem.documentNotCurrent': 'Only the current version of a document can be voided. The page now shows where it stands.',
  'problem.documentAlreadyVoided': 'This document has already been voided. The page now shows its void.',
  'problem.correctionRecordsNeedReview':
    "The booking's records contradict one another, so no corrected document can be issued. Nothing was voided.",
  'problem.correctionIssuerNotConfigured':
    "Khadra's legal identity is not configured for documents, so no corrected document can be issued. Nothing was voided.",
  'problem.correctionFailed': 'The corrected document could not be composed. Nothing was voided; the failure has been logged.',
  'problem.pdfNotReady': 'This PDF is still being drawn. Try again shortly.',
  'problem.pdfNotVoided': 'This document is not voided, so it has no voided copy.',
  'problem.documentNotEmailed': 'Only receipts are emailed to the customer.',
  'problem.documentVoidedNotEmailed': 'This receipt was voided, so it is not emailed again. Its correction is the one to send.',
  'problem.emailAlreadyQueued': 'An email of this receipt is already queued. The page now shows it.',
  'problem.emailDeliveryDisabled':
    "Receipt emails are switched off on this server until Brevo's protection against duplicate sends is verified. Nothing was queued.",
  'problem.reasonRejected': 'Check the reason: it is missing, or longer than the platform allows.',
  'problem.signedOut': 'Your session has ended. Sign in again and retry.',
  'problem.notPermitted': 'Your account is not allowed to do that.',
  'problem.notFound': 'That record no longer exists. Reload the screen.',
  'problem.documentChangedSinceViewed':
    'This file changed after you opened it: the customer uploaded a new one, or another administrator acted on it. Close this, then open the file again before deciding.',
  'problem.documentNotViewed':
    'Open the file first, with the link above: a rejection names a file you have looked at. Then reject it.',
  'problem.documentNotFound': "That file is no longer on the customer's record. Reload the page.",
  'problem.conflict': 'That conflicts with the record as it stands now. Reload and check it.',
  'problem.tooMany': 'Too many attempts. Wait a moment and try again.',
  'problem.unavailable': 'The service could not complete that. Nothing has been changed.',
  'problem.rejectedDetails': 'The details were rejected. Check them and send again.',
  'problem.reference': 'reference {traceId}',
  // Handover refusals. An office once pressed a refusal it could not see three times, and used three of the
  // customer's five tries (E2E F53); each of these says what to do next.
  'problem.handoverCodeInvalid': 'That code is not right. Ask the customer to check it, or to show a new one.',
  'problem.handoverCodeInvalidTries': {
    one: 'That code is not right. {count} try left before it locks — ask the customer to check it, or to show a new one.',
    other: 'That code is not right. {count} tries left before it locks — ask the customer to check it, or to show a new one.',
  },
  'problem.handoverCodeExpired': 'That code has expired. Ask the customer to show a new one.',
  'problem.handoverCodeUsed': 'That code has already been used.',
  'problem.handoverCodeLocked': 'Too many wrong codes, so this one is locked. Ask the customer to show a new one.',
  'problem.handoverCodeRequired': "Enter the customer's code, or record the handover as unverified and say why.",
  'problem.handoverReasonRequired': 'Say a little more about why the handover could not be verified.',
  'problem.handoverNotAvailable': 'There is no handover to record on this booking right now. Reload the page.',
  'problem.handoverInvalidOdometer': 'The odometer reading cannot be negative.',
  'problem.handoverInvalidFuel': 'The fuel level must be between 0 (empty) and 1 (full).',
  'problem.pickupTooEarly': 'It is too early to record this pickup. The booking shows when it can be recorded.',
  'problem.returnTooEarly': 'The rental has not started yet, so its return cannot be recorded. The booking shows when it can be.',
  'common.customerAccountClosed': 'Customer account closed',
  'common.dealerNoLongerOnPlatform': 'Dealer no longer on the platform',
  'common.accountClosed': 'Account closed',
  'common.formerStaffMember': 'Former staff member',
  'common.theRentalOffice': 'The rental office',
  'common.unassigned': 'Unassigned',
  'status.requestedDealerBooking': 'Pending',
  'status.approvedDealerBooking': 'Awaiting payment',
  'status.pickedUpDealerBooking': 'Active',
  'queue.overdue': 'Overdue',
  'queue.slaLeft': 'SLA {duration}',
  'queue.actionResolve': 'Resolve',
  'queue.actionReview': 'Review',
  'queue.actionOpen': 'Open',
  'adminDashboard.bookingsOnDay': {
    one: '{day}: {count} booking',
    other: '{day}: {count} bookings',
  },
  'notifications.overdue': 'Overdue',
  'notifications.toAnswer': 'To answer',
  'notifications.pickupRow': 'Pickup — {vehicle}',
  'notifications.returnRow': 'Return — {vehicle}',
  'dealerReports.today': 'Today',
  'dealerBooking.rentalLine': 'Rental · {count} × {rate}',
  'dealerBooking.depositPaidByCard': 'Deposit paid by card ({percent})',
  'dealerBooking.paidInFullByCard': 'Paid in full by card',
  // {rate} is a commission-rate phrase that names its basis ("20% of one daily rate"), never a bare
  // percent: see core/i18n/commission-rate.ts.
  'dealerBooking.platformCommissionFrozen':
    'Platform commission · {rate} (frozen on this booking)',
  'dealerBooking.amountFrozen': '{amount} · frozen on this booking',
  'dealerBooking.mileageAllowance': '{limit} km/day, {fee}/km over',

  // Console audit 2026-09-13: the dealer bookings list and activity feed
  'dealerActivity.wasStatus': '(was {status})',
  // Wave 3, F27: who made a change the office did not make.
  'dealerActivity.actor.customer': 'The customer',
  'dealerActivity.actor.khadra': 'Khadra',
  'dealerActivity.pageSummary': {
    one: '{shown} of {count} change',
    other: '{shown} of {count} changes',
  },
  'dealerBookings.noBookingsYet': 'No bookings yet',
  'dealerBookings.nothingInThisTab': 'Nothing in this tab',
  'dealerBookings.dealerLocation': 'Dealer location',
  'dealerBookings.tabPending': 'Pending',
  'dealerBookings.tabUpcoming': 'Upcoming',
  'dealerBookings.tabActive': 'Active',
  'dealerBookings.tabReturned': 'Returned',
  'dealerBookings.tabCompleted': 'Completed',
  'dealerBookings.tabClosed': 'Closed',
  'dealerBookings.pageSummary': {
    one: '{shown} of {count} booking',
    other: '{shown} of {count} bookings',
  },
  'dealerBookings.periodRange': '{start} → {end}',

  // Wave Two 2026-09-17: the dealer application timeline, worded from the server's facts
  'dealerReview.timelineMeta': '{detail} · {when}',
  'dealerReview.stepSubmitted': 'Application submitted',
  'dealerReview.stepDocumentsAttached': 'Documents attached',
  'dealerReview.documentsOfRequired': '{have} of {need} required',
  'dealerReview.stepApproved': 'Approved',
  'dealerReview.stepRejected': 'Rejected',
  'dealerReview.stepClarificationRequested': 'Clarification requested',
  'dealerReview.stepDecisionRecorded': 'Decision recorded',
  'dealerReview.noNoteRecorded': 'No note recorded.',
  'dealerReview.stepResubmitted': 'Resubmitted by the dealer',
  'dealerReview.resubmittedDetail': 'The application was corrected and sent back for review.',
  'dealerReview.stepAwaitingDecision': 'Awaiting admin decision',
  'dealerReview.noDecisionRecordedYet': 'No decision has been recorded yet.',

  // Wave Two 2026-09-17: dispute parties, holders and closed accounts, worded from the server's facts
  'party.customer': 'Customer',
  'party.dealer': 'Dealer',
  'party.system': 'System',
  'party.unattributed': 'Unattributed',
  'party.admin': 'Admin',
  'disputesList.withHolder': 'With {name}',
  'disputesList.heldByClosedAccount': 'With a closed account',
  'disputeDetail.openedLine': 'Opened {when} by {name} ({party})',
  'disputeDetail.betweenParties': '{dealer} and {customer}',
  'disputeDetail.nameWithParty': '{name} ({party})',
  'disputeDetail.partyOpenedIt': '{party} · opened it',
  'disputeDetail.openedBy': 'Opened by {name}',
  'disputeDetail.answeredBy': '{name} answered',
  'disputeDetail.takenOnBy': 'Taken on by {name}',
  'disputeDetail.resolvedBy': 'Resolved by {name}',
  'disputeDetail.filesAttached': {
    one: '{count} file',
    other: '{count} files',
  },
  'disputeDetail.quoted': '“{text}”',
  'disputeDetail.dueAt': 'Due {when}',
  'disputeDetail.resolveConfirmBody':
    "{refund} back to {customer}, {platform} kept by the platform, {dealerShare} to {dealer}. The customer sees the decision as Khadra's, with your note but never your name; the office sees only its own share, with your note and your name. All of it is written to the audit log.",
  'disputeDetail.resolveConfirmBodyWithCharge':
    "{refund} back to {customer}, {platform} kept by the platform, {dealerShare} to {dealer}, and {charge} charged to the dealer. The customer sees the decision as Khadra's, with your note but never your name; the office sees only its own share and the charge, with your note and your name. All of it is written to the audit log.",
  'dealerDispute.openedByYourSide': 'Opened {when} by {name} (your side)',
  'dealerDispute.openedByCustomer': 'Opened {when} by {name} (the customer)',
  'dealerDispute.handledByName': 'Handled by {name}',
  'dealerDispute.yourSide': 'Your side',

  // Wave Two 2026-09-17: booking parties that left the platform, worded from the server's flags
  'bookingsList.scopedToDealer': 'Showing one dealer: {name}',
  'bookingsList.scopedToCustomer': 'Showing one customer: {name}',
  'bookingsList.scopedToOneParty': 'Showing one party',
  'adminBooking.cancelTitle': 'Cancel {reference}?',
  'adminBooking.cancelBody':
    'The booking ends now and the car is released. No penalty is assessed against {customer} or {dealer} — the platform is cancelling, not either party.',
  'adminBooking.expireTitle': 'Expire {reference}?',
  'adminBooking.expireBody':
    '{which} Expiring releases the car. No penalty is assessed against anyone.',
  'adminBooking.noShowTitle': 'Mark {reference} as a no-show?',
  'adminBooking.noShowBody':
    "{customer} never collected the car. This assesses whatever this booking's own terms say is owed. Nothing is charged now: a penalty on the customer is kept from the deposit only when the dispute window closes with no dispute.",
  'dealerBooking.requestedAt': 'Requested {when}',

  // Wave Two 2026-09-17: one clock for every deadline and SLA — remaining, overdue by, expired
  'time.remainingMinutes': {
    one: '{count}m remaining',
    other: '{count}m remaining',
  },
  'time.remainingHours': {
    one: '{count}h remaining',
    other: '{count}h remaining',
  },
  'time.remainingDays': {
    one: '{count}d remaining',
    other: '{count}d remaining',
  },
  'time.overdueMinutes': {
    one: 'Overdue by {count}m',
    other: 'Overdue by {count}m',
  },
  'time.overdueHours': {
    one: 'Overdue by {count}h',
    other: 'Overdue by {count}h',
  },
  'time.overdueDays': {
    one: 'Overdue by {count}d',
    other: 'Overdue by {count}d',
  },
  'time.overdue': 'Overdue',
  'time.expired': 'Expired',

  // Wave Two 2026-09-17: the six hand-written clocks, on the shared SLA and deadline readings
  'dealerReview.settled': 'Settled',
  'dealerReview.pastTheReviewSla': {
    one: 'Past the {count}-hour review SLA.',
    other: 'Past the {count}-hour review SLA.',
  },
  'dealerReview.untilTheReviewSla': {
    one: 'Until the {count}-hour review SLA.',
    other: 'Until the {count}-hour review SLA.',
  },
  'disputeDetail.closed': 'Closed',
  'disputesList.closedAt': 'Closed {when}',
  'dealerDispute.platformDeadline': 'Platform deadline: {clock}',
  'dealerBooking.expiresAt': 'Expires {when}.',

  // Wave Two 2026-09-17: shared words — a fleet car's status, handover types
  'status.activeVehicle': 'Published',
  'status.maintenanceVehicle': 'Off the road',
  'handoverType.pickup': 'Pickup',
  'handoverType.return': 'Return',

  // Wave Two 2026-09-17: common words shared by every console, created once before the screen sweep
  'common.yes': 'Yes',
  'common.no': 'No',
  'common.on': 'On',
  'common.off': 'Off',
  'common.never': 'Never',
  'common.saving': 'Saving…',
  'common.none': 'None',
  'common.save': 'Save',
  'common.details': 'Details',
  'common.hide': 'Hide',

  // Wave Two 2026-09-17: the shared map and the console's own failure toast
  'map.location': 'Location',
  'common.thatDidNotGoThrough': 'That did not go through',
  'common.serviceDidNotRespond': 'The service did not respond.',

  // Wave Two 2026-09-17: Dealer and Employee dashboards, employee notifications — whole messages shared by both dashboards (D10)
  'employeeDash.goodMorningName': 'Good morning, {name}',
  'employeeDash.goodAfternoonName': 'Good afternoon, {name}',
  'employeeDash.goodEveningName': 'Good evening, {name}',
  'employeeDash.oldestWhen': 'oldest {when}',
  'employeeDash.nextWhen': 'next: {when}',
  'employeeDash.noneInNextHours': {
    one: 'none in the next {count}h',
    other: 'none in the next {count}h',
  },
  'employeeDash.overdueCount': {
    one: '{count} overdue',
    other: '{count} overdue',
  },
  'employeeDash.requestsWaitingForAnswer': {
    one: '{count} booking request is waiting for an answer',
    other: '{count} booking requests are waiting for an answer',
  },
  'employeeDash.wasDueBack': '{vehicle} was due back {when}',
  'employeeDash.pickupAt': '{vehicle} pickup {when}',
  'employeeDash.deliveryAt': '{vehicle} delivery {when}',
  'employeeDash.pickupsNextHours': {
    one: 'Pickups · next {count}h',
    other: 'Pickups · next {count}h',
  },
  'employeeDash.returnsNextHours': {
    one: 'Returns · next {count}h',
    other: 'Returns · next {count}h',
  },
  'employeeDash.oldestArrived': 'The oldest arrived {when}.',
  'employeeDash.overdueDesc':
    '{customer} has not brought the car back. Record the return when it arrives.',
  'employeeDash.pickupDesc': '{customer}. Record the handover when the car leaves.',
  'employeeDash.dueBackAt': '{vehicle} due back {when}',
  'employeeDash.returnDesc': '{customer}’s rental ends. Confirm the return and note any damage.',
  'employeeDash.activityApproved': 'Approved {reference}',
  'employeeDash.activityRejected': 'Rejected {reference}',
  'employeeDash.activityHandedOver': 'Handed over {reference}',
  'employeeDash.activityTookBack': 'Took back {reference}',
  'employeeDash.activityCancelled': 'Cancelled {reference}',
  'employeeDash.activityOther': '{status} {reference}',
  'dealerDash.statusLine': {
    one: '{requests} waiting, {pickups} and {returns} in the next hour.',
    other: '{requests} waiting, {pickups} and {returns} in the next {count} hours.',
  },
  'dealerDash.bookingRequestsCount': {
    one: '{count} booking request',
    other: '{count} booking requests',
  },
  'dealerDash.pickupsCount': {
    one: '{count} pickup',
    other: '{count} pickups',
  },
  'dealerDash.returnsCount': {
    one: '{count} return',
    other: '{count} returns',
  },
  'dealerDash.ofPublished': {
    one: 'of {count} published',
    other: 'of {count} published',
  },
  'dealerDash.inYourFleet': {
    one: '{count} in your fleet',
    other: '{count} in your fleet',
  },
  'dealerDash.oldestMadeExpiry':
    'The oldest was made {when}. The next one expires {deadline} unless it is answered.',
  // Wave 3, F24: when the first waiting request expires, the server's moment.
  'dealerDash.nextExpires': 'next expires {when}',
  'dealerDash.overdueDesc':
    '{customer} has not returned the car. Record the return when it comes back, and note any damage within the settlement window.',
  'dealerDash.pickupDesc':
    '{customer}. Record the handover with the odometer and fuel level when the keys change hands.',
  'dealerDash.returnAt': '{vehicle} return {when}',
  'dealerDash.returnDesc':
    '{customer}’s rental ends. Confirm the return and note any damage within the settlement window.',
  'dealerDash.activityApproved': '{actor} approved booking {reference}',
  'dealerDash.activityRejected': '{actor} rejected booking {reference}',
  'dealerDash.activityHandedOver': '{actor} handed over booking {reference}',
  'dealerDash.activityTookBack': '{actor} took back booking {reference}',
  'dealerDash.activityCancelled': '{actor} cancelled booking {reference}',
  // Wave 3, F27: the customer's and the platform's changes.
  'dealerDash.activityRequested': '{actor} requested booking {reference}',
  'dealerDash.activityPaid': '{actor} paid for booking {reference}',
  'dealerDash.activityExpiredUnanswered': 'Booking {reference} expired unanswered',
  'dealerDash.activityExpiredUnpaid': 'Booking {reference} expired unpaid',
  'dealerDash.activityNoShow': 'Booking {reference} was marked a no-show',
  'dealerDash.activityCompleted': 'Booking {reference} was completed',
  'dealerDash.activityOther': '{actor} moved booking {reference} to {status}',
  'dealerDashboard.nothingDueInWindow': {
    one: 'No requests waiting, nothing due in the next hour.',
    other: 'No requests waiting, nothing due in the next {count} hours.',
  },
  'dealerDashboard.nextHours': {
    one: 'Next {count}h',
    other: 'Next {count}h',
  },
  'dealerDashboard.deliveryToTheCustomer': 'Delivery to the customer',
  'dealerDashboard.vehiclesCount': {
    one: '{count} vehicle',
    other: '{count} vehicles',
  },

  // Wave Two 2026-09-17: the dealer's booking, its decisions, its dispute, the application and the gate
  'dealerBooking.bookingReference': 'Booking {reference}',
  'dealerBooking.fromRatings': {
    one: 'from {count} rating',
    other: 'from {count} ratings',
  },
  'dealerBooking.completedWithYou': '({rentals} with you)',
  'renterDocs.uploadedWhen': 'Uploaded {when}',
  'renterDocs.reviewedByReviewer': 'Reviewed by {reviewer}',
  'dealerBooking.allAmountsFrozen': 'All amounts in {currency} · frozen on this booking',
  'dealerDispute.evidenceAttached': 'Attached: {files}',
  'dealerBooking.odometerAndFuel': '{km} km · fuel {fuel}',
  'dealerBooking.cashCollectedAmount': 'Cash collected: {amount}',
  // The free-cancellation window starts when a payment confirms the booking (Booking.ConfirmPayment),
  // never at approval.
  'freeCancellation.withinHoursOfPayment': {
    one: 'Within {count} hour after payment',
    other: 'Within {count} hours after payment',
  },
  'freeCancellation.withinMinutesOfPayment': {
    one: 'Within {count} minute after payment',
    other: 'Within {count} minutes after payment',
  },
  'dealerBooking.hoursAfterReturn': {
    one: '{count}h after return',
    other: '{count}h after return',
  },
  'dealerBooking.approvedOrRejected': 'Approved / rejected',
  'dealerBooking.yourAnswerBefore': 'Your answer, before {when}',
  'dealerBooking.customerPaysBy': 'The customer pays by {when}',
  'dealerBooking.scheduledFor': 'Scheduled {when}',
  'dealerBooking.afterSettlementWindow': {
    one: 'After the {count}h settlement window',
    other: 'After the {count}h settlement window',
  },
  'dealerBooking.platformWillAnswerWithin': {
    one: 'The platform will answer within {count} hour.',
    other: 'The platform will answer within {count} hours.',
  },
  'dealerDecide.cashLabelIn': 'Cash collected ({currency})',
  'dealerDispute.disputeOnReference': 'Dispute on {reference}',
  'dealerDispute.photosOrAPdfStored': 'Photos or a PDF, stored privately.',
  'dealerDispute.addStatement': 'Add statement',
  'dealerApply.withThePlatformForLicenceCheck':
    '{name} is with the platform for its licence check.',
  'dealerApply.attachedFile': 'Attached: {name}',
  'dealerApply.stillNeededDocuments': 'Still needed: {documents}.',
  'dealerApply.submitting': 'Submitting…',
  'dealerApply.submitForReview': 'Submit for review',

  // Wave Two 2026-09-17: dealer staff, dealer page, delivery, settings; employee business and settings
  'dealerEmployees.availableOnceTrading': 'Available once your dealership is approved and trading',
  'dealerEmployees.addedOn': 'Added {date}',
  'dealerEmployees.sinceDate': 'Since {date}',
  'dealerEmployees.canSeeReports': 'Can see reports',
  'dealerEmployees.bookingsOnly': 'Bookings only',
  'dealerEmployees.freshLinkOnItsWay': 'A fresh link is on its way to {email}.',
  'dealerEmployees.canNowSeeReports': '{name} can now see revenue and reports.',
  'dealerEmployees.reportsNowHidden': '{name} can still handle bookings; reports are hidden.',
  'dealerEmployees.deactivateName': 'Deactivate {name}?',
  'dealerEmployees.noLongerHasAccess': '{name} no longer has access.',
  'dealerEmployees.stillNeedsToAccept':
    '{name} still needs to accept their invitation and set a password.',
  'dealerEmployees.canSignInAgain': '{name} can sign in again with their existing password.',
  'dealerProfile.saveChanges': 'Save changes',
  'dealerProfile.uploading': 'Uploading…',
  'dealerProfile.change': 'Change',
  'dealerProfile.open': 'Open',
  'dealerProfile.closed': 'Closed',
  'dealerProfile.yourDealershipLocation': 'Your dealership location',
  'dealerProfile.noReviewsDeliversUpTo': 'No reviews yet · Delivers up to {km} km',
  'dealerProfile.noReviewsPickupOnly': 'No reviews yet · Pickup only',
  'dealerProfile.noDescriptionYet': 'No description yet.',
  'dealerProfile.listed': 'Listed',
  'dealerProfile.notListed': 'Not listed',
  'dealerProfile.platformNoteQuoted': 'Platform note: “{note}”',
  'dealerProfile.everyDayHours': 'Every day · {opens}–{closes}',
  'dealerProfile.daysAWeekHours': {
    one: '{count} day a week · {opens}–{closes}',
    other: '{count} days a week · {opens}–{closes}',
  },
  'dealerProfile.openDaysAWeekHoursVary': {
    one: 'Open {count} day a week · hours vary',
    other: 'Open {count} days a week · hours vary',
  },
  'dealerDelivery.fromYourLocationUpToKm': 'From your dealer location · up to {km} km',
  'dealerDelivery.distanceKm': '{km} km',
  'dealerDelivery.enterRadiusUpToKm': 'Enter a radius between 0 and {max} km.',
  'dealerDelivery.whatYouChargePerDelivery': 'What you charge per delivery ({currency})',
  'dealerDelivery.enterFeeUpTo': 'Enter what you charge, between 0 and {max}.',
  'employeeSettings.changingPassword': 'Changing…',
  'employeeSettings.passwordUnchanged':
    'That is the password you already have. Choose a different one.',
  'employeeSettings.passwordRulesNotMet': 'The new password does not meet the password rules.',
  'employeeSettings.security': 'Security',
  'employeeSettings.notifications': 'Notifications',
  'employeeSettings.employeeOfBusiness': 'Employee of {business}',
  'employeeBusiness.detailsCouldNotBeLoaded':
    "Your dealership's details could not be loaded. Nothing has been changed.",
  'employeeBusiness.radiusAndFee': '{km} km · {fee}',
  'employeeBusiness.opensToCloses': '{opens} – {closes}',
  'employeeBusiness.trading': 'Trading',
  'employeeBusiness.activeStaffDealerSinceDate': {
    one: '{count} active staff member · dealer since {date}',
    other: '{count} active staff · dealer since {date}',
  },

  // Wave Two 2026-09-17: the fleet list and the car page
  'fleetList.showingCars': {
    one: '{shown} of {count} car',
    other: '{shown} of {count} cars',
  },
  'fleetList.seatCount': {
    one: '{count} seat',
    other: '{count} seats',
  },
  'fleetList.transmissionAutomatic': 'Automatic',
  'fleetList.transmissionManual': 'Manual',
  'fleetList.fuelPetrol': 'Petrol',
  'fleetList.fuelDiesel': 'Diesel',
  'fleetList.fuelHybrid': 'Hybrid',
  'fleetList.fuelElectric': 'Electric',
  'fleetList.securityDeposit': 'Deposit',
  'fleetList.takeVehicleOffTheRoad': 'Take {vehicle} off the road?',
  'fleetList.vehicleNotBeingOffered': '{vehicle} is not being offered.',
  'fleetList.removeVehicle': 'Remove {vehicle}?',
  'fleetList.vehicleNoLongerListed': '{vehicle} is no longer listed.',

  // Wave Two 2026-09-17: admin dashboard, dealers, customers, admin users, security
  'adminDashboard.bookingsInTheLastDays': {
    one: 'Bookings · last {count} day',
    other: 'Bookings · last {count} days',
  },
  'adminDashboard.bookingsTrendHeading': 'Bookings',
  'adminDashboard.trendRange': '{from} – {to}',
  'adminDashboard.queueOpenCount': {
    one: '{count} open',
    other: '{count} open',
  },
  'adminDashboard.queueOverdueCount': {
    one: '{count} overdue',
    other: '{count} overdue',
  },
  'adminDashboard.currentSlaHours': {
    one: 'Current SLA {count}h',
    other: 'Current SLA {count}h',
  },
  'dealersList.showingRange': {
    one: 'Showing {count} of {count} dealer',
    other: 'Showing {from}–{to} of {count} dealers',
  },
  'dealersList.commercialRegistration': 'CR {number}',
  'dealersList.documentsOfRequired': '{have} of {need}',
  'dealersList.noReviewsYet': 'No reviews yet',
  'dealersList.reviewCount': {
    one: '{count} review',
    other: '{count} reviews',
  },
  'dealerReview.documentsOfCount': {
    one: '{have} of {count} document',
    other: '{have} of {count} documents',
  },
  'dealerReview.submittedOn': 'submitted {date}',
  'dealerReview.provided': 'Provided',
  'dealerReview.linkExpiresAt': 'Link expires {time}',
  'dealerReview.signedLinksExpireAt': 'Signed URLs · expire at {time}',
  'dealerReview.signedLinksExpired': 'Signed URLs · expired — reload the page',
  'dealerReview.documentCommercialRegistration': 'Commercial registration',
  'dealerReview.documentVehicleRegistration': 'Vehicle registration',
  'dealerReview.documentOwnerIdentity': 'Owner identity',
  'dealerReview.missingDocumentsRequired':
    'Missing: {documents}. Every one of these is required before this application can be approved.',
  'dealerReview.approveNameQuestion': 'Approve {name}?',
  'dealerReview.nameCanNowTrade': '{name} can now trade.',
  'dealerReview.rejectNameQuestion': 'Reject {name}?',
  'dealerReview.nameWasToldWhy': '{name} was told why.',
  'dealerReview.suspendNameQuestion': 'Suspend {name}?',
  'dealerReview.nameCanNoLongerTrade': '{name} can no longer trade.',
  'dealerReview.reactivateNameQuestion': 'Reactivate {name}?',
  'dealerReview.nameCanTradeAgain': '{name} can trade again.',
  'dealerReview.decision': 'Decision',
  'dealerReview.decisionOnRecord': 'Decision on record',
  'customersList.showingRange': {
    one: 'Showing {count} of {count} customer',
    other: 'Showing {from}–{to} of {count} customers',
  },
  'customersList.joinedOn': 'Joined {date}',
  'customersList.documentsOnFile': {
    one: '{count} on file',
    other: '{count} on file',
  },
  'customersList.completeToRent': 'Complete to rent',
  'customersList.incomplete': 'Incomplete',
  'customerProfile.suspendNameQuestion': 'Suspend {name}?',
  'customerProfile.reactivateNameQuestion': 'Reactivate {name}?',
  'customerProfile.sizeInKilobytes': '{size} KB',
  'customerProfile.uploadedOn': 'uploaded {date}',
  'adminUsers.addedOn': 'Added {date}',
  'adminUsers.canAcceptUntil': '{email} can accept until {date}.',
  'adminUsers.deactivateNameQuestion': 'Deactivate {name}?',
  'adminUsers.reactivateNameQuestion': 'Reactivate {name}?',

  // Wave Two 2026-09-17: Admin bookings, disputes, audit log, lookups, platform settings
  'bookingsList.tabUnpaid': 'Unpaid',
  'bookingsList.pageSummary': {
    one: 'Showing {from}–{to} of {count} booking',
    other: 'Showing {from}–{to} of {count} bookings',
  },
  'bookingsList.bookedOn': 'Booked {date}',
  'bookingsList.periodUntil': 'to {end}',
  'adminBooking.dailyRateForDays': {
    one: 'Daily rate × {count} day',
    other: 'Daily rate × {count} days',
  },
  'adminBooking.depositWithPercent': 'Deposit ({percent})',
  'adminBooking.platformCommissionWithPercent': 'Platform commission ({rate})',
  'adminBooking.hours': {
    one: '{count} hour',
    other: '{count} hours',
  },
  'adminBooking.periodRange': '{start} to {end}',
  'adminBooking.againstParty': 'Against {party}',
  'adminBooking.recordedByParty': 'Recorded by {party}',
  'adminBooking.odometerKm': '{km} km',
  'adminBooking.fuelLevel': 'fuel {level}',
  'adminBooking.photoCount': {
    one: '{count} photo',
    other: '{count} photos',
  },
  'adminBooking.cashCollectedAmount': 'Cash collected: {amount}',
  'disputesList.nothingWaitingOnYou': 'Nothing waiting on you',
  'disputesList.nothingInThisView': 'Nothing in this view',
  'disputesList.shownOfTickets': {
    one: '{shown} of {count} ticket',
    other: '{shown} of {count} tickets',
  },
  'disputesList.overdueCount': {
    one: '{count} overdue',
    other: '{count} overdue',
  },
  'disputesList.unassignedCount': {
    one: '{count} unassigned',
    other: '{count} unassigned',
  },
  'disputesList.openedAt': 'Opened {when}',
  'disputesList.statementCount': {
    one: '{count} statement',
    other: '{count} statements',
  },
  'disputesList.view': 'View',
  'disputeDetail.periodRange': '{start} – {end}',
  'disputeDetail.evidenceFromBothParties': {
    one: '{count} file from both parties · links expire',
    other: '{count} files from both parties · links expire',
  },
  'disputeDetail.refundedToCustomerIn': 'Refunded to the customer ({currency})',
  'disputeDetail.keptByPlatformIn': 'Kept by the platform ({currency})',
  'disputeDetail.transferredToDealerIn': 'Transferred to the dealer ({currency})',
  'disputeDetail.chargeToDealerOptionalIn': 'Charge to the dealer ({currency}, optional)',
  'disputeDetail.balancesAgainstTheDepositHeld': 'Balances against the deposit held',
  'disputeDetail.leftToAllocate': 'Left to allocate',
  'disputeDetail.allocatedOfHeld': '{allocated} of {held}',

  // Wave Two 2026-09-18: the audit log's own vocabulary, the security sessions and the employee notifications
  'auditLog.actionDealerApproved': 'Dealer approved',
  'auditLog.actionDealerRejected': 'Dealer rejected',
  'auditLog.actionDealerClarificationRequested': 'Dealer clarification requested',
  'auditLog.actionDealerSuspended': 'Dealer suspended',
  'auditLog.actionDealerReactivated': 'Dealer reactivated',
  'auditLog.actionCustomerSuspended': 'Customer suspended',
  'auditLog.actionCustomerReactivated': 'Customer reactivated',
  'auditLog.actionCustomerDocumentRejected': 'Customer document rejected',
  'auditLog.actionDisputeOpened': 'Dispute opened',
  'auditLog.actionDisputeAssigned': 'Dispute assigned',
  'auditLog.actionDisputeResolved': 'Dispute resolved',
  'auditLog.actionBusinessRuleChanged': 'Business rule changed',
  'auditLog.actionReviewHidden': 'Review hidden',
  'auditLog.actionReviewRestored': 'Review restored',
  'auditLog.actionAdminInvited': 'Admin invited',
  'auditLog.actionAdminInvitationResent': 'Admin invitation resent',
  'auditLog.actionAdminDeactivated': 'Admin deactivated',
  'auditLog.actionAdminReactivated': 'Admin reactivated',
  'auditLog.actionBookingCancelledByAdmin': 'Booking cancelled by admin',
  'auditLog.actionBookingExpired': 'Booking expired',
  'auditLog.actionBookingMarkedNoShow': 'Booking marked no-show',
  'auditLog.actionHandoverVerified': 'Handover verified by code',
  'auditLog.actionHandoverUnverified': 'Handover recorded unverified',
  'auditLog.actionHandoverCodeLocked': 'Handover code locked (wrong tries)',
  'auditLog.actionFinancialDocumentVoided': 'Financial document voided',
  'auditLog.actionFinancialDocumentEmailRequested': 'Receipt email requested',
  'auditLog.actionOfficeSettlementRecorded': 'Office settlement recorded',
  'auditLog.actionOfficeSettlementVoided': 'Office settlement voided',
  'auditLog.actionOfficePayableHeld': 'Office payable held',
  'auditLog.actionOfficePayableReleased': 'Office payable released',
  'auditLog.actionLookupCreated': 'Lookup created',
  'auditLog.actionLookupRenamed': 'Lookup renamed',
  'auditLog.actionLookupRetired': 'Lookup retired',
  'auditLog.actionLookupRestored': 'Lookup restored',
  'auditLog.entityDealer': 'Dealer',
  'auditLog.entityCustomer': 'Customer',
  'auditLog.entityBooking': 'Booking',
  'auditLog.entityDispute': 'Dispute',
  'auditLog.entityReview': 'Review',
  'auditLog.entitySetting': 'Setting',
  'auditLog.entityAdminUser': 'Admin user',
  'auditLog.entityCity': 'City',
  'auditLog.entityCarType': 'Car type',
  'auditLog.entityFinancialDocument': 'Financial document',
  'auditLog.entityOfficeSettlement': 'Office settlement',
  'auditLog.entityOfficePayable': 'Office payable',
  // The Record cell for the two kinds whose stored label was an English sentence, worded from facts.
  'auditLog.subjectDispute': 'Dispute on {reference}',
  'auditLog.subjectCustomer': 'Customer {reference}',
  'auditLog.pageSummary': {
    one: 'Showing {from}–{to} of {count} entry',
    other: 'Showing {from}–{to} of {count} entries',
  },
  'auditLog.timesAndDatesIn': 'Times and dates in {zone}',
  'auditLog.nothingMatchesThoseFilters': 'Nothing matches those filters',
  'auditLog.noActionsRecordedYet': 'No actions have been recorded yet',
  'auditLog.automatedActor': 'Automated',
  'auditLog.automatedActorOption': '{name} — automated ({count})',
  'auditLog.noReasonRecordedWithAction': 'No reason was recorded with this action.',
  'security.activeSessionsCount': {
    one: '{count} active session',
    other: '{count} active sessions',
  },
  'security.sessionActive': 'Active',
  'security.ended': 'Ended',
  'security.signedInCannotBeRefreshed': 'Signed in {date}. It cannot be refreshed after this.',
  'security.signedInFromCannotBeRefreshed':
    'Signed in {date} from {address}. It cannot be refreshed after this.',
  'security.inFlightForUpToMinutes': {
    one: 'A session already in flight can keep working for up to {count} minute before it has to refresh. If this is the session you are using now, you will be signed out.',
    other:
      'A session already in flight can keep working for up to {count} minutes before it has to refresh. If this is the session you are using now, you will be signed out.',
  },
  'employeeNotifications.unreadSummary': {
    one: '{count} unread · what happened at your dealership',
    other: '{count} unread · what happened at your dealership',
  },
  'employeeNotifications.nothingUnread': 'Nothing unread',
  'employeeNotifications.marking': 'Marking…',
  'employeeNotifications.markAllRead': 'Mark all read',
  'employeeNotifications.showingOf': {
    one: 'Showing {shown} of {count} notification.',
    other: 'Showing {shown} of {count} notifications.',
  },
  'employeeNotif.team': 'Team',
  'employeeNotif.booking': 'Booking',
  'employeeNotif.dispute': 'Dispute',
  'employeeNotif.payout': 'Payout',
  'employeeNotif.dealership': 'Dealership',
  'employeeNotif.notificationsMarkedRead': {
    one: '{count} notification marked read.',
    other: '{count} notifications marked read.',
  },

  // Wave Two 2026-09-18: the twelve system penalty reasons, worded from their stable codes
  'penaltyReason.paymentWindowLapsed': 'The deposit was not paid within the payment window.',
  'penaltyReason.dealerAnswerWindowLapsed': 'The dealer did not answer within the agreed window.',
  'penaltyReason.dealerRejected': 'The dealer rejected the request.',
  'penaltyReason.dealerDidNotHandOver':
    'The dealer did not hand over the vehicle after approving the booking.',
  'penaltyReason.customerNoShow':
    'The customer did not collect the vehicle within the no-show window.',
  'penaltyReason.deliveryNoShowUndetermined':
    'The vehicle was never handed over on a delivery booking; responsibility is undetermined.',
  'penaltyReason.cancelledBeforeDeposit': 'Cancelled before the deposit was paid.',
  'penaltyReason.cancelledInFreeWindow': 'Cancelled inside the free cancellation window.',
  'penaltyReason.customerCancelledAfterFreeWindow':
    'The customer cancelled after the free cancellation window.',
  'penaltyReason.dealerCancelledAfterFreeWindow':
    'The dealer cancelled after the free cancellation window.',
  'penaltyReason.cancelledByPlatform': 'Cancelled by the platform.',
  'penaltyReason.notCancellable': 'This booking can no longer be cancelled.',

  // Wave Two 2026-09-17: lookups (cities & car types) and platform settings
  'lookups.citiesRegions': 'Cities & regions',
  'lookups.addACity': 'Add a city',
  'lookups.addTheFirstCity': 'Add the first city',
  'lookups.addTheFirstCarType': 'Add the first car type',
  'lookups.exampleCityEnglish': 'e.g. Amman',
  'lookups.exampleCityArabic': 'e.g. عمّان',
  'lookups.exampleCarTypeEnglish': 'e.g. Sedan',
  'lookups.exampleCarTypeArabic': 'e.g. سيدان',
  'lookups.optionalExampleLatitude': 'Optional, e.g. 31.9539',
  'lookups.optionalExampleLongitude': 'Optional, e.g. 35.9106',
  'lookups.renameEntry': 'Rename {name}',
  'lookups.restoreEntry': 'Restore {name}?',
  'lookups.retireEntry': 'Retire {name}?',
  'lookups.restored': 'Restored',
  'lookups.retired': 'Retired',
  'lookups.notPinned': 'Not pinned',
  'lookups.stateOffered': 'Offered',
  'lookups.stateRetired': 'Retired',
  'lookups.entriesInTheList': {
    one: '{count} in the list',
    other: '{count} in the list',
  },
  'lookups.offeredCount': {
    one: '{count} offered on new listings',
    other: '{count} offered on new listings',
  },
  'settings.percentOfTheRentalTotal': '{percent} of the rental total',
  'settings.percentOfTheDeposit': '{percent} of the deposit',
  'settings.percentOfTheRental': '{percent} of the rental',
  'settings.minutes': {
    one: '{count} minute',
    other: '{count} minutes',
  },

  // Wave Two 2026-09-17: the car page, the add-a-car wizard and the car form
  'vehicleDetail.tabOverview': 'Overview',
  'vehicleDetail.tabAvailability': 'Availability',
  'vehicleDetail.tabActivity': 'Activity',
  'vehicleDetail.free': 'Free',
  'vehicleDetail.booked': 'Booked',
  'vehicleDetail.requested': 'Requested',
  'vehicleDetail.notOffered': 'Not offered',
  'vehicleDetail.makeModel': 'Make / model',
  'vehicleDetail.dateAdded': 'Date added',
  'vehicleDetail.plateFact': 'Plate {plate}',
  'vehicleDetail.addedFact': 'Added {date}',
  'vehicleDetail.deliveryEligibleRadiusFee': 'Eligible · radius {radius} km · your fee {fee}',
  'vehicleDetail.periodRange': '{start} – {end}',
  'vehicleDetail.completedCount': {
    one: '{count} completed',
    other: '{count} completed',
  },
  'vehicleDetail.upcomingCount': {
    one: '{count} upcoming',
    other: '{count} upcoming',
  },
  'vehicleDetail.currentRental': 'Current rental',
  'vehicleDetail.nextRental': 'Next rental',
  'vehicleDetail.currentRentalDelivered': '{customer}, {period}, delivered.',
  'vehicleDetail.currentRentalCollected': '{customer}, {period}, collected.',
  'vehicleDetail.nextRentalDelivery': '{customer}, {period}, delivery.',
  'vehicleDetail.nextRentalPickup': '{customer}, {period}, pickup.',
  'vehicleDetail.statusByActor': '{status} · {actor}',
  'vehicleDetail.photoNumber': 'Photo {number}',
  'vehicleDetail.noPhotosYet': 'No photos yet',
  'vehicleDetail.noPhotosYetAddOne': 'No photos yet — add one before publishing',
  'vehicleDetail.availabilityInMonth': 'Availability · {month}',
  'vehicleDetail.backOnTheRoadDone': 'Back on the road',
  'vehicleWizard.specifications': 'Specifications',
  'vehicleWizard.pricing': 'Pricing',
  'vehicleWizard.availability': 'Availability',
  'vehicleWizard.review': 'Review',
  'vehicleWizard.stepOfTotal': 'Step {current} of {total}',
  'vehicleWizard.perDayAndDeposit': 'per day · {deposit} deposit',
  'vehicleWizard.eligible': 'Eligible',
  'vehicleWizard.eligibleYourFee': 'Eligible · your fee {fee}',
  'vehicleWizard.photosUploaded': {
    one: '{count} uploaded',
    other: '{count} uploaded',
  },
  'vehicleWizard.plateIsOnYourDraft':
    '{plate} is on a draft you already started ({vehicle}). Continue that draft instead of creating another.',
  'vehicleWizard.plateIsOnYourCar': '{plate} is already on {vehicle} in your fleet.',
  'vehicleWizard.vehicleIsLive': '{vehicle} is live in your fleet.',
  'vehicleWizard.vehicleIsADraft': '{vehicle} is in your fleet as a draft.',
  'vehicleWizard.vehicleStaysADraft':
    '{vehicle} stays in your fleet as a draft. Open it from the fleet to finish.',
  'vehicleWizard.pickupFromYourLocation':
    "Every car is collected from your dealership's location. To move it, change the location on your {profile}; a per-car pickup point is not offered.",
  'vehicleWizard.pickupLocationOf': '{business} — pickup location',
  'vehicleWizard.customersWithinRadius':
    'Customers within your {radius} km radius can ask for this car to be delivered.',
  'vehicleWizard.addPhoto': 'Add photo',
  'vehicleWizard.uploading': 'Uploading…',
  'vehicleWizard.continue': 'Continue',
  'vehicleWizard.saveDraft': 'Save draft',
  'vehicleWizard.saveAndPublish': 'Save & publish',
  'vehicleWizard.savingPublishes':
    'Saving publishes the vehicle to customer search and makes it bookable straight away.',
  'vehicleWizard.savingKeepsADraft':
    'Saving keeps the vehicle as a draft. Publish it from its page when it is ready.',
  'carForm.editCar': 'Edit car',
  'carForm.saveChanges': 'Save changes',
  'carForm.savedAsStatus': '{vehicle} is saved as {status}.',
  'carForm.photoFormats': 'JPEG, PNG or WebP.',

  // Wave Two 2026-09-18: the advisor's review — a note that is gone, not absent
  'dealerReview.noteNoLongerOnRecord': 'The note is no longer on record.',

  // 2026-09-21: the sandbox payment provider. Shown only when the API itself reports
  // payments.mode = Sandbox, which a Production API can never do — it refuses to start on that
  // provider. Both consoles show it, because a dealer preparing a car and an administrator reading
  // a figure are equally entitled to know the money behind it is not real.
  // What a commission percent is a percent OF (owner, 2026-09-25): printed wherever the rate is,
  // so "20%" can never be read as a share of the whole rental. See core/i18n/commission-rate.ts.
  'commission.percentOfOneDailyRate': '{percent} of one daily rate',
  'commission.percentOfRentalTotal': '{percent} of the rental total',

  // Item 169 (owner, 2026-09-26): a later dispute on a booking splits only what earlier ones left.
  // Every figure is the server's; the console chooses the sentence, never the amount.
  'common.decidedByEarlierDisputes': 'Decided by earlier disputes',
  'common.earlierDisputeDecidedPart':
    'An earlier dispute on this booking already decided {decided} of its {onBooking} deposit, so this one can decide only what is left: {held}.',
  'common.earlierDisputeDecidedAll':
    'An earlier dispute on this booking already decided its whole {onBooking} deposit, so this one has nothing left to split.',
  'disputeDetail.chargeOutsideRange':
    'The charge to the office must stay inside the penalty range this booking assessed, counting what earlier disputes on it already charged.',
  'disputeDetail.depositOverAllocated':
    'Earlier decisions on this booking allocated more than its deposit. Nothing can be split until that is corrected.',

// ── Payments Phase 4b: money on the consoles ──────────────────────────────────────────────────────
  // Server enums, each under its own family (enumKey): a member this build does not know is spelled out.
  'paymentStatus.initiated': 'Started',
  'paymentStatus.pending': 'Awaiting the provider',
  'paymentStatus.failed': 'Failed — nothing charged',
  'paymentStatus.applied': 'Applied to the booking',
  'paymentStatus.orphaned': 'Not applied — being refunded in full',
  'paymentPurpose.deposit': 'Deposit',
  'paymentPurpose.fullPayment': 'Full payment',
  'paymentPurpose.remainingBalance': 'Remaining balance',
  'refundStatus.requested': 'Recorded',
  'refundStatus.sent': 'Sent',
  'refundStatus.settled': 'Refunded',
  'refundStatus.failed': 'Refused — being sent again',
  'refundProgress.none': 'No refund',
  'refundProgress.inProgress': 'Refund in progress',
  'refundProgress.delayed': 'Refund delayed',
  'refundProgress.partial': 'Partly refunded',
  'refundProgress.complete': 'Refunded in full',
  'commissionState.projected': 'If the booking is paid',
  'commissionState.expected': 'Expected',
  'commissionState.earned': 'Earned',
  'commissionState.notEarned': 'Not earned',
  'commissionState.undecided': 'Not decided yet',
  'commissionState.notApplicable': 'None',
  'providerEventOutcome.acted': 'Acted on',
  'providerEventOutcome.orphaned': 'Capture not applied',
  'providerEventOutcome.unknown': 'No payment found',
  'providerEventOutcome.ignored': 'Ignored',
  'providerEventOutcome.unmatched': 'No refund matched',
  // A capture notice for money the payment had already taken (Wave 4, B1).
  'providerEventOutcome.duplicate': 'Same capture, said again',
  'providerEventOutcome.assumedDuplicate': 'Same amount again — assumed the same capture',
  'providerEventOutcome.amountMismatch': 'Same capture, another amount — incident',
  'providerEventOutcome.secondCapture': 'Another capture — incident',
  'providerEventOutcome.otherAttempt': 'Capture held by another attempt — incident',
  'paymentIncidentKind.secondCapture': 'Second capture',
  'paymentIncidentKind.amountMismatch': 'Amount contradicts the capture',
  'paymentIncidentKind.captureOnAnotherAttempt': 'Capture held by another attempt',
  'financialIssue.confirmingPaymentMissing': 'The payment that confirmed this booking cannot be found',
  'financialIssue.endingRefundMissing': "A refund this booking's ending owes was never recorded",
  'financialIssue.refundAmountUnexpected': 'A refund does not match the rule it was recorded under',
  'financialIssue.refundsConflict': 'Refunds were recorded that cannot both apply',
  'financialIssue.disputeSharesUnbalanced': 'Dispute decisions do not add up to the deposit they split',
  'paymentMode.none': 'Payments are not accepted',
  'paymentMode.sandbox': 'Test payments (sandbox)',
  'paymentMode.live': 'Live payments',

  // A booking's deposit and balance, as both consoles state them from its financial state.
  'money.deposit.notPaid': 'Not paid',
  'money.deposit.held': 'Held until pickup, then counted towards the rental',
  'money.deposit.appliedToRental': 'Counted towards the rental',
  'money.deposit.inSettlementWindow': 'Held until {date} in case a dispute is opened',
  'money.deposit.underDispute': 'Held — a dispute is open',
  'money.deposit.settledWithRental': 'Settled with the rental',
  'money.deposit.returnedWithPayment': 'Returned to the customer with the payment',
  'money.deposit.heldUntilWindowCloses': 'Held until {date}, then returned to the customer unless a dispute is opened',
  'money.deposit.heldForAssessedPenalty': 'Held for a penalty assessed on the customer — a dispute can be opened until {date}',
  'money.deposit.heldUnresolved': 'Held pending settlement — a penalty was assessed on the customer and no dispute was opened',
  'money.deposit.released': 'Returned to the customer when the dispute window closed',
  'money.deposit.decidedByDispute': 'Decided by a dispute',
  'money.balance.notYetDue': 'Nothing is due until the booking is paid',
  'money.balance.paidInFull': 'Nothing to collect — paid in full online',
  'money.balance.notDue': 'Nothing further is due',
  'money.balance.dueAtHandover': 'Balance to collect in cash at handover',
  'money.balance.wasDueAtHandover': 'Balance that was due in cash at handover',
  'money.cashRecordedPickup': 'Cash recorded at pickup',
  'money.cashRecordedReturn': 'Cash recorded at return',
  'money.reviewing': 'Khadra is reviewing the payments on this booking.',
  'money.loadFailed': 'The money on this booking could not be loaded.',
  'money.commission.projected': '{amount} · if the booking is paid',
  'money.commission.expected': '{amount} · expected',
  'money.commission.earned': '{amount} · earned',
  'money.commission.undecided':
    "{amount} · decided when the booking is final, and never more than the office's money on it",
  'money.commission.undecidedOffice':
    '{amount} · decided when the booking is final, and never more than your money on it',
  'money.commission.notEarned': 'Not earned — the payment went back',
  'money.commission.notApplicable': 'None — the booking was never paid',

  // The office's Financial section: its own share of a dispute, and any charge to it.
  'dealerMoney.toYou': 'Dispute decision — to you',
  'dealerMoney.chargedToYou': 'Dispute decision — charged to you',

  // The administrator's Money section.
  'adminMoney.paidOnline': 'Paid online (booking money)',
  'adminMoney.processingFees': 'Card processing fees',
  'adminMoney.chargedOnline': 'Charged to the card',
  'adminMoney.refundDelayed': 'Refund delayed — still owed',
  'adminMoney.toCustomer': 'Dispute decision — to the customer',
  'adminMoney.toOffice': 'Dispute decision — to the office',
  'adminMoney.keptByPlatform': 'Dispute decision — kept by Khadra',
  'adminMoney.chargedToOffice': 'Dispute decision — charged to the office',
  'adminMoney.payments': 'Payments on this booking',
  'adminMoney.noPayments': 'No payment has been attempted on this booking.',
  'adminMoney.needsReview': 'The payment records on this booking contradict one another',
  'adminMoney.charged': '{amount} charged',
  /** An attempt that took no money: the amount its checkout asked for. */
  'adminMoney.requested': '{amount} requested',
  'adminMoney.feeInside': 'fee {amount}',
  'adminMoney.appliedAmount': '{amount} applied',
  'adminMoney.refundSplit': '{booking} booking money · {fee} fee',

  // The Payments screen: every attempt, and the refunds queue.
  'payments.subtitle': 'Every checkout attempt on the platform, and every refund owed back',
  'payments.tabPayments': 'Payments',
  'payments.tabRefunds': 'Refunds',
  'payments.filterStatus': 'Status',
  'payments.filterPurpose': 'Purpose',
  'payments.filterReference': 'Booking reference',
  'payments.filterFrom': 'From',
  'payments.filterTo': 'To',
  'payments.filterAll': 'All',
  'payments.clearFilters': 'Clear filters',
  'payments.colPayment': 'Payment',
  'payments.colBooking': 'Booking',
  'payments.colParties': 'Customer · office',
  'payments.colPurpose': 'Purpose',
  /** Charged or only requested: the status beside it says which. */
  'payments.colAmount': 'Amount',
  'payments.colFee': 'Fee',
  'payments.colStatus': 'Status',
  'payments.colRefunds': 'Refunds',
  'payments.shownOf': {
    one: '{shown} of {count} payment',
    other: '{shown} of {count} payments',
  },
  'payments.empty': 'No payment matches these filters.',
  'payments.emptyAll': 'No payment has been attempted on the platform yet.',
  'payments.loadFailed': 'The payments could not be loaded.',
  'payments.test': 'Test',
  'payments.noBooking': 'Booking no longer resolves',
  'refunds.viewLive': 'Owed now',
  'refunds.viewFailed': 'Refused',
  'refunds.viewRequested': 'Recorded',
  'refunds.viewSent': 'Sent',
  'refunds.viewSettled': 'Refunded',
  'refunds.colRefund': 'Refund',
  'refunds.colAmount': 'Amount',
  'refunds.colOwedSince': 'Owed since',
  'refunds.colRefundedOn': 'Refunded on',
  'refunds.shownOf': {
    one: '{shown} of {count} refund',
    other: '{shown} of {count} refunds',
  },
  'refunds.empty': 'Nothing is owed back right now.',
  'refunds.emptyView': 'No refund in this view.',
  'refunds.loadFailed': 'The refunds could not be loaded.',
  'refunds.providerCode': 'Provider said: {code}',
  // A refused refund waits and is counted (Wave 4, B4); from the server's alert on, a person must look.
  'refunds.refusedTimes': {
    one: 'Refused once',
    other: 'Refused {count} times',
  },
  'refunds.sentAgainAt': 'sent again {when}',
  'refunds.needsALook': 'Needs a look',
  'refunds.note':
    'The payment sweep sends a refused refund again by itself. This queue shows what is still owed, and how long it has waited.',

  // One payment's page.
  'paymentDetail.title': 'Payment {id}',
  'paymentDetail.subtitle': '{purpose} for {reference} · {amount}',
  'paymentDetail.viewBooking': 'View booking',
  'paymentDetail.failedNotice': 'This attempt failed and nothing was charged. Provider code: {code}',
  'paymentDetail.failedNoCode': 'This attempt failed and nothing was charged.',
  'paymentDetail.orphanedNotice': 'This capture could not be applied to the booking ({reason}), so all of it is refunded.',
  'paymentDetail.transaction': 'Transaction',
  'paymentDetail.refunds': 'Refunds',
  'paymentDetail.events': 'Provider events',
  'paymentDetail.noRefunds': 'Nothing has been refunded from this payment.',
  'paymentDetail.noEvents': 'The provider has sent nothing about this payment.',
  'paymentDetail.byReference': 'Matched by reference: it arrived before the payment was saved',
  'paymentDetail.status': 'Status',
  'paymentDetail.purpose': 'Purpose',
  'paymentDetail.charged': 'Charged to the card',
  'paymentDetail.requested': 'Requested',
  'paymentDetail.fee': 'Processing fee',
  'paymentDetail.feeRefundable': '{amount} · refundable',
  'paymentDetail.feeKept': '{amount} · not refundable',
  'paymentDetail.applied': 'Applied to the booking',
  'paymentDetail.refundProgress': 'Refunds',
  'paymentDetail.providerReference': 'Provider reference',
  'paymentDetail.opened': 'Opened',
  'paymentDetail.lastChange': 'Last change',
  'paymentDetail.sandbox': 'Test payment — no real money moved',
  'paymentDetail.bookingPart': 'Booking money',
  'paymentDetail.feePart': 'Processing fee',
  'paymentDetail.recordedAt': 'Recorded {when}',
  'paymentDetail.sentAt': 'Sent {when}',
  'paymentDetail.settledAt': 'Refunded {when}',
  'paymentDetail.failedAt': 'Refused {when}',
  'paymentDetail.openDispute': 'Open the dispute',
  'paymentDetail.loadFailed': 'This payment could not be loaded.',
  'paymentDetail.notFound': 'No payment has this id.',
  'paymentDetail.bookingGone': 'The booking this payment belongs to no longer resolves.',
  // Capture incidents (Wave 4, B1): never refunded by the platform; dealt with at the provider, then marked here.
  'paymentDetail.incidents': 'Capture incidents',
  'paymentDetail.noIncidents': 'No capture on this payment has needed checking.',
  'paymentDetail.incidentOpen': 'Open',
  'paymentDetail.incidentHandled': 'Handled',
  'paymentDetail.incidentDetected': 'Detected {when}',
  'paymentDetail.incidentReported': 'The provider reported {amount}',
  'paymentDetail.incidentAlreadyTaken': 'This payment had already taken {amount}',
  'paymentDetail.incidentAskedFor': 'This attempt asked for {amount}',
  'paymentDetail.incidentHandledBy': 'Marked handled by {name} · {when}',
  'paymentDetail.incidentHandledAt': 'Marked handled · {when}',
  'paymentDetail.incidentOtherAttempt': 'Open the attempt that holds this capture',
  'paymentDetail.captureReference': 'Capture {reference}',
  'paymentDetail.openIncidentsNotice': {
    one: 'A capture on this payment needs checking at the provider: money may have been taken twice, or reported wrongly. Nothing was refunded automatically.',
    other: '{count} captures on this payment need checking at the provider: money may have been taken twice, or reported wrongly. Nothing was refunded automatically.',
  },
  'paymentDetail.markHandled': 'Mark as handled',
  'paymentDetail.handle.title': 'Mark as handled: {kind}',
  'paymentDetail.handle.body':
    'Do this once the money has been dealt with at the provider. Nothing moves here: this records how it was dealt with, in the audit log, against your name.',
  'paymentDetail.handle.note': 'How it was dealt with',
  'paymentDetail.handle.confirm': 'Mark as handled',
  'paymentDetail.handle.done': 'Incident marked handled',

  // Issued financial documents (payments Phase 5b): the Payments screen's third tab, a document's page,
  // the holds, and a booking's documents. Inside a document every word is the stored document's own.
  'financialDocuments.tab': 'Financial documents',
  'financialDocuments.viewAll': 'All documents',
  'financialDocuments.viewHolds': 'On hold',
  'financialDocuments.filterType': 'Type',
  'financialDocuments.filterStanding': 'Standing',
  'financialDocuments.filterNumber': 'Number',
  'financialDocuments.filterIssuedFrom': 'Issued from',
  'financialDocuments.filterIssuedTo': 'Issued to',
  'financialDocuments.colDocument': 'Document',
  'financialDocuments.colStanding': 'Standing',
  'financialDocuments.colVersion': 'Version',
  'financialDocuments.colAmount': 'Amount',
  'financialDocuments.colIssued': 'Issued',
  'financialDocuments.shownOf': {
    one: '{shown} of {count} document',
    other: '{shown} of {count} documents',
  },
  'financialDocuments.empty': 'No documents match these filters.',
  'financialDocuments.emptyAll': 'No document has been issued yet.',
  'financialDocuments.loadFailed': 'The documents could not be loaded.',
  'financialDocuments.holdsNote':
    'A document on hold is owed and not issued. Each is tried again on its own schedule; the reason says what it is waiting for.',
  'financialDocuments.holdsEmpty': 'Nothing is on hold.',
  'financialDocuments.holdsLoadFailed': 'The documents on hold could not be loaded.',
  'financialDocuments.holdsShownOf': {
    one: '{shown} of {count} document on hold',
    other: '{shown} of {count} documents on hold',
  },
  'financialDocuments.colOwed': 'Document owed',
  'financialDocuments.colReason': 'Why it is on hold',
  'financialDocuments.colAttempts': 'Attempts',
  'financialDocuments.colFailures': 'Failures',
  'financialDocuments.colNextAttempt': 'Next attempt',
  'financialDocuments.openBooking': 'Open the booking',
  'financialDocuments.firstFailed': 'First: {when}',
  'financialDocuments.lastFailed': 'Latest: {when}',
  'financialDocuments.pageLoadFailed': 'This document could not be loaded.',
  'financialDocuments.notFound': 'No document has this id.',
  'financialDocuments.voidAction': 'Void and correct',
  'financialDocuments.voidedOn': 'Voided {when} by {by}.',
  'financialDocuments.voidedByUnknown': 'an administrator no longer on the platform',
  'financialDocuments.voidReason': 'Reason:',
  'financialDocuments.replacedBy': 'Replaced by',
  'financialDocuments.newerVersion': 'A newer version exists:',
  'financialDocuments.cannotShow': "This document can't be shown here yet.",
  'financialDocuments.recordedFacts': 'Recorded facts',
  'financialDocuments.recordedFactsHint': 'As stored, for evidence',
  'financialDocuments.noFacts': 'This document recorded no facts apart from its text.',
  'financialDocuments.details': 'Details',
  'financialDocuments.type': 'Type',
  'financialDocuments.cause': 'Issued because of',
  'financialDocuments.occurred': 'When the money moved',
  'financialDocuments.issued': 'Issued',
  'financialDocuments.booking': 'Booking',
  'financialDocuments.office': 'Rental office',
  'financialDocuments.customer': 'Customer',
  'financialDocuments.auditLog': 'Audit log',
  'financialDocuments.family': 'Versions and related documents',
  'financialDocuments.versions': 'Versions',
  'financialDocuments.issuedAgainst': 'Issued against payment receipt',
  'financialDocuments.refundsFromPayment': 'Refunds from this payment',
  'financialDocuments.proof': 'Proof of issue',
  'financialDocuments.provider': 'Payment provider',
  'financialDocuments.contentHash': 'Content hash (SHA-256)',
  'financialDocuments.coversThrough': 'Covers money movements up to',
  'financialDocuments.checkpoint': 'Checkpoint fingerprint',
  // Owner, 2026-09-29: kept in the record and here, and off the customer's page and the PDF.
  'financialDocuments.registrations': 'Commercial registrations (not shown to the customer)',
  // A document's PDFs (payments Phase 6): each drawn once from the stored document, with the proof of its bytes.
  'financialDocuments.pdfs': 'PDFs',
  'financialDocuments.pdfsHint': 'Drawn once from the stored document',
  'financialDocuments.pdfEnglish': 'English',
  'financialDocuments.pdfArabic': 'Arabic',
  'financialDocuments.pdfTitle': '{language} · template {n}',
  // A voided document has two kinds of PDF (owner, 2026-09-29): the original as issued, and its customer's voided copy.
  'financialDocuments.pdfTitleOriginal': '{language} · original as issued, unstamped · template {n}',
  'financialDocuments.pdfTitleVoided': "{language} · voided copy, the customer's · template {n}",
  'financialDocuments.pdfDownloadEn': 'Download PDF (English)',
  'financialDocuments.pdfDownloadAr': 'Download PDF (Arabic)',
  'financialDocuments.pdfDownloadOriginalEn': 'Download the original as issued, unstamped (English)',
  'financialDocuments.pdfDownloadOriginalAr': 'Download the original as issued, unstamped (Arabic)',
  'financialDocuments.pdfDownloadVoidedEn': 'Download the voided copy (English)',
  'financialDocuments.pdfDownloadVoidedAr': 'Download the voided copy (Arabic)',
  'financialDocuments.pdfRendered': 'Drawn',
  'financialDocuments.pdfRenderer': 'Drawn with',
  'financialDocuments.pdfSize': 'Size',
  'financialDocuments.pdfBytes': '{n} bytes',
  'financialDocuments.pdfHash': 'File hash (SHA-256)',
  'financialDocuments.pdfDrawnFrom': 'Drawn from content hash',
  'financialDocuments.pdfPreparing': 'A PDF of this document is still being drawn.',
  'financialDocuments.pdfNone': 'No PDF has been drawn of this document yet.',
  'financialDocuments.pdfVoidedNote':
    'The customer is given the voided copies: the document as issued, stamped VOID on every page and naming its correction. The original as issued stays here, unstamped, for administrators.',
  // Its emails to the customer (payments Phase 7): receipts only, with their PDFs. "Sent" means the mail provider
  // ACCEPTED it — never "delivered" — and nothing here promises when.
  'financialDocuments.emails': 'Emails to the customer',
  'financialDocuments.emailsHint': 'Sent means the mail provider accepted it, not that it reached the inbox',
  'financialDocuments.emailsNone': 'No email of this receipt has been queued.',
  'financialDocuments.emailsNotForStatements': 'Booking statements are not emailed to the customer. Only receipts are.',
  'financialDocuments.emailsVoided': 'Voided, so it is not emailed again. Its correction is the receipt the customer is sent.',
  // Production on Brevo (owner, 2026-09-29): the server sends no receipt email until Brevo's duplicate protection is verified.
  'financialDocuments.emailsSwitchedOff':
    'Receipt emails are switched off on this server: its mail provider, Brevo, has not had its protection against duplicate sends verified. Queued emails wait here, and nothing is sent.',
  'financialDocuments.emailSend': 'Email it to the customer',
  'financialDocuments.emailAgain': 'Email it again',
  'financialDocuments.emailOnItsWay': 'An email of this receipt is already on its way.',
  'financialDocuments.emailAskedAtIssue': 'Queued when the receipt was issued',
  'financialDocuments.emailAskedBy': 'Asked for by {name}',
  'financialDocuments.emailByUnknown': 'an administrator no longer on the platform',
  'financialDocuments.emailQueuedAt': 'Queued',
  'financialDocuments.emailWaiting': 'Waiting for',
  'financialDocuments.emailWaitingSince': '{reason}, since {since}',
  'financialDocuments.emailFinished': 'Finished',
  'financialDocuments.emailTo': 'To',
  'financialDocuments.emailLanguages': 'Written in',
  'financialDocuments.emailSendAttempts': 'Send attempts',
  'financialDocuments.emailLastError': 'Last error',
  'financialDocuments.emailWhyNotSent': 'Why it was not sent',
  'financialDocuments.emailAttempt': 'Attempt {n} · {outcome}',
  'financialDocuments.emailAttemptAt': 'When',
  'financialDocuments.emailProvider': 'Mail provider',
  'financialDocuments.emailMessageId': "Provider's message id",
  'financialDocuments.emailError': 'Error',
  'financialDocuments.emailWhy': 'Why',
  'financialDocuments.emailPdfEn': 'English PDF attached (SHA-256)',
  'financialDocuments.emailPdfAr': 'Arabic PDF attached (SHA-256)',
  'financialDocuments.emailTitle': 'Email {number} to its customer?',
  'financialDocuments.emailBody':
    "It goes to the customer's verified email address with its PDF attached: in the language they chose, or in Arabic and English if they never chose one.",
  'financialDocuments.emailNote': "The request is recorded in the audit log under this receipt's number.",
  'financialDocuments.emailConfirm': 'Queue the email',
  'financialDocuments.emailQueuedTitle': 'Email queued',
  'financialDocuments.emailQueuedBody': '{number} is queued for its customer. This page shows when the mail provider accepts it.',
  'financialDocuments.versionOf': 'Version {n} of {total}',
  'financialDocuments.version': 'Version {n}',
  'financialDocuments.preparingPaymentReceipt': 'Payment receipt — being prepared',
  'financialDocuments.preparingRefundReceipt': 'Refund receipt — being prepared',
  'financialDocuments.preparingBookingStatement': 'Booking statement — being prepared',
  'financialDocuments.preparingOther': 'A document — being prepared',
  'financialDocuments.onHold': '{type} — on hold',
  'financialDocuments.allForBooking': 'All documents for this booking',
  'financialDocuments.bookingLoadFailed': "This booking's documents could not be loaded.",
  'financialDocuments.noneForBooking': 'No document has been issued for this booking.',
  'financialDocuments.paymentReceipts': 'Receipts',
  'financialDocuments.noPaymentReceipt': 'No receipt has been issued for this payment.',
  'financialDocuments.voidTitle': 'Void {number} and issue its correction?',
  'financialDocuments.voidBody':
    "This is permanent. A corrected document is issued at once under a new number, from the booking's records as they stand now. The customer sees this one voided and replaced — never your reason.",
  'financialDocuments.voidNote': 'Your reason is kept with the void and in the audit log.',
  'financialDocuments.voidStatementNote': 'A new booking statement will be issued shortly after the correction.',
  'financialDocuments.voidReasonLabel': 'Why is it being voided?',
  'financialDocuments.voidReasonPlaceholder': 'What is wrong with this document',
  'financialDocuments.voidConfirm': 'Void and issue the correction',
  'financialDocuments.voidedTitle': 'Document voided',
  'financialDocuments.voidedBody': 'Voided {voided}. Issued {replacement}.',

  // A document's standing, type, cause and hold reason — the types and causes in the words the documents
  // themselves use, so the console never names one differently from the record it describes.
  'status.currentFinancialDocument': 'Current version',
  'status.supersededFinancialDocument': 'Earlier version',
  'status.voidedFinancialDocument': 'Voided',
  'financialDocumentType.paymentReceipt': 'Payment receipt',
  'financialDocumentType.refundReceipt': 'Refund receipt',
  'financialDocumentType.bookingStatement': 'Booking statement',
  'financialDocumentCause.paymentCaptured': 'Payment received',
  'financialDocumentCause.refundSettled': 'Refund completed',
  'financialDocumentCause.disputeResolved': 'Dispute decided',
  'financialDocumentCause.bookingEnded': 'Booking ended',
  'financialDocumentCause.cashRecorded': 'Cash recorded by the rental office',
  'financialDocumentCause.correction': 'Correction of a voided document',
  'financialDocumentCause.receiptCorrected': 'Receipt corrected',
  'financialDocumentHoldReason.recordsNeedReview': "The booking's records need review",
  'financialDocumentHoldReason.issuerNotConfigured': "Khadra's legal identity is not configured",
  'financialDocumentHoldReason.snapshotFailed': 'The document could not be composed',
  // A receipt's email (payments Phase 7): where it stands, what a queued one waits for, what an attempt came to.
  'status.queuedFinancialDocumentEmail': 'Queued',
  'status.sentFinancialDocumentEmail': 'Sent',
  'status.skippedFinancialDocumentEmail': 'Skipped',
  'status.failedFinancialDocumentEmail': 'Failed',
  'financialDocumentEmailWait.pdfNotReady': 'Its PDF to be drawn',
  'financialDocumentEmailOutcome.accepted': 'Accepted by the mail provider',
  'financialDocumentEmailOutcome.failed': 'Failed',
  'financialDocumentEmailOutcome.skipped': 'Skipped',

  // The dashboard's money panel.
  'adminDashboard.thisMonth': 'This month · {month}',
  'adminDashboard.appliedToBookings': 'Applied to bookings',
  'adminDashboard.paymentsCount': {
    one: '{count} payment',
    other: '{count} payments',
  },
  'adminDashboard.feesCharged': 'Card processing fees charged',
  'adminDashboard.refundsSettled': 'Refunds settled',
  'adminDashboard.refundsCount': {
    one: '{count} refund',
    other: '{count} refunds',
  },
  'adminDashboard.rightNow': 'Owed back right now',
  'adminDashboard.refundsOnTheirWay': 'Refunds on their way',
  'adminDashboard.refundsRefused': 'Refunds refused — still owed',
  /** A PART of both lines above it (refunds on their way and refused ones), never added to them. */
  'adminDashboard.ofWhichOrphans': 'Included above: captures that could not be applied',
  /** Not money owed back: a count of capture incidents nobody has marked handled (Wave 4, B1). */
  'adminDashboard.captureIncidents': 'Captures to check at the provider',
  'adminDashboard.incidentsCount': {
    one: '{count} incident',
    other: '{count} incidents',
  },
  'adminDashboard.otherCurrency': '{currency}: {settled} settled this month · {outstanding} owed',
  'adminDashboard.moneyLoadFailed': 'The money figures could not be loaded.',
  'adminDashboard.seePayments': 'See payments',
  'adminDashboard.noRevenueHere':
    "Commission is decided per booking, once its outcome is final. What the platform earned is on Finance, and what each office is owed on Payouts.",

  // The work queue's money rows (they have no clock: nobody froze a deadline for money owed back).
  // Refunds refused often enough that a person must look (Wave 4, B4); below that the back-off handles them.
  'queue.refundsRefused': {
    one: '{count} refund refused repeatedly — still owed',
    other: '{count} refunds refused repeatedly — still owed',
  },
  'queue.capturesBeingRefunded': {
    one: '{count} capture being refunded — it could not be applied',
    other: '{count} captures being refunded — they could not be applied',
  },
  'queue.payablesOnHold': {
    one: '{count} office payable held back — somebody needs to look',
    other: '{count} office payables held back — somebody needs to look',
  },
  'queue.documentsOnHold': {
    one: '{count} financial document on hold — owed and not issued',
    other: '{count} financial documents on hold — owed and not issued',
  },
  'queue.documentEmailsNotSent': {
    one: '{count} receipt not emailed — its email failed or has waited too long',
    other: '{count} receipts not emailed — their emails failed or have waited too long',
  },
  'queue.captureIncident': 'A capture needs checking at the provider',
  'queue.needsALook': 'Needs a look',
  'queue.watching': 'Watching',
  'queue.waitingFor': 'Waiting {duration}',

  // ── The office payables ledger (payments Phase 8) ──────────────────────────────────────────────
  'screen.officePayouts': 'Office payouts',
  'screen.officeSettlement': 'Settlement',

  'payableOutcome.rental': 'Rental',
  'payableOutcome.rentalAfterDispute': 'Rental decided by a dispute',
  'payableOutcome.disputeDecided': 'Ended before pickup — decided by a dispute',
  'payableOutcome.penaltyKept': "Customer's penalty kept from the deposit",
  'payableOutcome.depositReleased': 'Ended before pickup — deposit returned',
  'payableOutcome.paymentReturned': 'Whole payment returned',
  'payableState.due': 'Due',
  'payableState.nothingDue': 'Nothing due',
  'payableState.blocked': 'Not due yet',
  'payableState.onHold': 'On hold',
  'payableState.settled': 'Settled',
  'payableState.awaitingRecord': 'Being recorded',
  'payableState.open': 'Not final yet',
  'payableState.notApplicable': 'Nothing paid',
  'payableLineKind.rentalRevenue': 'Paid online for the rental',
  'payableLineKind.disputeShare': "Office's share of the deposit (dispute)",
  'payableLineKind.penaltyKept': "Customer's penalty, kept from the deposit",
  'payableLineKind.commission': "Khadra's commission",
  'payableLineKind.disputeCharge': 'Charge a dispute assessed on the office',
  'settlementDirection.payout': 'Paid to the office',
  'settlementDirection.received': 'Received from the office',
  'settlementDirection.netted': 'Netted — no money moved',
  'payableHoldReason.needsReview': "The booking's records contradict one another",
  'payableHoldReason.penaltyNotWholeDeposit': "The customer's penalty is not the whole deposit",
  'payableHoldReason.contradicted': 'No longer matches its records',
  'payableHoldReason.manual': 'Held by an administrator',
  'payableBlock.refundOutstanding': 'A refund on this booking is not back with the customer yet',
  'payableBlock.disputeLive': 'A dispute on this booking is open',
  'financialIssue.refundWithoutCause': 'A refund on a completed rental that no dispute explains',
  'financialIssue.paidOnlineDisagrees': 'What the booking says was paid online is not what its payment applied',
  'money.deposit.keptAsPenalty': "Kept as the customer's penalty — the dispute window closed with no dispute",

  'payouts.subtitle': 'What Khadra owes each rental office, or is owed by it, booking by booking once each outcome is final.',
  'payouts.byHandNote':
    'Nothing here moves money. Pay or collect outside the platform first, then record the settlement here: it closes every payable due in one currency, netted, under a number, and is audited.',
  'payouts.balancesTitle': 'Balances',
  'payouts.loadFailed': 'Payouts could not be loaded.',
  'payouts.empty': 'No office has a payable yet',
  'payouts.emptyHint': 'A payable is recorded for each paid booking a few minutes after its outcome becomes final.',
  'payouts.colOffice': 'Office',
  'payouts.colDue': 'Due now',
  'payouts.colNotYetDue': 'Not due yet',
  'payouts.colHeldBack': 'Held back',
  'payouts.colWaiting': 'Held back or not due yet',
  'payouts.colLastSettlement': 'Last settlement',
  'payouts.colReason': 'Why',
  'payouts.colSince': 'Since',
  'payouts.colOutcome': 'Outcome',
  'payouts.colLines': 'Worked out as',
  'payouts.colNet': 'Net',
  'payouts.colState': 'State',
  'payouts.colNumber': 'Number',
  'payouts.colDirection': 'Direction',
  'payouts.colAmount': 'Amount',
  'payouts.colPaidOn': 'Paid on',
  'payouts.colReference': 'Payment reference',
  'payouts.colRecorded': 'Recorded',
  'payouts.colSettlement': 'Settlement',
  'payouts.bookingsCount': { one: '{count} booking', other: '{count} bookings' },
  'payouts.neverSettled': 'Never settled',
  'payouts.notRecordedTitle': 'Bookings not recorded yet',
  'payouts.notRecordedNote':
    'These paid bookings are final but cannot be recorded: their records contradict one another, or the customer’s penalty is not the whole deposit. They stay out of every balance until somebody puts them right.',
  'payouts.nothingDue': 'Nothing due',
  'payouts.notYetDue': {
    one: '{count} booking not due yet: {amount}',
    other: '{count} bookings not due yet: {amount}',
  },
  // Held back is not "not due yet" (Wave 4, F56 a): somebody, or the records, stopped it.
  'payouts.heldBack': {
    one: '{count} booking held back: {amount}',
    other: '{count} bookings held back: {amount}',
  },
  'payouts.lastSettlement': '{number} · {direction} · on {day}',
  'payouts.net.toOffice': 'Khadra owes the office {amount}',
  'payouts.net.byOffice': 'The office owes Khadra {amount}',
  'payouts.net.toYou': 'Khadra owes you {amount}',
  'payouts.net.byYou': 'You owe Khadra {amount}',
  'payouts.net.even': 'Nothing either way',
  // What a settlement closed: past tense, because the money has moved (a settled payable, a settlement's lines).
  'payouts.net.owedToOffice': 'Khadra owed the office {amount}',
  'payouts.net.owedByOffice': 'The office owed Khadra {amount}',
  'payouts.net.owedToYou': 'Khadra owed you {amount}',
  'payouts.net.owedByYou': 'You owed Khadra {amount}',
  'payouts.line.less': 'Less {line}',
  'payouts.officeTitle': 'Office payouts',
  'payouts.officeSubtitle': 'Its balance, the bookings behind it, and every settlement recorded.',
  'payouts.officeEmpty': 'Nothing has been recorded for this office yet',
  'payouts.balanceIn': 'Balance in {currency}',
  'payouts.record.action': 'Record settlement',
  'payouts.record.titlePayout': 'Record a payment to the office',
  'payouts.record.titleReceived': 'Record a payment from the office',
  'payouts.record.titleNetted': 'Record that the balance nets to nothing',
  // A count as a labelled figure, so "1 bookings" cannot happen and Arabic needs no case agreement after a preposition.
  'payouts.record.body': '{office}: {balance}. Bookings it covers: {count}. Confirm only once the money has actually moved.',
  'payouts.record.note': 'This closes every payable due now, under a new settlement number, and cannot be edited — only voided.',
  'payouts.record.testNote': 'Test money: the settlement is numbered as a test and moves nothing real.',
  'payouts.record.paidOn': 'Day the money moved',
  'payouts.record.reference': 'Payment reference (optional)',
  'payouts.record.noteLabel': 'Note',
  'payouts.record.confirm': 'Record settlement',
  'payouts.record.done': 'Settlement recorded',
  'payouts.record.doneBody': 'Recorded as {number}.',
  'payouts.record.balanceChanged': 'The balance due changed while you were confirming: it is now {balance}. Nothing was recorded — review it and confirm again.',
  'payouts.payablesTitle': 'Payables',
  'payouts.scopeOpen': 'Open',
  'payouts.scopeNothingDue': 'Nothing due',
  'payouts.scopeSettled': 'Settled',
  'payouts.scopeAll': 'All',
  'payouts.payablesEmpty': 'No payables here',
  'payouts.finalOn': 'Final {when}',
  'payouts.noLines': 'Nothing either way',
  'payouts.settledUnder': 'Settled under {number} on {day}',
  'payouts.hold.action': 'Hold',
  'payouts.hold.title': 'Hold {reference} out of settlements?',
  'payouts.hold.body': 'It stays recorded, and is left out of every settlement until you release it.',
  'payouts.hold.reason': 'Why it is held',
  'payouts.hold.confirm': 'Hold',
  'payouts.hold.done': 'Payable held',
  'payouts.release.action': 'Release',
  'payouts.release.title': 'Release {reference}?',
  'payouts.release.body': 'It is due again, and in the next settlement.',
  'payouts.release.note': 'Note (optional)',
  'payouts.release.confirm': 'Release',
  'payouts.release.done': 'Payable released',
  'payouts.settlementsTitle': 'Settlements',
  'payouts.settlementsEmpty': 'No settlement recorded yet',
  'payouts.voided': 'Voided',
  'payouts.settlementLoadFailed': 'The settlement could not be loaded.',
  'payouts.backToOffice': 'The office',
  'payouts.void.action': 'Void',
  'payouts.void.title': 'Void {number}?',
  'payouts.void.body': {
    one: 'Its {count} payable opens again and is due in the next settlement.',
    other: 'Its {count} payables open again and are due in the next settlement.',
  },
  'payouts.void.note': 'Nothing is deleted: the settlement stays readable, marked void, with your reason.',
  'payouts.void.reason': 'Why it was wrong',
  'payouts.void.confirm': 'Void settlement',
  'payouts.void.done': 'Settlement voided',
  'payouts.void.doneBody': '{number} is void; its payables are due again.',
  'payouts.voidedOn': 'Voided {when}',
  'payouts.settlementTitle': 'What moved',
  'payouts.closedTitle': { one: 'The {count} booking it closed', other: 'The {count} bookings it closed' },
  'payouts.officeCardTitle': 'For the office',
  'payouts.officeNote.open': "Worked out once this booking's outcome is final.",
  'payouts.officeNote.awaitingRecord': 'Final: the payables ledger records it within minutes.',
  'payouts.officeNote.onHold': 'Held back from settlements until somebody looks.',
  'payouts.officeNote.due': 'In the next settlement with this office.',
  'payouts.officeNote.nothingDue': 'Nothing moves for this booking either way.',
  'payouts.officeNote.blocked': 'Not due while a refund or a dispute on this booking is open.',
  'payouts.officeNote.settled': 'Settled.',

  'finance.subtitle': "Khadra's own money, from the payables ledger.",
  'finance.span': 'From {from} to {to}',
  'finance.apply': 'Show',
  'finance.thisMonth': 'This month',
  'finance.loadFailed': 'The finance figures could not be loaded.',
  'finance.attention': '{held} held back and {blocked} not due yet are left out of what is due.',
  'finance.openPayouts': 'Open payouts',
  'finance.group': 'In {currency}',
  'finance.groupTest': '{currency} — test money',
  'finance.commissionEarned': 'Commission earned',
  // Every booking whose outcome became final in the span, commission or not: never "earned on N bookings".
  'finance.payablesRecorded': { one: 'From {count} booking final in this span', other: 'From {count} bookings final in this span' },
  'finance.keptFromDisputes': 'Kept from disputes',
  'finance.keptFromDisputesHint': 'What resolved disputes left with Khadra — not commission.',
  'finance.paidToOffices': 'Paid to offices',
  'finance.receivedFromOffices': 'Received from offices',
  'finance.owedToOffices': 'Owed to offices now',
  'finance.owedByOffices': 'Owed by offices now',
  'finance.owedNowHint': 'Every open payable, due or not yet.',
  'finance.empty': 'No outcome became final in this span',
  'finance.emptyHint': 'Commission is earned booking by booking, once each outcome is final.',
  'finance.reportTitle': 'Commission report',
  'finance.reportEmpty': 'No payable in this span',
  'finance.colOfficeMoney': 'Office money',
  'finance.colCommission': 'Commission',
  'finance.colPayout': 'Office payout',

  'dealerPayouts.subtitle': 'What Khadra owes you, or you owe Khadra, booking by booking once each outcome is final.',
  'dealerPayouts.notGranted': 'Payouts are for the owner, and staff the owner has granted the reports to.',
  'dealerPayouts.howItWorks':
    'Khadra pays what it owes you outside the platform and records each payment here, with its number. A charge a dispute assessed on you is taken from what you are owed.',
  'dealerPayouts.loadFailed': 'Payouts could not be loaded.',
  'dealerPayouts.dueNow': 'Due now',
  'dealerPayouts.empty': 'Nothing recorded yet',
  'dealerPayouts.emptyHint': 'Each paid booking is recorded here a few minutes after its outcome becomes final.',
  'dealerPayouts.bookingsTitle': 'Bookings',

  'dealerMoney.payoutOpen': "Worked out once this booking's outcome is final",
  'dealerMoney.payoutWhere': 'Where it stands',
  'dealerMoney.payoutDue': 'In your next payout',
  'dealerMoney.payoutNotYetDue': 'Not due yet: something on this booking is still open',
  'dealerMoney.payoutAwaiting': 'Being recorded',
  'dealerMoney.payoutSettled': 'Paid under {number} on {day}',
  'dealerReports.seePayouts': 'See payouts',

  'problem.payablesNothingDue': 'Nothing is due to or from this office in that currency now.',
  'problem.payablesBalanceChanged': 'The balance due has changed. Review it and confirm again.',
  'problem.payablesRecordsChanged':
    'A booking in this settlement no longer matches its recorded payable. Nothing was recorded; it will be held for review.',
  'problem.payablesChangedConcurrently': "This office's payables changed while you were recording. Nothing was recorded.",
  'problem.payablesPaidOnInFuture': 'The day the money moved cannot be in the future.',
  'problem.settlementAlreadyVoided': 'That settlement has already been voided.',
  'problem.payableAlreadyHeld': 'That payable is already held.',
  'problem.payableNothingToHold': 'That payable moves no money either way, so there is nothing to hold back.',
  'problem.incidentAlreadyHandled': 'Somebody has already marked this incident handled. The page now shows their account.',
  'problem.incidentNotFound': 'This payment has no such incident.',
  'problem.noteRejected': 'Check the note: it is missing, or longer than the platform allows.',
  'problem.payableNotHeld': 'That payable is not held by an administrator.',
  'problem.payableAlreadySettled': 'That payable is already settled.',
  'problem.financeSpanInvalid': 'Choose a span of up to a year, its end on or after its start.',

  'sandbox.title': 'Test payments',
  'sandbox.body':
    'This platform is running a sandbox payment provider. No card is charged and no money moves, so any booking confirmed here is not a real rental.',

  // ── The resolution form (E2E F34, F35, F36; checklist 16) ────────────────────────────────────────
  // It says before the click what the server would refuse after it, and words every code the resolve
  // path can return. Every figure in these sentences is the server's.
  'disputeDetail.chargeNotAssessedHint':
    'This booking assessed no penalty against the office, so there is nothing to charge it.',
  'disputeDetail.chargeExhaustedHint':
    'Earlier disputes on this booking already charged the office the whole penalty it assessed.',
  'disputeDetail.chargeRangeHint':
    'Separate from the deposit, and only inside the penalty this booking assessed: {range}.',
  'disputeDetail.chargeAfterEarlierHint':
    'Separate from the deposit. Earlier disputes on this booking charged the office {charged}, so this one can charge up to {max}.',
  'disputeDetail.chargeOutOfRangeHint': 'Outside what this booking lets you charge the office.',
  'disputeDetail.notAnAmount': 'Enter an amount, or leave it empty.',
  'disputeDetail.tooManyPlaces': {
    one: 'At most {count} decimal place.',
    other: 'At most {count} decimal places.',
  },
  'disputeDetail.chargeUnassessed':
    'This booking assessed no penalty against the office, so nothing can be charged to it.',
  'disputeDetail.chargeCurrencyMismatch':
    'A charge to the office must be in the currency of the penalty this booking assessed.',
  'disputeDetail.dispositionCurrencyMismatch':
    'Every amount in a decision must be in the currency of the deposit held.',
  'disputeDetail.amountPrecision':
    'One of the amounts has more decimal places than this currency has. Nothing was decided.',
  'disputeDetail.bookingMissing':
    'The booking behind this dispute could not be loaded, so nothing can be decided on it.',
  'disputeDetail.bookingNotReturned':
    'The car has not been returned, so the deposit cannot be decided yet.',
  'disputeDetail.paymentNotLive':
    "The customer's payment can no longer be refunded, so nothing was decided. Reload the ticket.",
  'disputeDetail.refundExceedsCapture':
    "The customer's share is more than is left of their payment to refund, so nothing was decided.",

  // ── What a dispute decision does to the money (Wave 2 C1; E2E F37) ────────────────────────────────
  // Every figure is the server's, from the one office function the payouts ledger uses. The words say
  // what the records will say, never what money will do.
  'disputePreview.title': 'What this decision does to the money',
  'disputePreview.working': 'Working out what this decision does…',
  'disputePreview.waiting': 'Shown once the three amounts add up to the deposit held.',
  // F66: the amounts balance, and a figure beside them (the office charge, a leg's decimals) is refused.
  'disputePreview.waitingForValid': 'Shown once every amount is valid.',
  'disputePreview.statusAfter': 'The booking becomes {status}.',
  'disputePreview.statusStays': 'The booking stays {status}.',
  'disputePreview.refundRequested': 'Refund requested to the customer',
  'disputePreview.keptByKhadra': 'Kept by Khadra from the deposit',
  'disputePreview.officeLines': 'For the office, as the payouts ledger will record it',
  'disputePreview.commissionCapped':
    "The commission frozen on the booking is {frozen}, and it is never more than the office's money on it, here {money}.",
  'disputePreview.earlier': {
    one: 'These figures include the earlier decision on this booking.',
    other: 'These figures include the {count} earlier decisions on this booking.',
  },
  'disputePreview.recordedNotBefore': 'The payouts ledger records it no earlier than {when}.',
  'disputePreview.recordedNextPass': 'The booking is final already, so the payouts ledger records it at its next pass, within minutes.',
  'disputePreview.ledgerHolds':
    'The payouts ledger will hold this booking for review instead of recording it, because its records disagree:',
  'disputePreview.untilWindow': 'If nothing else is decided on this booking before {when}: another dispute may still be opened until then.',
  'disputePreview.untilWindowOffice': 'If nothing else is decided on this booking before {when}: another dispute may still be opened until then.',
  'disputePreview.settledByHand': 'The office is paid only when an administrator settles its payouts, and a payable can be held.',
  'disputePreview.notApplicable': 'Nothing was paid online for this booking, so the payouts ledger records nothing for it.',
  'disputePreview.confirm': 'As the payouts ledger will record it: {net}.',
  'disputePreview.confirmUntil': 'As the payouts ledger will record it: {net}, if nothing else is decided on this booking before {when}.',
  'disputePreview.confirmHeld':
    'The payouts ledger will hold this booking for review instead of recording it, because its records disagree.',
  'officeOutcome.title': 'What this decision comes to for you',
  'officeOutcome.projected': 'Worked out from the decision. Your payouts will show these figures once the booking is recorded there.',
  'officeOutcome.recorded': 'As recorded in your payouts.',
  'officeOutcome.anotherOpen': 'Another dispute on this booking is still open, and its decision can change these figures.',
  'officeOutcome.openPayouts': 'Open your payouts',

  // ── The legal texts (Wave 2 G1) ──────────────────────────────────────────────────────────────────
  // Every version is published from this screen and is permanent. The texts themselves are the
  // administrator's; these are the screen around them.
  'nav.legalDocuments': 'Legal documents',
  'screen.legalDocuments': 'Legal documents',
  'legal.subtitle':
    'The Terms of Service and the Privacy notice that customers and rental offices read. Every version is permanent: a correction is a new version.',
  'legal.terms': 'Terms of Service',
  'legal.privacy': 'Privacy notice',
  'legal.stateCurrent': 'In force',
  'legal.stateSuperseded': 'Replaced',
  'legal.nothingPublished':
    'Nothing published yet. Its public page says so, and no link to it is shown anywhere.',
  'legal.inForceSince': 'Version {label} · in force since {date}',
  'legal.publishedBy': 'Published by {name}',
  'legal.publishNew': 'Publish a new version',
  'legal.publishFirst': 'Publish the first version',
  'legal.view': 'View',
  'legal.history': 'History',
  'legal.versionsTitle': 'Every version',
  'legal.colDocument': 'Document',
  'legal.colVersion': 'Version',
  'legal.colInForceSince': 'In force since',
  'legal.colPublishedBy': 'Published by',
  'legal.noVersions': 'No version of either document has been published.',
  'legal.couldntLoad': "Couldn't load the legal documents",
  'legal.formTitle': 'Publish a new version',
  'legal.document': 'Document',
  'legal.versionLabel': 'Version label',
  'legal.versionLabelHint':
    'How this version is named on the public page. Each label is used once per document.',
  'legal.englishText': 'English text',
  'legal.arabicText': 'Arabic text',
  'legal.markdownHint':
    'Markdown: # headings down to ####, **bold**, *italic*, lists, > quotes, --- lines, and links to https:, mailto: or a page on this site (/en/…). No HTML, images or code.',
  'legal.preview': 'Preview',
  'legal.previewing': 'Rendering…',
  'legal.previewTitle': 'Preview: what the public page will show',
  'legal.previewStale': 'The form changed after this preview. Preview again before publishing.',
  'legal.previewNeeded': 'Preview the texts first: what is published is exactly what was previewed.',
  'legal.replaces': 'Replaces version {label}, in force since {date}.',
  'legal.firstVersion': 'The first version of this document.',
  'legal.publish': 'Publish',
  'legal.confirmTitle': 'Publish version {label} of the {document}?',
  'legal.confirmBody':
    'It is in force from the moment you publish it, on its public page and wherever the platform links to it. It can never be changed or removed: a correction is a new version.',
  'legal.published': 'Published',
  'legal.publishedBody': 'Version {label} of the {document} is now in force.',
  'legal.detailTitle': '{document}, version {label}',
  'legal.sha256': 'SHA-256',
  'legal.sha256Hint':
    'Of each text exactly as published. The same file run through sha256sum gives the same value.',
  'legal.labelInvalid': 'A version label is required. Keep it short, with no line breaks or tabs.',
  'legal.labelTaken': 'This document already has a version with that label.',
  'legal.bodyRequired': 'Both the English and the Arabic text are required.',
  'legal.bodyTooLong': 'One of the texts is longer than the server accepts.',
  'legal.bodyInvalidCharacters':
    'One of the texts contains a control character or a broken character. Paste it again as plain text.',
  'legal.publishConflict':
    'Another version of this document was published at the same moment. Reload the list and try again.',
  'legal.kindUnknown': 'There is no such legal document.',
  'legal.versionNotFound': 'That version was not found.',
  'legal.textUnsupported': 'The {text} cannot be published as it stands. Line {line} {reason}.',
  'legal.textUnsupportedSomewhere': 'One of the texts uses something that cannot be published.',
  'legal.reasonHtml': 'contains HTML, which is never published',
  'legal.reasonImage': 'has an image',
  'legal.reasonLink':
    'has a link that goes somewhere other than https:, mailto: or a page on this site',
  'legal.reasonHeading': 'has a heading deeper than ####',
  'legal.reasonCode': 'has code (a line indented by four spaces counts as code)',
  'legal.reasonNesting': 'nests lists, quotes or emphasis deeper than a page can show',
  'legal.reasonUnsupported': 'uses Markdown that is not published',
  'legal.linksLabel': 'Legal',
  // Consent to the texts in force (Wave 4, W4-8): the checkbox where somebody joins, and the prompt that takes the
  // page while a text awaits a signed-in person's acceptance. The joining words carry their own spaces.
  'consent.agreeLead': 'I have read and accept the ',
  'consent.agreeAnd': ' and the ',
  'consent.required': 'Tick the box to accept the texts in force.',
  'consent.versionChanged':
    'The texts were updated while this page was open. Read the current version and tick the box again.',
  'consent.title': 'Before you continue',
  'consent.badge': 'Your acceptance is needed',
  'consent.body':
    'Khadra has published the texts that govern your use of the platform. Read them, then accept them to continue: until you do, nothing else in the console is available.',
  'consent.version': 'version',
  'consent.agree': 'I have read these texts and accept them.',
  'consent.accept': 'Accept and continue',
  'consent.accepting': 'Recording your acceptance…',
  'consent.signOut': 'Sign out',
  'consent.changed':
    'A newer version came into force while this page was open. Read the texts listed now, then accept again.',
  'consent.failed': 'Your acceptance was not recorded. Try again.',
  'auditLog.actionLegalDocumentPublished': 'Legal document published',
  'auditLog.entityLegalDocument': 'Legal document',
  'activity.legalDocumentPublished': '{actor} published a new version of the {subject}',
  // Capture incidents (Wave 4, B1), labelled by the booking's reference, or by the payment when it has none.
  'auditLog.actionPaymentIncidentHandled': 'Capture incident marked as handled',
  'auditLog.entityPaymentIncident': 'Capture incident',
  'activity.paymentIncidentHandled': '{actor} marked the capture incident on {subject} as handled',
} as const satisfies Record<string, Message>;

export type TranslationKey = keyof typeof EN;
