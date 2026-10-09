import 'package:dio/dio.dart';

/// The API's idea of "now", for deciding when something the SERVER will judge has
/// become possible (pre-launch item 225: a handover code is issued only from
/// the moment the handover may be recorded).
///
/// A phone's clock can be minutes out, in either direction. Judged on it, a phone
/// running slow would hide the pickup code from a customer standing at the counter,
/// and one running fast would offer a code the server then refuses. So the offset
/// between the two clocks is read from the `Date` header of the API's own answers,
/// and "now" is the device clock corrected by it.
///
/// The server stays the authority: this decides only what the app OFFERS. A request
/// made a second too early is refused with the moment it becomes possible, and the
/// screen words that. Until the first answer arrives (and on the web, where the
/// browser does not expose `Date` to the page) the offset is zero: the device clock,
/// which is what the app used before.
class ServerClock {
  ServerClock({DateTime Function()? deviceNow}) : _deviceNow = deviceNow ?? DateTime.now;

  final DateTime Function() _deviceNow;

  Duration _offset = Duration.zero;

  /// How far the server's clock is ahead of this phone's (negative when behind).
  Duration get offset => _offset;

  /// The server's "now", in UTC.
  DateTime now() => _deviceNow().toUtc().add(_offset);

  /// Records an instant the server stated in an answer received just now.
  void observe(DateTime serverTime) {
    _offset = serverTime.toUtc().difference(_deviceNow().toUtc());
  }

  static const _months = {
    'Jan': 1, 'Feb': 2, 'Mar': 3, 'Apr': 4, 'May': 5, 'Jun': 6,
    'Jul': 7, 'Aug': 8, 'Sep': 9, 'Oct': 10, 'Nov': 11, 'Dec': 12,
  };

  static final _httpDate =
      RegExp(r'^[A-Za-z]{3}, (\d{2}) ([A-Za-z]{3}) (\d{4}) (\d{2}):(\d{2}):(\d{2}) GMT$');

  /// An HTTP `Date` header (RFC 9110 IMF-fixdate, `Sun, 06 Nov 1994 08:49:37 GMT`),
  /// or null for anything else: an unreadable header moves nothing.
  static DateTime? parseHttpDate(String? value) {
    final match = _httpDate.firstMatch(value?.trim() ?? '');
    final month = match == null ? null : _months[match.group(2)];
    if (match == null || month == null) return null;
    return DateTime.utc(
      int.parse(match.group(3)!),
      month,
      int.parse(match.group(1)!),
      int.parse(match.group(4)!),
      int.parse(match.group(5)!),
      int.parse(match.group(6)!),
    );
  }
}

/// Reads the `Date` of every answer from the API's own origin into [ServerClock].
///
/// Errors count too: a 409 carries the server's time as much as a 200 does. An
/// upload to another host does not, because its clock is not the one that judges.
class ServerClockInterceptor extends Interceptor {
  ServerClockInterceptor(this.clock, {required String apiBaseUrl}) : _api = Uri.parse(apiBaseUrl);

  final ServerClock clock;
  final Uri _api;

  @override
  void onResponse(Response<dynamic> response, ResponseInterceptorHandler handler) {
    _observe(response);
    handler.next(response);
  }

  @override
  void onError(DioException err, ErrorInterceptorHandler handler) {
    final response = err.response;
    if (response != null) _observe(response);
    handler.next(err);
  }

  void _observe(Response<dynamic> response) {
    final uri = response.requestOptions.uri;
    if (uri.scheme != _api.scheme || uri.host != _api.host || uri.port != _api.port) return;
    final stated = ServerClock.parseHttpDate(response.headers.value('date'));
    if (stated != null) clock.observe(stated);
  }
}
