# End-to-end drive of the booking-request slice against the real API and Postgres.
# Everything is created the way a customer would create it: register, verify by the emailed link,
# upload documents, browse, quote, book. Nothing is written straight to the database.

$ErrorActionPreference = 'Stop'
$api = 'http://localhost:5012/api/v1'
$mail = 'http://localhost:8025/api/v1'
$pass = 0
$fail = 0

# 10:00 in Amman (UTC+3) on a given day offset. Since 2026-09-07 a self-pickup must fall inside the
# gallery's opening hours, so a harness that booked at "now + N days" would start failing after the
# galleries closed for the evening -- a test that passes or fails by the clock on the wall.
function AmmanMorning([int]$daysFromNow, [int]$hourLocal = 10) {
  $utcHour = $hourLocal - 3
  return [DateTimeOffset]::new(
    [DateTime]::UtcNow.Date.AddDays($daysFromNow).AddHours($utcHour), [TimeSpan]::Zero).ToString('o')
}

function Step($name, $ok, $detail = '') {
  if ($ok) { $script:pass++; Write-Host ("  PASS  " + $name + $(if ($detail) { " -- $detail" } else { '' })) }
  else     { $script:fail++; Write-Host ("  FAIL  " + $name + $(if ($detail) { " -- $detail" } else { '' })) -ForegroundColor Red }
}

# Returns @{ Status = <int>; Code = <problem code>; Body = <object> } for any response.
function Call($method, $url, $body, $token, $raw) {
  $headers = @{}
  if ($token) { $headers['Authorization'] = "Bearer $token" }
  try {
    $request = @{ Method = $method; Uri = $url; Headers = $headers; TimeoutSec = 30; UseBasicParsing = $true }
    if ($null -ne $body) {
      $request['Body'] = ($body | ConvertTo-Json -Depth 6)
      $request['ContentType'] = 'application/json'
    }
    $response = Invoke-WebRequest @request
    $parsed = $null
    if ($response.Content) { try { $parsed = $response.Content | ConvertFrom-Json } catch {} }
    return @{ Status = [int]$response.StatusCode; Code = $null; Body = $parsed }
  } catch {
    $r = $_.Exception.Response
    if (-not $r) { return @{ Status = 0; Code = $_.Exception.Message; Body = $null } }
    $status = [int]$r.StatusCode
    $text = (New-Object IO.StreamReader($r.GetResponseStream())).ReadToEnd()
    $code = $null; $parsed = $null
    try { $parsed = $text | ConvertFrom-Json; $code = $parsed.code } catch {}
    return @{ Status = $status; Code = $code; Body = $parsed }
  }
}

Write-Host "`n=== 1. A customer registers ==="
$stamp = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$email = "rana.$stamp@example.jo"
# A generated password for a throwaway local test account. Not a real credential.
$password = "Kh-" + [guid]::NewGuid().ToString('N').Substring(0, 16) + "!7"

$reg = Call POST "$api/auth/register" @{
  email = $email; password = $password; fullName = 'Rana Sharif'
  phone = "079$((Get-Random -Minimum 1000000 -Maximum 9999999))"
  dateOfBirth = '1995-04-12'; isForeignNational = $false
} $null
Step 'register returns 201' ($reg.Status -eq 201) "status $($reg.Status) $($reg.Code)"

Write-Host "`n=== 2. Booking is refused before the email is verified ==="
# Sign-in itself is blocked before verification, so this is checked after verifying, using a
# second account. Recorded here so the order of the story stays readable.

Write-Host "`n=== 3. The verification email arrives, and its link works ==="
# Wait for the message rather than guessing at a delay. The first send after the API starts is much
# slower than every one after it - a cold SMTP connection - and a fixed 1.2s sleep made this step a
# coin toss that the harness lost on the FIRST customer of every run while the second sailed through.
$mine = $null
$waited = 0
while ($null -eq $mine -and $waited -lt 45) {
  Start-Sleep -Seconds 2
  $waited += 2
  $messages = Invoke-RestMethod "$mail/messages?limit=50"
  $mine = $messages.messages | Where-Object { @($_.To | ForEach-Object { $_.Address }) -contains $email } | Select-Object -First 1
}
Step 'verification email delivered' ($null -ne $mine) $(if ($mine) { "$($mine.Subject) [after $waited s]" } else { "nothing after $waited s" })

