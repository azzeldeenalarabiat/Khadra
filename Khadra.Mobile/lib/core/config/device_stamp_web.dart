/// The web build asks for nothing.
///
/// A browser writes its own `User-Agent` and refuses to let a page set one, so a
/// value built here would be discarded on the way out — and the server already
/// records the browser's, which is what Registered Devices reads for a web
/// session. Empty means "send no header of ours".
String deviceStamp() => '';
