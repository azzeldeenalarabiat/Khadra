/// How Home addresses the person reading it.
///
/// Two pure functions, out of the widget and tested, because both of them are
/// the kind of thing that looks obvious and is not.

library;

/// Which part of the day it is, for the greeting on Home.
enum DayPart { morning, afternoon, evening }

/// The buckets, named rather than left as numbers in a build method.
///
/// Evening runs from 17:00 round to 04:59, which is why this is not two
/// comparisons: somebody browsing at one in the morning is having an evening,
/// not a morning. Arabic collapses afternoon and evening into مساء الخير, which
/// is correct — there is no idiomatic "good afternoon" — so the third bucket
/// exists for English and costs Arabic nothing.
///
/// The hour is the PHONE's, deliberately, unlike every date on this platform.
/// Rentals are counted in Amman days because that is where the car is; this is
/// about the moment the reader is in, and a customer comparing prices from
/// Berlin at nine in the evening is not having a Jordanian morning.
DayPart greetingBucket(DateTime localNow) {
  final hour = localNow.hour;
  if (hour >= 5 && hour < 12) return DayPart.morning;
  // Both bounds, not just the upper one: `hour < 17` alone calls one in the
  // morning an afternoon, because the small hours are below every threshold.
  if (hour >= 12 && hour < 17) return DayPart.afternoon;
  return DayPart.evening;
}

/// Compound given names, which a space does not divide.
///
/// عبد الله is one name and "عبد" is not a short form of it — on its own the
/// word means servant, so greeting somebody with it is not merely wrong but
/// rude. The same holds for أبو بكر, نور الدين and their Latin transliterations.
/// Written in their FOLDED form — see [_fold] — so أبو and ابو are one entry.
const _leadingParticles = <String>{
  'عبد', 'ابو', 'ام', 'بن', 'ابن',
  'abd', 'abdul', 'abdel', 'abdal', 'abed', 'abou', 'abu', 'bin', 'ibn', 'umm',
};

/// Second words that belong to the first: عبد **الله**, نور **الدين**.
const _trailingParticles = <String>{
  'الله', 'الدين',
  'allah', 'aldin', 'aldeen', 'eldin', 'eldeen', 'eddin', 'eddine', 'uddin', 'din',
};

/// The name to greet somebody by, taken from the full name the SERVER holds.
///
/// A first name is usually the first whitespace-separated word, and in Jordan it
/// often is not: عبد الله, عبد الرحمن, عبد الكريم, أبو بكر, نور الدين and صلاح
/// الدين are all common, all written with a space, and all ruined by taking the
/// first word alone. So a second word is taken when the first is a particle that
/// cannot stand alone, or when the second is one that cannot begin a name.
///
/// This is a heuristic and it is allowed to be: the platform collects ONE name
/// field, so there is no given name to read instead, and a `GivenName` column
/// would be a schema, a registration form and an edit screen for the sake of a
/// greeting. What it must never do is address somebody as "عبد". Anything it
/// does not recognise falls through to the first word, which is right for the
/// ordinary case.
///
/// Returns null when there is nothing to greet, so the caller renders no
/// greeting rather than one addressed to an empty string.
String? givenName(String fullName) {
  final parts = fullName
      .trim()
      .split(RegExp(r'\s+'))
      .where((part) => part.isNotEmpty)
      .toList();
  if (parts.isEmpty) return null;
  if (parts.length == 1) return parts.first;

  final first = _fold(parts[0]);
  final second = _fold(parts[1]);

  // A bare article between the two halves is TRANSPARENT, and missing that was
  // the same mistake in Latin script that the whole helper exists to stop in
  // Arabic. "Abd Al Rahman Qasem" is three words for one name: taking two gives
  // "Abd Al", which says even less than "Abd" did. "Noor Al Din Saeed" fails the
  // other way round — the article hides الدين from the trailing check, so the
  // name collapses to "Noor". Arabic escapes both because ال is never written
  // as a word of its own.
  final isArticle = second == 'al' || second == 'el';
  if (isArticle && parts.length > 2) {
    final third = _fold(parts[2]);
    if (_leadingParticles.contains(first) || _trailingParticles.contains(third)) {
      return '${parts[0]} ${parts[1]} ${parts[2]}';
    }
  }

  if (_leadingParticles.contains(first) || _trailingParticles.contains(second)) {
    return '${parts[0]} ${parts[1]}';
  }
  return parts.first;
}

/// Lower-cases Latin and strips the Arabic hamza forms that the same name is
/// written with either way — أبو and ابو are one word typed two ways.
String _fold(String word) => word
    .toLowerCase()
    .replaceAll('أ', 'ا') // أ -> ا
    .replaceAll('إ', 'ا') // إ -> ا
    .replaceAll('آ', 'ا') // آ -> ا
    .replaceAll('ّ', '') // shadda
    .replaceAll(RegExp(r'[ً-ِْ]'), ''); // short vowels