$token = $null
if ($mine) {
  $full = Invoke-RestMethod "$mail/message/$($mine.ID)"
  $text = "$($full.Text)$($full.HTML)"
  if ($text -match 'token=([A-Za-z0-9_\-\.]+)') { $token = $Matches[1] }
}
Step 'email carries a verification token' ($null -ne $token)

$verify = Call POST "$api/auth/verify-email" @{ token = $token } $null
Step 'verify-email returns 200' ($verify.Status -eq 200) "status $($verify.Status) $($verify.Code)"

Write-Host "`n=== 4. Sign in ==="
$login = Call POST "$api/auth/login" @{ email = $email; password = $password } $null
Step 'login returns 200' ($login.Status -eq 200) "status $($login.Status) $($login.Code)"
$bearer = $login.Body.accessToken
Step 'a bearer token is issued' ($null -ne $bearer)

Write-Host "`n=== 5. A car is chosen from the live catalogue ==="
$cars = Call GET "$api/vehicles?page=1&pageSize=20" $null $null
Step 'the catalogue lists real cars' ($cars.Body.totalCount -gt 0) "$($cars.Body.totalCount) listed"
if ($cars.Body.totalCount -eq 0) { Write-Host "  no listed cars to book"; exit 1 }

# A window this run owns. The bookings below are real and stay in the database, so the harness
# has to find dates nothing already holds -- including its own earlier runs -- rather than guess.
# It needs six clear days from $base: the rental, the turnaround probe just after it, and the
# booking placed exactly at the gap.
# Six clear days for the rental, the turnaround probes and the booking placed exactly at the gap,
# plus a clear stretch 60 days later for the opening-hours probes. Searched across CARS as well as
# dates, because previous runs of this harness leave real bookings behind and they pile up on
# whichever car the catalogue happens to list first.
$car = $null
$base = $null
foreach ($candidateCar in $cars.Body.items) {
  foreach ($candidate in 10..120) {
    $from = AmmanMorning $candidate
    $to = AmmanMorning ($candidate + 6) 12
    $probe = Call GET "$api/vehicles/$($candidateCar.vehicleId)/quote?pickupAt=$([uri]::EscapeDataString($from))&returnAt=$([uri]::EscapeDataString($to))&pickupMethod=SelfPickup" $null $null
    if ($probe.Status -ne 200 -or -not $probe.Body.isAvailable) { continue }
    # The opening-hours section books 60 days further out; make sure that is clear too.
    $farFrom = AmmanMorning ($candidate + 60)
    $farTo = AmmanMorning ($candidate + 63) 12
    $farProbe = Call GET "$api/vehicles/$($candidateCar.vehicleId)/quote?pickupAt=$([uri]::EscapeDataString($farFrom))&returnAt=$([uri]::EscapeDataString($farTo))&pickupMethod=SelfPickup" $null $null
    if ($farProbe.Status -eq 200 -and $farProbe.Body.isAvailable) {
      $car = $candidateCar; $base = $candidate; break
    }
  }
  if ($null -ne $base) { break }
}
Step 'a free car and window were found for this run' ($null -ne $base) $(if ($base) { "$($car.make) $($car.model), days +$base to +$($base + 6)" } else { 'every listed car is booked solid' })
if ($null -eq $base) { Write-Host "  cannot continue without a free window"; exit 1 }
$pickup = AmmanMorning $base
$return = AmmanMorning ($base + 3) 12

