/// The version a build of this app reports, and the ONE rule for ordering two.
///
/// **Semantic Versioning 2.0.0, exactly** — the twin of the API's
/// `Khadra.Application/Common/AppVersion.cs`, and tested against the same file,
/// `docs/contracts/app-version-vectors.json`. The version gate only works if the
/// server and the phone agree about which of two builds is newer, so neither side
/// gets its own idea of the rule.
///
/// `MAJOR.MINOR.PATCH`, no leading zeros; an optional `-prerelease`; an optional
/// `+build`, ignored — the store build number rides there (`1.1.0+2`). Nothing else
/// parses. Never compared as text: as strings `"1.10.0" < "1.9.0"`.
class AppVersion implements Comparable<AppVersion> {
  const AppVersion._(this.major, this.minor, this.patch, this.prerelease);

  final int major;
  final int minor;
  final int patch;

  /// The prerelease identifiers as written, or null for a release.
  final String? prerelease;

  bool get isPrerelease => prerelease != null;

  // The grammar from semver.org, each core field capped at nine digits so it
  // always fits an int. `[0-9]`, never `\d`, and anchored at both ends.
  static final RegExp _grammar = RegExp(
    r'^(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})\.(0|[1-9][0-9]{0,8})'
    r'(?:-((?:0|[1-9][0-9]*|[0-9]*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9]*[a-zA-Z-][0-9a-zA-Z-]*))*))?'
    r'(?:\+[0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*)?$',
  );

  /// Null for anything that is not a semantic version.
  static AppVersion? tryParse(String? text) {
    if (text == null) return null;
    final match = _grammar.firstMatch(text);
    // Dart's `$` matches only at the very end without multiLine, so "1.1.0\n"
    // does not match — the same answer the server gives with `\z`.
    if (match == null) return null;
    return AppVersion._(
      int.parse(match.group(1)!),
      int.parse(match.group(2)!),
      int.parse(match.group(3)!),
      match.group(4),
    );
  }

  @override
  int compareTo(AppVersion other) {
    var order = major.compareTo(other.major);
    if (order != 0) return order;
    order = minor.compareTo(other.minor);
    if (order != 0) return order;
    order = patch.compareTo(other.patch);
    if (order != 0) return order;

    // A release outranks any prerelease of itself.
    final mine = prerelease;
    final theirs = other.prerelease;
    if (mine == null) return theirs == null ? 0 : 1;
    if (theirs == null) return -1;

    final left = mine.split('.');
    final right = theirs.split('.');
    for (var index = 0; index < left.length && index < right.length; index++) {
      final identifier = _compareIdentifier(left[index], right[index]);
      if (identifier != 0) return identifier;
    }
    return left.length.compareTo(right.length);
  }

  bool operator <(AppVersion other) => compareTo(other) < 0;
  bool operator >(AppVersion other) => compareTo(other) > 0;
  bool operator <=(AppVersion other) => compareTo(other) <= 0;
  bool operator >=(AppVersion other) => compareTo(other) >= 0;

  @override
  bool operator ==(Object other) =>
      other is AppVersion && compareTo(other) == 0;

  @override
  int get hashCode => Object.hash(major, minor, patch, prerelease);

  @override
  String toString() => prerelease == null
      ? '$major.$minor.$patch'
      : '$major.$minor.$patch-$prerelease';

  static final RegExp _numeric = RegExp(r'^[0-9]+$');

  static int _compareIdentifier(String left, String right) {
    final leftIsNumber = _numeric.hasMatch(left);
    final rightIsNumber = _numeric.hasMatch(right);
    if (leftIsNumber && rightIsNumber) {
      // No leading zeros, so the longer number is the larger — and no
      // identifier is ever too long to compare.
      if (left.length != right.length) return left.length.compareTo(right.length);
      return left.compareTo(right);
    }
    if (leftIsNumber) return -1;
    if (rightIsNumber) return 1;
    return left.compareTo(right).sign;
  }
}
