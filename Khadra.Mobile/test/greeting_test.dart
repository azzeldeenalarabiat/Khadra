import 'package:flutter_test/flutter_test.dart';
import 'package:khadra_mobile/core/format/greeting.dart';

/// How Home addresses a customer by name, and at what hour it says what.
///
/// The name half is the one that matters. Splitting a full name on whitespace
/// looks obviously right and is wrong in Jordan for some of its commonest
/// names: عبد الله, عبد الرحمن, أبو بكر and نور الدين all carry a space, and the
/// first word alone is not a short form of any of them — عبد on its own means
/// servant, so "صباح الخير، عبد" is not a near miss, it is an insult on the
/// front screen.
void main() {
  group('givenName', () {
    test('takes the first word of an ordinary name', () {
      expect(givenName('Layla Odeh'), 'Layla');
      expect(givenName('عزالدين العربيات'), 'عزالدين');
      expect(givenName('Mary Ann Fitzgerald'), 'Mary');
    });

    test('keeps a compound Arabic given name whole', () {
      expect(givenName('عبد الله الخطيب'), 'عبد الله');
      expect(givenName('عبد الرحمن قاسم'), 'عبد الرحمن');
      expect(givenName('عبد الكريم نصار'), 'عبد الكريم');
      expect(givenName('أبو بكر الحسن'), 'أبو بكر');
      expect(givenName('نور الدين سعيد'), 'نور الدين');
      expect(givenName('صلاح الدين أيوب'), 'صلاح الدين');
      expect(givenName('عز الدين العربيات'), 'عز الدين');
    });

    test('keeps the Latin transliterations whole too', () {
      expect(givenName('Abdul Rahman Qasem'), 'Abdul Rahman');
      expect(givenName('Abu Bakr Al Hassan'), 'Abu Bakr');
      expect(givenName('Noor Aldin Saeed'), 'Noor Aldin');
    });

    test('reads through a bare article between the two halves', () {
      // The same failure in Latin script that the helper exists to stop in
      // Arabic. Taking two words here gives "Abd Al", which says less than
      // "Abd" did; and in the third case the article hides the trailing
      // particle, so the name collapses to its first word. Arabic never hits
      // either, because ال is not written as a word of its own.
      expect(givenName('Abd Al Rahman Qasem'), 'Abd Al Rahman');
      expect(givenName('Abed Al Kareem Nassar'), 'Abed Al Kareem');
      expect(givenName('Noor Al Din Saeed'), 'Noor Al Din');
    });

    test('does not swallow a third word that is just a surname', () {
      // "Al" leading a SURNAME is the common case and must stay out of the
      // greeting: two words, not three.
      expect(givenName('Layla Al Masri'), 'Layla');
      expect(givenName('Omar Al Khatib'), 'Omar');
    });

    test('passes a single name through', () {
      expect(givenName('Layla'), 'Layla');
      expect(givenName('  Layla  '), 'Layla');
    });

    test('has nothing to greet when there is no name', () {
      // The screen renders no greeting at all rather than one addressed to an
      // empty string.
      expect(givenName(''), isNull);
      expect(givenName('   '), isNull);
    });
  });

  group('greetingBucket', () {
    test('runs morning, afternoon, evening — and evening into the small hours', () {
      DayPart at(int hour) => greetingBucket(DateTime(2026, 9, 20, hour));

      expect(at(5), DayPart.morning);
      expect(at(11), DayPart.morning);
      expect(at(12), DayPart.afternoon);
      expect(at(16), DayPart.afternoon);
      expect(at(17), DayPart.evening);
      expect(at(23), DayPart.evening);
      // One in the morning is not a morning. Somebody browsing then is having a
      // late evening, and "Good morning" would read as a machine talking.
      expect(at(1), DayPart.evening);
      expect(at(4), DayPart.evening);
    });
  });
}