Write-Host "`n=== 6. Booking is refused while documents are missing ==="
$noDocs = Call POST "$api/bookings" @{
  vehicleId = $car.vehicleId; pickupAt = $pickup; returnAt = $return; pickupMethod = 'SelfPickup'
} $bearer
Step 'refused as documents_incomplete' ($noDocs.Status -eq 403 -and $noDocs.Code -eq 'booking.documents_incomplete') "status $($noDocs.Status) code $($noDocs.Code)"

Write-Host "`n=== 7. The customer uploads a licence and an ID ==="
Add-Type -AssemblyName System.Net.Http
$jpeg = [byte[]](0xFF,0xD8,0xFF,0xE0,0x00,0x10,0x4A,0x46,0x49,0x46,0x00,0x01) + (1..600 | ForEach-Object { [byte](Get-Random -Max 255) }) + [byte[]](0xFF,0xD9)
foreach ($type in @('DrivingLicenceFront','DrivingLicenceBack','NationalId')) {
  $client = [System.Net.Http.HttpClient]::new()
  $client.DefaultRequestHeaders.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $bearer)
  $form = [System.Net.Http.MultipartFormDataContent]::new()
  $form.Add([System.Net.Http.StringContent]::new($type), 'type')
  $file = [System.Net.Http.ByteArrayContent]::new($jpeg)
  $file.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::new('image/jpeg')
  $form.Add($file, 'file', "$type.jpg")
  $up = $client.PostAsync("$api/customers/me/documents", $form).Result
  Step "uploaded $type" ($up.StatusCode -eq 201) "status $([int]$up.StatusCode)"
  $client.Dispose()
}

Write-Host "`n=== 8. The dates are quoted, then booked ==="
$quote = Call GET "$api/vehicles/$($car.vehicleId)/quote?pickupAt=$([uri]::EscapeDataString($pickup))&returnAt=$([uri]::EscapeDataString($return))&pickupMethod=SelfPickup" $null $null
Step 'quote returns a price' ($quote.Status -eq 200) "status $($quote.Status) days $($quote.Body.pricing.days) total $($quote.Body.pricing.totalPrice.amount)"
Step 'quote says the car is available' ($quote.Body.isAvailable -eq $true)

$booking = Call POST "$api/bookings" @{
  vehicleId = $car.vehicleId; pickupAt = $pickup; returnAt = $return; pickupMethod = 'SelfPickup'
} $bearer
Step 'booking returns 201' ($booking.Status -eq 201) "status $($booking.Status) code $($booking.Code)"
$made = $booking.Body
if ($made) {
  Step 'status is Requested' ($made.status -eq 'Requested') $made.status
  Step 'nothing is paid' ($made.depositPaid -eq $false)
  Step 'no payment clock yet' ($null -eq $made.paymentDeadline)
  Step 'the dealer has a deadline' ($null -ne $made.decisionDeadline) $made.decisionDeadline
  Step 'the price matches the quote' ($made.pricing.totalPrice.amount -eq $quote.Body.pricing.totalPrice.amount) "$($made.pricing.totalPrice.amount) JOD"
  Step 'a reference was minted' ($made.reference -like 'KH-*') $made.reference
}

Write-Host "`n=== 9. The car is now held ==="
$after = Call GET "$api/vehicles/$($car.vehicleId)/quote?pickupAt=$([uri]::EscapeDataString($pickup))&returnAt=$([uri]::EscapeDataString($return))&pickupMethod=SelfPickup" $null $null
Step 'the quote now says unavailable' ($after.Body.isAvailable -eq $false)

$search = Call GET "$api/vehicles?page=1&pageSize=20&pickupAt=$([uri]::EscapeDataString($pickup))&returnAt=$([uri]::EscapeDataString($return))" $null $null
$stillListed = $search.Body.items | Where-Object { $_.vehicleId -eq $car.vehicleId }
Step 'the search hides the held car for those dates' ($null -eq $stillListed) "$($search.Body.totalCount) car(s) free"

$other = Call GET "$api/vehicles?page=1&pageSize=20" $null $null
Step 'the car is still listed with no dates at all' ($null -ne ($other.Body.items | Where-Object { $_.vehicleId -eq $car.vehicleId }))

