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
  'nav.delivery': 'Delivery',
  'nav.reports': 'Reports',
  'nav.activity': 'Activity',
  'nav.myBusiness': 'My Business',

  // Screen titles that are not simply their nav item.
  'screen.dealerApplication': 'Dealer application',
  'screen.bookingDetails': 'Booking details',
  'screen.customerProfile': 'Customer profile',
  'screen.paymentDetails': 'Payment details',
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

  'notBuilt.payments.purpose':
    'Every deposit, balance, refund and penalty the platform has processed.',
  'notBuilt.payments.blocked':
    'The Payments context is not built. It is blocked on owner decisions (provider, the non-delivery penalty tier, the quick-cancellation fee), and no money has ever moved through the platform.',
  'notBuilt.paymentDetail.purpose':
    'One transaction, its provider reference, and the booking it belongs to.',
  'notBuilt.paymentDetail.blocked':
    'The Payments context is not built, so there are no transactions to show.',
  'notBuilt.payouts.purpose':
    'What each dealer is owed after commission, and the runs that paid them.',
  'notBuilt.payouts.blocked':
    'Payouts need the Payments context, which is not built. Commission is computed and shown per booking at the rate each booking froze, but nothing schedules or records a payment to a dealer.',
  'notBuilt.payouts.instead':
    'A dealer already sees their own revenue and commission at frozen rates on their reports screen.',
  'notBuilt.finance.purpose':
    'Platform revenue: commission earned, deposits held, refunds issued, and what is owed out.',
  'notBuilt.finance.blocked':
    'Every figure on this screen would come from the Payments context, which is not built. Showing commission alone would read as money received, and none has been.',
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
  'notLive.notifications.body':
    'A notification feed is not live yet. Today the dashboard already shows everything that needs your attention: pending requests, pickups and returns due, and overdue returns.',
  'notLive.notifications.point1':
    'Booking requests appear on the dashboard the moment the customer pays the deposit.',
  'notLive.notifications.point2': 'Staff invitations and password links go by email.',

  'notLive.notifications.point3': 'Push and SMS alerts will arrive with the customer app.',

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
  'auth.register.err.noResponse': 'The service did not respond. Nothing was created; try again shortly.',
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
    "Decision recorded — no funds moved. Money only moves once the platform's payment module is live.",
  'common.delivery': 'Delivery',
  'common.deposit': 'Deposit',
  'common.depositHeld': 'Deposit held',
  'common.description': 'Description',
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
  'dealerReview.noAddressRecorded': 'No address recorded. The pin below is the location the applicant gave.',
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
    'Assessed, not charged. Money moves only when an administrator resolves a dispute.',
  'adminBooking.cancelBooking': 'Cancel booking',
  'adminBooking.expireBooking': 'Expire booking',
  'adminBooking.frozenWhenTheBooking': 'Frozen when the booking was made',
  'adminBooking.history': 'History',
  'adminBooking.markAsNoShow': 'Mark as no-show',
  'adminBooking.noneOfTheseMoves':
    "None of these moves money. A cancellation by the platform assesses no penalty against either party; the two deadline actions are refused while this booking's own window still has time in it. Every one is attributed to you in the",
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
    'The platform holds these but does not open them here. Identity papers are private (spec 7) and the domain lets only the customer and a dealer with an active request see one; whether an administrator may, and whether admin review is how a document becomes verified, is an owner decision that has not been made.',
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
    'Resolving records the decision. No funds move until the payment module is live.',
  'disputeDetail.separateFromTheDeposit':
    'Separate from the deposit, and only inside the range this booking assessed.',
  'disputeDetail.takeThisOn': 'Take this on',
  'disputeDetail.theDecisionYourNote':
    'The decision, your note, your name and the timestamp are shown to both parties and written to the audit log.',
  'disputeDetail.thisTicketWasWithdrawn':
    'This ticket was withdrawn by the party who opened it, so nothing is charged to anyone and the booking settles as if no dispute had been raised.',
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
  'dealerBooking.commissionIsDeductedFrom':
    'Commission is deducted from the card deposit at the rate frozen when this booking was made. Payout scheduling is not live yet, so no net figure is shown.',
  'dealerBooking.customer': 'Customer',
  'dealerBooking.history': 'History on Khadra',
  'dealerBooking.historyHint': 'What the platform recorded. Not shared by other galleries.',
  'dealerBooking.noHistory': 'No history on Khadra yet. This is their first booking on the platform.',
  'dealerBooking.historyClosed': 'A customer\u2019s history is shown only while this booking is live.',
  'dealerBooking.ratedByGalleries': 'Rated by galleries',
  'dealerBooking.ratingsCount': 'from {count} rating(s)',
  'dealerBooking.notRatedYet': 'Not rated yet',
  'dealerBooking.completedRentals': 'Completed rentals',
  'dealerBooking.withYou': 'with you',
  'dealerBooking.noShows': 'No-shows',
  'dealerBooking.lateCancellations': 'Late cancellations',
  'dealerBooking.disputesAgainst': 'Disputes decided against them',
  'dealerBooking.customerSince': 'On Khadra since',
  'dealerBooking.rateCustomer': 'Rate this customer',
  'dealerBooking.rateCustomerHint':
    'A score only \u2014 there is no comment. Other galleries see the average, never who gave it. Yours stays hidden until the customer rates you or the window closes.',
  'dealerBooking.youRated': 'You rated this customer',
  'dealerBooking.rateSaved': 'Rating saved.',
  'dealerBooking.documentsOnFileAre':
    'Documents on file are checked by the platform, not shown here',
  'dealerBooking.financial': 'Financial',
  'dealerBooking.freeCancellation': 'Free cancellation',
  'dealerBooking.openADispute': 'Open a dispute',
  'dealerBooking.openVehicle': 'Open vehicle',
  'dealerBooking.photosOrAPdf':
    'Photos or a PDF. Stored privately; only the two parties and the platform can open them.',
  'dealerBooking.pickup': 'Pickup',
  'dealerBooking.recordPickup': 'Record pickup',
  'dealerBooking.recordReturn': 'Record return',
  'dealerBooking.rental': 'Rental',
  'dealerBooking.rulesVersion': 'Rules version',
  'dealerBooking.sayWhatHappenedWith':
    'Say what happened, with times. Both parties and the platform will read this.',
  'dealerBooking.settlementWindow': 'Settlement window',
  'dealerBooking.termsFrozenOnThis': 'Terms frozen on this booking',
  'dealerBooking.thePlatformAnswersWithin':
    'The platform answers within its SLA; nothing is charged without a ticket',
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
    'Charged to the customer on every delivery booking you take. It is not part of the card deposit and the platform takes no commission on it — your driver collects it in cash at handover, on top of the rental balance.',
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
  'dealerDelivery.switchedOnBody': 'Customers within {radius} km of your location can ask for delivery.',
  'dealerDelivery.switchedOffTitle': 'Delivery switched off',
  'dealerDelivery.switchedOffBody': 'Customers will collect from your location only.',
  'dealerDelivery.unsavedChanges': 'Unsaved changes',
  'dealerDelivery.whenACustomerDrops':
    'When a customer drops a pin, the platform measures the straight-line distance from your dealer location. Inside the radius, delivery is offered on your cars; outside, pickup only.',
  'dealerDelivery.whetherYouDeliverHow':
    'Whether you deliver, how far, and what you charge for it.',
  'dealerDelivery.yourDeliveryFee': 'Your delivery fee',
  'dealerDelivery.yoursToSetEnter': 'Yours to set. Enter 0 if you deliver free of charge.',

  // Dealer profile.
  'dealerProfile.applyToAll': 'Apply to all',
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
  'dealerApply.clickTheMapTo': 'Click the map where your gallery is, or use your current location. You can drag the pin afterwards to put it on the door.',
  'dealerApply.couldNotFindYou': 'Your device could not work out where you are. Click the map instead.',
  'dealerApply.findingYou': 'Finding you…',
  'dealerApply.locationPermissionRefused': 'This browser was not given permission to share your location. Click the map instead, or allow location for this site and try again.',
  'dealerApply.thisBrowserCannot': 'This browser cannot share your location. Click the map to place the pin.',
  'dealerApply.useMyLocation': 'Use my current location',
  'dealerApply.whereYourGalleryIs': 'Where your gallery is',
  'dealerApply.area': 'Area',
  'dealerApply.street': 'Street',
  'dealerApply.theNeighbourhoodACustomer': 'The neighbourhood a customer would name to a taxi driver.',
  'dealerApply.manyStreetsHaveNo': 'Many streets have no recorded name. Leave it blank if yours does not.',
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
  'dealerReports.notAvailableYet': 'Not available yet',
  'dealerReports.occupancy': 'Occupancy',
  'dealerReports.occupancyByVehicle': 'Occupancy by vehicle',
  'dealerReports.payoutsAreNotLive':
    'Payouts are not live. Commission is deducted from the card deposit; the balance is collected in cash at handover.',
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
  'vehicleWizard.aPerCarPickup': '; a per-car pickup point is not offered.',
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
  'vehicleWizard.everyCarIsCollected':
    "Every car is collected from your dealership's location. To move it, change the location on your",
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
    "A customer's request arriving, a customer cancelling, and reminders as a pickup falls due are not here — nothing in the platform raises them yet. Your dashboard works those out live from your bookings every time you open it.",
  'employeeNotifications.couldntLoadYourNotifications': "Couldn't load your notifications",
  'employeeNotifications.goToTheDashboard': 'Go to the dashboard',
  'employeeNotifications.markRead': 'Mark read',
  'employeeNotifications.nothingYet': 'Nothing yet',
  'employeeNotifications.notifications': 'Notifications',
  'employeeNotifications.open': 'Open',
  'employeeNotifications.unread': 'Unread',
  'employeeNotifications.whenAColleagueAnswers':
    'When a colleague answers a request or hands a car over, when the platform decides something about your dealership, or when your access changes, it appears here.',

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
    "{customer} is notified and the vehicle is held for these dates. The customer's free-cancellation window starts now.",
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
  'dealerDecide.cashLabel': 'Cash collected (JOD)',
  'dealerDecide.notesLabel': 'Notes',
  'dealerDecide.mustBeANumber': '{field} must be a number.',
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
  'activity.dealerApproved': 'approved dealer',
  'activity.dealerRejected': 'rejected dealer',
  'activity.dealerClarification': 'asked for clarification from',
  'activity.dealerSuspended': 'suspended dealer',
  'activity.dealerReactivated': 'reactivated dealer',
  'activity.customerSuspended': 'suspended customer',
  'activity.customerReactivated': 'reactivated customer',
  'activity.disputeOpened': 'opened dispute',
  'activity.disputeAssigned': 'took the dispute',
  'activity.disputeResolved': 'resolved dispute',
  'activity.settingChanged': 'changed setting',
  'activity.reviewHidden': 'hid review',
  'activity.reviewRestored': 'restored review',
  'activity.adminInvited': 'invited admin',
  'activity.adminDeactivated': 'deactivated admin',
  'activity.bookingCancelled': 'cancelled booking',
  'activity.bookingExpired': 'expired booking',
  'activity.bookingNoShow': 'recorded a no-show on',
  'time.justNow': 'Just now',
  'time.minutesAgo': {
    one: '{count} min ago',
    other: '{count} min ago',
  },
  'time.hoursAgo': {
    one: '{count}h ago',
    other: '{count}h ago',
  },
  'time.yesterday': 'Yesterday',
  'time.daysAgo': {
    one: '{count}d ago',
    other: '{count}d ago',
  },

  // Dealer activity feed.
  'dealerActivity.everyChangeOnYour':
    'Every change on your bookings, newest first, with who made it.',
  'dealerActivity.couldntLoadActivity': "Couldn't load activity",
  'dealerActivity.nothingYet': 'Nothing yet',
  'dealerActivity.bookingRequestsApprovalsPickups':
    'Booking requests, approvals, pickups and returns will appear here as they happen.',

  // Second pass: literal runs that sit beside control flow.
  'dealerDashboard.aRequestExpiresWhen':
    'A request expires when its rental date arrives unanswered',
  'dealerDashboard.noRequestsWaitingNothing': 'No requests waiting, nothing due in the next',
  'dealerDashboard.hours': 'hours.',
  'dealerDashboard.upcomingPickups': 'Upcoming pickups',
  'dealerDashboard.noPickupsDueIn': 'No pickups due in this window.',
  'dealerDashboard.upcomingReturns': 'Upcoming returns',
  'dealerDashboard.noReturnsDueIn': 'No returns due in this window.',
  'dealerDashboard.returnedToYourLocation': 'Returned to your location',
  'dealerDashboard.fleetStatus': 'Fleet status',
  'dealerDashboard.vehicles': 'vehicles',
  'dealerDashboard.noVehiclesYet': 'No vehicles yet.',
  'dealerDashboard.addYourFirstCar': 'Add your first car.',
  'dealerDashboard.recentActivity': 'Recent activity',
  'dealerDashboard.viewAll': 'View all',
  'dealerDashboard.bookingDecisionsAndHandovers':
    'Booking decisions and handovers by you and your staff will appear here.',
  'disputesList.noDisputeIsOpen':
    'No dispute is open. A customer or a dealer can open one from a finished booking, within the window that booking froze.',
  'disputesList.noTicketMatchesThis': 'No ticket matches this view right now.',
  'disputesList.tickets': 'tickets',
  'disputesList.overdue': 'overdue',
  'disputesList.unassigned': 'unassigned',
  'disputesList.opened': 'Opened',
  'vehicleWizard.step': 'Step',
  'vehicleWizard.of8': 'of 8 ·',
  'vehicleWizard.of82': 'of 8',
  'vehicleWizard.customersWithinYour': 'Customers within your',
  'vehicleWizard.kmRadiusCanAsk': 'km radius can ask for this car to be delivered.',
  'vehicleWizard.deliveryIsSwitchedOff':
    'Delivery is switched off for your dealership, so this only takes effect once you switch it on from the Delivery page.',
  'vehicleWizard.customersInsideYourDelivery':
    'Customers inside your delivery radius can ask for this car to be delivered.',
  'vehicleWizard.notSet': 'Not set',
  'vehicleWizard.seats': 'seats',
  'vehicleWizard.jod': 'JOD',
  'vehicleWizard.perDay': 'per day ·',
  'vehicleWizard.jodDeposit': 'JOD deposit',
  'dealerBooking.allAmountsIn': 'All amounts in',
  'dealerBooking.frozenOnThisBooking': '· frozen on this booking',
  'dealerBooking.attached': 'Attached:',
  'dealerBooking.kmFuel': 'km · fuel',
  'dealerBooking.cashCollected': 'Cash collected:',
  'dealerBooking.hAfterApproval': 'h after approval',
  'dealerBooking.hAfterReturn': 'h after return',
  'dealerDelivery.deliveryCanBeChanged':
    'Delivery can be changed once your dealership is approved and trading.',
  'dealerDelivery.onlyTheDealerOwner':
    'Only the dealer owner can change delivery settings. You can read them here.',
  'dealerDelivery.fromYourDealerLocation': 'From your dealer location · up to',
  'dealerDelivery.enterARadiusBetween': 'Enter a radius between 0 and',
  'dealerDelivery.whatYouChargePer': 'What you charge per delivery (',
  'dealerDelivery.enterWhatYouCharge': 'Enter what you charge, between 0 and',
  'disputeDetail.opened': 'Opened',
  'disputeDetail.and': 'and',
  'disputeDetail.fileSFromBoth': 'file(s) from both parties · links expire',
  'disputeDetail.refundedToTheCustomer': 'Refunded to the customer (',
  'disputeDetail.keptByThePlatform': 'Kept by the platform (',
  'disputeDetail.transferredToTheDealerOpen': 'Transferred to the dealer (',
  'disputeDetail.chargeToTheDealer': 'Charge to the dealer (',
  'disputeDetail.optional': ', optional)',
  'bookingsList.showingOne': 'Showing one',
  'bookingsList.booked': 'Booked',
  'customersList.couldntLoadTheCustomers': "Couldn't load the customers",
  'customersList.joined': 'Joined',
  'customersList.onFile': 'on file',
  'dealerReview.documentsSubmitted': 'documents · submitted',
  'dealerReview.signedUrls': 'Signed URLs ·',
  'dealerReview.missing': 'Missing:',
  'dealerReview.everyOneOfThese':
    '. Every one of these is required before this application can be approved.',
  'dealerReview.rejectionClarificationAndSuspensio':
    'Rejection, clarification and suspension all require a written reason. Every decision is attributed to you in the audit log, which cannot be edited afterwards.',
  'dealerReview.whoDecidedThisAnd': 'Who decided this and when is in the',
  'dealerReview.whichCannotBeEdited': ', which cannot be edited afterwards.',
  'vehicleDetail.editVehicle': 'Edit vehicle',
  'vehicleDetail.changingThisCarIts':
    "Changing this car — its details, its photos, whether it is listed — is the dealer owner's. Everything about it is here for you to work from, and its bookings are yours to handle.",
  'vehicleDetail.availability': 'Availability ·',
  'adminBooking.booked': 'Booked',
  'adminBooking.against': 'Against',
  'adminBooking.recordedBy': 'Recorded by',
  'adminBooking.fuel': '· fuel',
  'adminBooking.photoS': 'photo(s)',
  'adminBooking.cashCollected': 'Cash collected:',
  'security.active': 'active',
  'security.signedIn': 'Signed in',
  'security.lastUsed': 'Last used',
  'security.endingASessionStops':
    'Ending a session stops it being refreshed. A request already holding a valid token can keep working for up to',
  'security.minutesAfterThat': 'minutes after that.',
  'auditLog.automated': '— automated (',
  'auditLog.noEntryMatchesThis':
    'No entry matches this combination. Widen the range or clear the filters.',
  'auditLog.theLogFillsAs':
    'The log fills as administrators act: approving a dealer, resolving a dispute, changing a platform setting. Each entry is written in the same transaction as the action itself.',
  'dealerActivity.was': '(was',
  'dealerBookings.onceYourVehiclesAre':
    'Once your vehicles are published, customer booking requests land here. Answer a request before its rental date arrives, or it expires.',
  'dealerBookings.noBookingsMatchThis': 'No bookings match this tab right now.',
  'dealerApply.attached': 'Attached:',
  'dealerApply.stillNeeded': 'Still needed:',
  'dealerDispute.disputeOn': 'Dispute on',
  'dealerDispute.opened': 'Opened',
  'dealerDispute.handledBy': '· handled by',
  'dealerEmployees.added': 'Added',
  'dealerEmployees.since': 'Since',
  'dealerProfile.noReviewsYet': 'No reviews yet ·',
  'dealerProfile.platformNote': 'Platform note: “',
  'employeeNotifications.unreadWhatHappenedAt': 'unread · what happened at your dealership',
  'employeeNotifications.whatHappenedAtYour': 'What happened at your dealership',
  'employeeNotifications.showing': 'Showing',
  'fleetList.cars': 'cars',
  'fleetList.showingOfCars': '{shown} of {total} cars',
  'fleetList.seats': 'seats',
  'lookups.dealersPinTheirLocation':
    'Dealers pin their location on a map, and distance is measured from the coordinates, so the platform works without this list. Adding cities gives customers something to filter by.',
  'lookups.everyCarCurrentlyCarries':
    'Every car currently carries a placeholder type. Adding real types here gives dealers something to choose from and customers something to filter by.',
  'lookups.inTheList': 'in the list ·',
  'lookups.offeredOnNewListings': 'offered on new listings',
  'adminUsers.added': 'Added',
  'dealerReports.ammanTime': '· Amman time',
  'employeeBusiness.activeStaffDealerSince': 'active staff · dealer since',
  'employeeSettings.employeeOf': 'Employee of',
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
  'adminDashboard.bookingsLastDays': 'Bookings · last {days} days',
  'adminDashboard.theBookingTrendCould': 'The booking trend could not be loaded.',
  'adminDashboard.noBookingsWereMade': 'No bookings were made in this period.',
  'adminDashboard.busiestDayWith': {
    one: 'busiest day {count} booking',
    other: 'busiest day {count} bookings',
  },
  'adminDashboard.seeTheBookings': 'see the bookings',
  'adminDashboard.moneyInMotion': 'Money in motion',
  'adminDashboard.noMoneyHasMoved': 'No money has moved through the platform',
  'adminDashboard.grossBookingValueCommission':
    'Gross booking value, commission and dealer payouts all come from the Payments context, which is not built. Commission is computed and frozen on every booking at the rate that booking was made under — but nothing has been charged, held or paid out.',
  'adminDashboard.whatThisWillShow': 'What this will show',
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
  'adminBooking.nothingIsRefundedHere':
    'Nothing is refunded here. Money moves only through a dispute resolution, and Payments is not live.',
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
    'Decision recorded — no funds moved. Payments is not live, so nothing is transferred yet.',
  'disputeDetail.resolveDispute': 'Resolve dispute',
  'disputeDetail.disputeResolved': 'Dispute resolved',
  'disputeDetail.decisionRecordedNoFunds2': 'Decision recorded — no funds moved.',
  'dealerDashboard.pendingRequests': 'Pending requests',
  'dealerDashboard.activeRentals': 'Active rentals',
  'dealerDashboard.availableVehicles': 'Available vehicles',
  'dealerDashboard.confirmedNotYetCollected': 'Confirmed, not yet collected',
  'dealerDashboard.awaitingDeposit': 'Awaiting deposit',
  'dealerDashboard.approvedAndUnpaid': 'approved, not paid for yet',
  'dealerDashboard.heldForTheirDates': 'held for their dates',
  'dealerDashboard.revenueThisMonth': 'Revenue · this month',
  'dealerDashboard.occupancyRate': 'Occupancy rate',
  'dealerBooking.theAnswerWindowHasClosed': 'The answer window has closed. This request can only be rejected now.',
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
    'The amicable path: the ticket closes, nothing is charged to anyone, and the booking settles as if no dispute had been raised. You can open a new one while the window is still open.',
  'dealerDispute.withdrawDispute': 'Withdraw dispute',
  'dealerDispute.disputeWithdrawn': 'Dispute withdrawn',
  'dealerDispute.nothingIsChargedTo': 'Nothing is charged to anyone.',
  'dealerSettings.twoFactorAuthentication': 'Two-factor authentication',
  'dealerSettings.notLiveYetSign': 'Not live yet. Sign-in is email and password.',
  'dealerSettings.notLiveYetInvitations':
    'Not live yet. Invitations and password links go by email; everything else is on the dashboard.',
  'dealerSettings.bankDetailsForPayouts': 'Bank details for payouts',
  'dealerSettings.notLiveYetPayouts':
    'Not live yet. Payouts are not built; commission is deducted from the card deposit and the balance is collected in cash.',
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
  'disputesList.underReview': 'Under review',
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
  'dealersList.showing': 'Showing {from}–{to} of {total} dealers',
  'dealersList.showingOne': 'Showing 1 of 1 dealer',
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
  'status.awaitingDeposit': 'Awaiting deposit',
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
  'dealerBooking.depositPaid': 'Deposit paid',
  'dealerBooking.return': 'Return',
  'dealerBooking.disputeOpened': 'Dispute opened',
  'dealerBooking.theVehicle': 'the vehicle',
  'dealerBooking.requestedAwaitingYourAnswer': 'Requested · awaiting your answer',
  'dealerBooking.approvedAwaitingTheDeposit': 'Approved · awaiting the deposit',
  'dealerBooking.depositPaidBookingConfirmed': 'Deposit paid · booking confirmed',
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
  'employeeDash.requestIs': 'request is',
  'employeeDash.requestsAre': 'requests are',
  'employeeDash.bookingRequest': 'Booking request',
  'employeeDash.returnOverdue': 'Return overdue',
  'employeeDash.pickupApproaching': 'Pickup approaching',
  'employeeDash.returnDue': 'Return due',
  'employeeDash.thisAccountIsNot': 'This account is not part of a dealership.',
  'employeeDash.yourDashboardCouldNot':
    'Your dashboard could not be loaded. Nothing has been changed.',
  'employeeDash.handedOver': 'Handed over',
  'employeeDash.tookBack': 'Took back',
  'employeeDash.justNow': 'just now',

  // The dealer dashboard's KPI sub-labels and activity verbs.
  'dealerDash.allWithinTheirDates': 'all within their dates',
  'dealerDash.rentalsReturnedThisMonth': 'rentals returned this month, before commission',
  'dealerDash.notPartOfYour': 'not part of your access',
  'dealerDash.fleetUtilisationLast30': 'fleet utilisation, last 30 days',
  'dealerDash.overdueReturn': 'Overdue return',
  'dealerDash.viewBooking': 'View booking',
  'dealerDash.handedOver': 'handed over',
  'dealerDash.tookBack': 'took back',

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
  'dealerProfile.everyDay': 'Every day',
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
  'dealerReview.expiredReloadThePage': 'expired — reload the page',
  'dealerReview.thatDealerApplicationNo': 'That dealer application no longer exists.',
  'dealerReview.onlyAdministratorsCanReview': 'Only administrators can review dealer applications.',
  'dealerReview.theApplicationCouldNot':
    'The application could not be loaded. Nothing has been changed.',
  'dealerReview.note': 'note',
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
  'employeeNotif.oneNotificationMarkedRead': 'One notification marked read.',

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
  'dealerActivity.requestedAwaitingYourAnswer': 'Requested — awaiting your answer',
  'dealerActivity.approvedAwaitingTheDeposit': 'Approved — awaiting the deposit',
  'dealerActivity.depositPaidBookingConfirmed': 'Deposit paid — booking confirmed',
  'dealerActivity.markedNoShow': 'Marked no-show',
  'dealerActivity.expiredUnanswered': 'Expired unanswered',

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
  'bookingsList.oneParty': 'one party',
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
  'notifications.customerPaid': 'A customer paid the deposit on {what}',
  'notifications.approved': '{who} approved {what}',
  'notifications.rejected': '{who} rejected {what}',
  'notifications.recordedPickup': '{who} recorded the pickup for {what}',
  'notifications.recordedReturn': '{who} recorded the return for {what}',
  'notifications.updated': '{who} updated {what}',
  'notifications.dealerApproved': 'Your dealership was approved',
  'notifications.dealerRejected': 'Your dealership’s application was rejected',
  'notifications.dealerClarification': 'The platform asked for more on your application',
  'notifications.dealerSuspended': 'Your dealership was suspended',
  'notifications.dealerReactivated': 'Your dealership is trading again',
  'notifications.staffReactivated': '{who} reactivated a member of staff',
  'notifications.reportAccessGranted': '{who} gave you access to financial reports',
  'notifications.reportAccessRevoked': '{who} removed your access to financial reports',

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
  'notifications.aRequestExpiresWhen': 'A request expires when its rental date arrives unanswered.',
  'notifications.oldestAndExpiry':
    'Oldest {when}. A request expires when its rental date arrives unanswered.',

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
} as const satisfies Record<string, Message>;

export type TranslationKey = keyof typeof EN;