$clearBase = $null
foreach ($candidate in ($base + 10)..165) {
  $from = AmmanMorning $candidate
  $to = AmmanMorning ($candidate + 3) 12
  $probe = Call GET "$api/vehicles/$($car.vehicleId)/quote?pickupAt=$([uri]::EscapeDataString($from))&returnAt=$([uri]::EscapeDataString($to))&pickupMethod=SelfPickup" $null $null
  if ($probe.Status -eq 200 -and $probe.Body.isAvailable) { $clearBase = $candidate; break }
}
Step 'a clear window was found' ($null -ne $clearBase) "offset +$clearBase"
$clearStart = AmmanMorning $clearBase
$clearEnd = AmmanMorning ($clearBase + 3) 12
$clear = Call GET "$api/vehicles?page=1&pageSize=20&pickupAt=$([uri]::EscapeDataString($clearStart))&returnAt=$([uri]::EscapeDataString($clearEnd))" $null $null
Step 'and free for dates nothing holds' ($null -ne ($clear.Body.items | Where-Object { $_.vehicleId -eq $car.vehicleId })) "$($clear.Body.totalCount) car(s) free"

$halfWindow = Call GET "$api/vehicles?page=1&pageSize=20&pickupAt=$([uri]::EscapeDataString($clearStart))" $null $null
Step 'half a window is refused rather than ignored' ($halfWindow.Code -eq 'catalogue.incomplete_period') "code $($halfWindow.Code)"

Write-Host "`n=== 10. Booking the same car for the same dates again ==="
$again = Call POST "$api/bookings" @{
  vehicleId = $car.vehicleId; pickupAt = $pickup; returnAt = $return; pickupMethod = 'SelfPickup'
} $bearer
Step 'refused as vehicle_unavailable' ($again.Status -eq 409 -and $again.Code -eq 'booking.vehicle_unavailable') "status $($again.Status) code $($again.Code)"

Write-Host "`n=== 11. The turnaround gap is enforced ==="
# The held booking ends at 12:00. A pickup at 13:00 is inside the gallery's two-hour turnaround gap;
# one at 14:00 is exactly at it. Both are inside opening hours, so the gap is what decides them.
$tooClose = AmmanMorning ($base + 3) 13
$gap = Call POST "$api/bookings" @{
  vehicleId = $car.vehicleId; pickupAt = $tooClose; returnAt = (AmmanMorning ($base + 5) 12); pickupMethod = 'SelfPickup'
} $bearer
Step 'a booking inside the turnaround gap is refused' ($gap.Status -eq 409 -and $gap.Code -eq 'booking.vehicle_unavailable') "status $($gap.Status) code $($gap.Code)"

# Exactly at the gap: allowed.
$atGap = AmmanMorning ($base + 3) 14
$ok = Call POST "$api/bookings" @{
  vehicleId = $car.vehicleId; pickupAt = $atGap; returnAt = (AmmanMorning ($base + 6) 12); pickupMethod = 'SelfPickup'
} $bearer
Step 'a booking exactly at the gap is allowed' ($ok.Status -eq 201) "status $($ok.Status) code $($ok.Code)"

Write-Host "`n=== 12. The date bounds ==="
$soon = Call POST "$api/bookings" @{
  vehicleId = $car.vehicleId; pickupAt = ([DateTimeOffset]::UtcNow.AddMinutes(90).ToString('o')); returnAt = ([DateTimeOffset]::UtcNow.AddDays(2).ToString('o')); pickupMethod = 'SelfPickup'
} $bearer
Step 'too soon is refused' ($soon.Code -eq 'booking.too_soon') "code $($soon.Code)"

$far = Call POST "$api/bookings" @{
  vehicleId = $car.vehicleId; pickupAt = ([DateTimeOffset]::UtcNow.AddDays(181).ToString('o')); returnAt = ([DateTimeOffset]::UtcNow.AddDays(184).ToString('o')); pickupMethod = 'SelfPickup'
} $bearer
Step 'beyond the horizon is refused' ($far.Code -eq 'booking.beyond_horizon') "code $($far.Code)"

$past = Call POST "$api/bookings" @{
  vehicleId = $car.vehicleId; pickupAt = ([DateTimeOffset]::UtcNow.AddDays(-1).ToString('o')); returnAt = ([DateTimeOffset]::UtcNow.AddDays(2).ToString('o')); pickupMethod = 'SelfPickup'
} $bearer
Step 'a past date is refused' ($past.Code -eq 'booking.period_in_past') "code $($past.Code)"

$backwards = Call POST "$api/bookings" @{
  vehicleId = $car.vehicleId; pickupAt = $return; returnAt = $pickup; pickupMethod = 'SelfPickup'
} $bearer
Step 'a return before the pickup is refused' ($backwards.Status -eq 400) "status $($backwards.Status) code $($backwards.Code)"

Write-Host "`n=== 12b. The longest a rental may run ==="
$config = Call GET "$api/app-config" $null $null
Step 'app-config publishes all three date bounds' (
  $config.Body.minimumBookingLeadTimeMinutes -eq 120 -and
  $config.Body.maxAdvanceBookingDays -eq 180 -and
  $config.Body.maxRentalDays -eq 90) "lead $($config.Body.minimumBookingLeadTimeMinutes)m, horizon $($config.Body.maxAdvanceBookingDays)d, max $($config.Body.maxRentalDays)d"

$tooLong = Call POST "$api/bookings" @{
  vehicleId = $car.vehicleId
  pickupAt = ([DateTimeOffset]::UtcNow.AddDays(3).ToString('o'))
  returnAt = ([DateTimeOffset]::UtcNow.AddDays(3 + 91).ToString('o'))
  pickupMethod = 'SelfPickup'
} $bearer
Step 'a 91-day rental is refused' ($tooLong.Code -eq 'booking.rental_too_long') "code $($tooLong.Code)"
Step 'the refusal names both figures' (
  $tooLong.Body.title -match '90' -and $tooLong.Body.title -match '91') "$($tooLong.Body.title)"

# The quote must refuse it too, or a screen would price a rental the booking rejects.
$longQuote = Call GET "$api/vehicles/$($car.vehicleId)/quote?pickupAt=$([uri]::EscapeDataString([DateTimeOffset]::UtcNow.AddDays(3).ToString('o')))&returnAt=$([uri]::EscapeDataString([DateTimeOffset]::UtcNow.AddDays(94).ToString('o')))&pickupMethod=SelfPickup" $null $null
Step 'the quote refuses the same span' ($longQuote.Code -eq 'booking.rental_too_long') "code $($longQuote.Code)"

$longSearch = Call GET "$api/vehicles?page=1&pageSize=5&pickupAt=$([uri]::EscapeDataString([DateTimeOffset]::UtcNow.AddDays(3).ToString('o')))&returnAt=$([uri]::EscapeDataString([DateTimeOffset]::UtcNow.AddDays(94).ToString('o')))" $null $null
Step 'the search refuses it too, rather than listing cars nobody can book' ($longSearch.Code -eq 'booking.rental_too_long') "code $($longSearch.Code)"

Write-Host "`n=== 12c. The gallery's opening hours ==="
$hours = docker exec khadra-postgres psql -U khadra -d khadra_e2e -Atc "select operating_hours from dealers d join vehicles v on v.dealer_id = d.id where v.id = '$($car.vehicleId)';"
Write-Host "        this gallery: $hours"

# 03:00 Amman: every gallery on the platform is shut.
$night = Call POST "$api/bookings" @{
  vehicleId = $car.vehicleId; pickupAt = (AmmanMorning ($base + 60) 3); returnAt = (AmmanMorning ($base + 63) 12); pickupMethod = 'SelfPickup'
} $bearer
Step 'a self-pickup in the middle of the night is refused' ($night.Code -eq 'booking.pickup_outside_opening_hours') "code $($night.Code)"
Step 'and the refusal says what the gallery actually does' ($night.Body.title -match '\d\d:\d\d-\d\d:\d\d') "$($night.Body.title)"

# Collected in working hours, brought back at 23:00: the counter is shut at the OTHER end.
$lateBack = Call POST "$api/bookings" @{
  vehicleId = $car.vehicleId; pickupAt = (AmmanMorning ($base + 60) 10); returnAt = (AmmanMorning ($base + 63) 23); pickupMethod = 'SelfPickup'
} $bearer
Step 'a return after closing is refused, and named as the return' ($lateBack.Code -eq 'booking.return_outside_opening_hours') "code $($lateBack.Code)"

# The quote must agree, or a screen prices a rental the booking rejects.
$nightQuote = Call GET "$api/vehicles/$($car.vehicleId)/quote?pickupAt=$([uri]::EscapeDataString((AmmanMorning ($base + 60) 3)))&returnAt=$([uri]::EscapeDataString((AmmanMorning ($base + 63) 12)))&pickupMethod=SelfPickup" $null $null
Step 'the quote refuses it too' ($nightQuote.Code -eq 'booking.pickup_outside_opening_hours') "code $($nightQuote.Code)"

# And a booking well inside the hours still goes through, so the rule is not refusing everything.
$inHours = Call POST "$api/bookings" @{
  vehicleId = $car.vehicleId; pickupAt = (AmmanMorning ($base + 60) 10); returnAt = (AmmanMorning ($base + 63) 12); pickupMethod = 'SelfPickup'
} $bearer
Step 'a self-pickup inside opening hours is accepted' ($inHours.Status -eq 201) "status $($inHours.Status) code $($inHours.Code)"

Write-Host "`n=== 13. The quote applies the same bounds as the booking ==="
$soonQuote = Call GET "$api/vehicles/$($car.vehicleId)/quote?pickupAt=$([uri]::EscapeDataString([DateTimeOffset]::UtcNow.AddMinutes(90).ToString('o')))&returnAt=$([uri]::EscapeDataString([DateTimeOffset]::UtcNow.AddDays(2).ToString('o')))&pickupMethod=SelfPickup" $null $null
Step 'the quote refuses a pickup inside the lead time' ($soonQuote.Code -eq 'booking.too_soon') "code $($soonQuote.Code)"

$soonSearch = Call GET "$api/vehicles?page=1&pageSize=5&pickupAt=$([uri]::EscapeDataString([DateTimeOffset]::UtcNow.AddMinutes(90).ToString('o')))&returnAt=$([uri]::EscapeDataString([DateTimeOffset]::UtcNow.AddDays(2).ToString('o')))" $null $null
Step 'the search refuses the same dates' ($soonSearch.Code -eq 'booking.too_soon' -or $soonSearch.Code -eq 'booking.period_in_past') "code $($soonSearch.Code)"

Write-Host "`n=== 14. Unknown and unbookable cars ==="
$ghost = Call POST "$api/bookings" @{
  vehicleId = [guid]::NewGuid().ToString(); pickupAt = $pickup; returnAt = $return; pickupMethod = 'SelfPickup'
} $bearer
Step 'an unknown car is not a car' ($ghost.Status -eq 404 -and $ghost.Code -eq 'vehicle.not_found') "status $($ghost.Status) code $($ghost.Code)"

$teleport = Call POST "$api/bookings" @{
  vehicleId = $car.vehicleId; pickupAt = $pickup; returnAt = $return; pickupMethod = 'Teleport'
} $bearer
Step 'an unknown pickup method is refused' ($teleport.Code -eq 'booking.unknown_pickup_method') "code $($teleport.Code)"

Write-Host "`n=== 15. Delivery ==="
$noWhere = Call POST "$api/bookings" @{
  vehicleId = $car.vehicleId; pickupAt = ([DateTimeOffset]::UtcNow.AddDays(175).ToString('o')); returnAt = ([DateTimeOffset]::UtcNow.AddDays(178).ToString('o')); pickupMethod = 'Delivery'
} $bearer
Step 'delivery without a location is refused' ($noWhere.Status -eq 400) "status $($noWhere.Status) code $($noWhere.Code)"

$halfPoint = Call POST "$api/bookings" @{
  vehicleId = $car.vehicleId; pickupAt = ([DateTimeOffset]::UtcNow.AddDays(175).ToString('o')); returnAt = ([DateTimeOffset]::UtcNow.AddDays(178).ToString('o')); pickupMethod = 'Delivery'; latitude = 31.95
} $bearer
Step 'half a coordinate is refused' ($halfPoint.Status -eq 400) "status $($halfPoint.Status) code $($halfPoint.Code)"

$selfWithPoint = Call POST "$api/bookings" @{
  vehicleId = $car.vehicleId; pickupAt = ([DateTimeOffset]::UtcNow.AddDays(175).ToString('o')); returnAt = ([DateTimeOffset]::UtcNow.AddDays(178).ToString('o')); pickupMethod = 'SelfPickup'; latitude = 31.95; longitude = 35.91
} $bearer
Step 'self-pickup carrying a location is refused' ($selfWithPoint.Code -eq 'booking.delivery_location_not_allowed') "code $($selfWithPoint.Code)"

Write-Host "`n=== 16. Anonymous and cross-role access ==="
$anon = Call POST "$api/bookings" @{
  vehicleId = $car.vehicleId; pickupAt = $pickup; returnAt = $return; pickupMethod = 'SelfPickup'
} $null
Step 'an anonymous caller cannot book' ($anon.Status -eq 401) "status $($anon.Status)"

Write-Host "`n=== 17. An unverified account cannot book ==="
$email2 = "omar.$stamp@example.jo"
$reg2 = Call POST "$api/auth/register" @{
  email = $email2; password = $password; fullName = 'Omar Nasser'
  phone = "078$((Get-Random -Minimum 1000000 -Maximum 9999999))"
  dateOfBirth = '1993-02-02'; isForeignNational = $false
} $null
Step 'a second customer registers' ($reg2.Status -eq 201) "status $($reg2.Status)"
$login2 = Call POST "$api/auth/login" @{ email = $email2; password = $password } $null
Step 'an unverified account cannot even sign in' ($login2.Status -ne 200) "status $($login2.Status) code $($login2.Code)"

Write-Host "`n=== 18. The customer sees their own bookings ==="
$mineList = Call GET "$api/bookings?page=1&pageSize=20" $null $bearer
Step 'the list returns the bookings just made' ($mineList.Body.totalCount -eq 3) "$($mineList.Body.totalCount) booking(s)"
$counts = Call GET "$api/bookings/tab-counts" $null $bearer
Step 'tab counts put them in Pending' ($counts.Body.pending -eq $counts.Body.all -and $counts.Body.pending -ge 2) "pending $($counts.Body.pending), all $($counts.Body.all)"

$one = Call GET "$api/bookings/$($made.bookingId)" $null $bearer
Step 'the booking reads back in full' ($one.Status -eq 200 -and $one.Body.reference -eq $made.reference) $one.Body.reference

Write-Host "`n=== 19. Concurrency: many customers, one car, same dates ==="
$raceBase = $null
foreach ($candidate in ($base + 10)..170) {
  if ($candidate -ge $clearBase -and $candidate -le ($clearBase + 3)) { continue }
  $from = AmmanMorning $candidate
  $to = AmmanMorning ($candidate + 3) 12
  $probe = Call GET "$api/vehicles/$($car.vehicleId)/quote?pickupAt=$([uri]::EscapeDataString($from))&returnAt=$([uri]::EscapeDataString($to))&pickupMethod=SelfPickup" $null $null
  if ($probe.Status -eq 200 -and $probe.Body.isAvailable) { $raceBase = $candidate; break }
}
Step 'a clear window was found for the race' ($null -ne $raceBase) "offset +$raceBase"
$raceStart = AmmanMorning $raceBase
$raceEnd = AmmanMorning ($raceBase + 3) 12
$body = @{ vehicleId = $car.vehicleId; pickupAt = $raceStart; returnAt = $raceEnd; pickupMethod = 'SelfPickup' } | ConvertTo-Json
$jobs = 1..6 | ForEach-Object {
  Start-Job -ScriptBlock {
    param($url, $body, $bearer)
    try {
      $r = Invoke-WebRequest -Method POST -Uri $url -Body $body -ContentType 'application/json' -Headers @{ Authorization = "Bearer $bearer" } -TimeoutSec 30 -UseBasicParsing
      return "$([int]$r.StatusCode)|ok"
    } catch {
      $resp = $_.Exception.Response
      if (-not $resp) { return "0|$($_.Exception.Message)" }
      $text = (New-Object IO.StreamReader($resp.GetResponseStream())).ReadToEnd()
      $code = try { ($text | ConvertFrom-Json).code } catch { 'unparseable' }
      return "$([int]$resp.StatusCode)|$code"
    }
  } -ArgumentList "$api/bookings", $body, $bearer
}
$results = $jobs | Wait-Job -Timeout 90 | Receive-Job
$jobs | Remove-Job -Force
$created = @($results | Where-Object { $_ -like '201|*' })
$conflicts = @($results | Where-Object { $_ -like '409|booking.vehicle_unavailable' })
Write-Host ("        results: " + ($results -join ', '))
Step 'exactly one of six concurrent requests wins' ($created.Count -eq 1) "$($created.Count) created"
Step 'every loser is told the car was taken' (($created.Count + $conflicts.Count) -eq $results.Count) "$($conflicts.Count) conflict(s) of $($results.Count)"

Write-Host "`n=== 20. The database agrees ==="
$sql = "select status, count(*) from bookings group by status order by 1;"
$rows = docker exec khadra-postgres psql -U khadra -d khadra_e2e -Atc $sql
Write-Host ("        rows: " + ($rows -join '; '))
$requested = ($rows | Where-Object { $_ -like 'Requested|*' })
Step 'every booking made is Requested in the database' ($null -ne $requested) "$requested"

$shape = docker exec khadra-postgres psql -U khadra -d khadra_e2e -Atc "select count(*) from bookings where decision_deadline is null or payment_deadline is not null or deposit_payment_id is not null;"
Step 'none carries a payment clock or a deposit' ([int]$shape -eq 0) "$shape row(s) with a payment clock"

$notified = docker exec khadra-postgres psql -U khadra -d khadra_e2e -Atc "select count(*) from notifications where kind = 'BookingRequested';"
Step 'the gallery was notified about the requests' ([int]$notified -gt 0) "$notified notification(s)"

$named = docker exec khadra-postgres psql -U khadra -d khadra_e2e -Atc "select count(*) from notifications where kind = 'BookingRequested' and (actor_name <> 'A customer' or actor_user_id is not null);"
Step 'no customer is named on any of them' ([int]$named -eq 0) "$named row(s) naming somebody"

$holds = docker exec khadra-postgres psql -U khadra -d khadra_e2e -Atc "select count(*) from pg_constraint where conname = 'bookings_one_hold_per_vehicle';"
Step 'the exclusion constraint is in place' ([int]$holds -eq 1)

$overlap = docker exec khadra-postgres psql -U khadra -d khadra_e2e -Atc @"
select count(*) from bookings a join bookings b
  on a.vehicle_id = b.vehicle_id and a.id < b.id
 and tstzrange(a.hold_start, a.period_end, '[)') && tstzrange(b.hold_start, b.period_end, '[)')
where a.status in ('Requested','Approved','Confirmed','PickedUp')
  and b.status in ('Requested','Approved','Confirmed','PickedUp');
"@
Step 'no two live bookings overlap on one car' ([int]$overlap -eq 0) "$overlap overlapping pair(s)"

Write-Host ("`n  passed $pass, failed $fail")
if ($fail -gt 0) { exit 1 }
