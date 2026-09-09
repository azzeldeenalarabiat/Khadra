# End-to-end drive of the PAYMENT and REPUTATION slices against the real API and Postgres.
#
# The sibling of booking-flow-e2e.ps1, which stops where the dealer's answer begins. This one starts
# there: a dealer answers, a customer tries to pay, the provider is not there, the clocks run out, and
# a gallery reads what the platform knows about the person in front of them.
#
# Everything is created the way a real actor would create it. Nothing is written straight to the
# database; the database is read only to check that what the API said matches what was stored.
#
# It expects the API at 5012 with mail pointed at MAILPIT (start-mailpit), an approved dealership with
# at least one listed car, and the dealer owner's password in $env:KHADRA_E2E_DEALER_PASSWORD.

$ErrorActionPreference = 'Stop'
$api = 'http://localhost:5012/api/v1'
$mail = 'http://localhost:8025/api/v1'
$db = { param($sql) docker exec khadra-postgres psql -U khadra -d khadra_e2e -Atc $sql }
$pass = 0
$fail = 0

function Step($name, $ok, $detail = '') {
  if ($ok) { $script:pass++; Write-Host ("  PASS  " + $name + $(if ($detail) { " -- $detail" } else { '' })) }
  else     { $script:fail++; Write-Host ("  FAIL  " + $name + $(if ($detail) { " -- $detail" } else { '' })) -ForegroundColor Red }
}

function Call($method, $url, $body, $token) {
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

# 10:00 in Amman (UTC+3). A self-pickup must fall inside the gallery's opening hours, so a harness
# that booked at "now + N days" would pass or fail by the clock on the wall.
function AmmanMorning([int]$daysFromNow, [int]$hourLocal = 10) {
  $utcHour = $hourLocal - 3
  return [DateTimeOffset]::new(
    [DateTime]::UtcNow.Date.AddDays($daysFromNow).AddHours($utcHour), [TimeSpan]::Zero).ToString('o')
}

function NewCustomer([string]$prefix) {
  $stamp = [guid]::NewGuid().ToString('N').Substring(0, 10)
  $email = "$prefix.$stamp@example.jo"
  # A generated password for a throwaway local test account. Not a real credential.
  $password = "Kh-" + [guid]::NewGuid().ToString('N').Substring(0, 16) + "!7"
  $reg = Call POST "$api/auth/register" @{
    email = $email; password = $password; fullName = 'Test Renter'
    phone = "079$((Get-Random -Minimum 1000000 -Maximum 9999999))"
    dateOfBirth = '1994-02-11'; isForeignNational = $false
  } $null
  if ($reg.Status -ne 201) { throw "register failed: $($reg.Status) $($reg.Code)" }

  $token = $null; $waited = 0
  while ($null -eq $token -and $waited -lt 40) {
    Start-Sleep -Seconds 2; $waited += 2
    $messages = Invoke-RestMethod "$mail/messages?limit=50"
    $mine = $messages.messages | Where-Object { @($_.To | ForEach-Object { $_.Address }) -contains $email } | Select-Object -First 1
    if ($mine) {
      $full = Invoke-RestMethod "$mail/message/$($mine.ID)"
      $text = "$($full.Text)$($full.HTML)"
      if ($text -match 'token=([A-Za-z0-9_\-\.]+)') { $token = $Matches[1] }
    }
  }
  if (-not $token) { throw "no verification token for $email after $waited s" }
  $null = Call POST "$api/auth/verify-email" @{ token = $token } $null

  $login = Call POST "$api/auth/login" @{ email = $email; password = $password } $null
  if ($login.Status -ne 200) { throw "login failed for $email" }

  Add-Type -AssemblyName System.Net.Http
  $jpeg = [byte[]](0xFF,0xD8,0xFF,0xE0,0x00,0x10,0x4A,0x46,0x49,0x46,0x00,0x01) +
          (1..600 | ForEach-Object { [byte](Get-Random -Max 255) }) + [byte[]](0xFF,0xD9)
  foreach ($type in @('DrivingLicenceFront','DrivingLicenceBack','NationalId')) {
    $client = [System.Net.Http.HttpClient]::new()
    $client.DefaultRequestHeaders.Authorization =
      [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $login.Body.accessToken)
    $form = [System.Net.Http.MultipartFormDataContent]::new()
    $form.Add([System.Net.Http.StringContent]::new($type), 'type')
    $file = [System.Net.Http.ByteArrayContent]::new($jpeg)
    $file.Headers.ContentType = [System.Net.Http.Headers.MediaTypeHeaderValue]::new('image/jpeg')
    $form.Add($file, 'file', "$type.jpg")
    $null = $client.PostAsync("$api/customers/me/documents", $form).Result
    $client.Dispose()
  }

  return @{ Email = $email; Password = $password; Token = $login.Body.accessToken }
}

Write-Host "`n=== 0. The platform says out loud that it cannot take money ==="
$config = Call GET "$api/app-config" $null $null
Step 'app-config answers' ($config.Status -eq 200)

$dealerPassword = $env:KHADRA_E2E_DEALER_PASSWORD
if (-not $dealerPassword) {
  Write-Host "  set KHADRA_E2E_DEALER_PASSWORD to the dealer owner's password" -ForegroundColor Yellow
  exit 2
}
$dealerEmail = $env:KHADRA_E2E_DEALER_EMAIL
if (-not $dealerEmail) {
  Write-Host "  set KHADRA_E2E_DEALER_EMAIL to the dealer owner's address" -ForegroundColor Yellow
  exit 2
}

$dealerLogin = Call POST "$api/auth/login" @{ email = $dealerEmail; password = $dealerPassword } $null
Step 'the dealer owner signs in' ($dealerLogin.Status -eq 200) "status $($dealerLogin.Status) $($dealerLogin.Code)"
if ($dealerLogin.Status -ne 200) { exit 1 }
$dealer = $dealerLogin.Body.accessToken

Write-Host "`n=== 1. A customer books, the gallery approves ==="
$customer = NewCustomer 'payer'
Step 'a verified customer with documents exists' ($null -ne $customer.Token) $customer.Email

$cars = Call GET "$api/vehicles?page=1&pageSize=20" $null $null
$car = $null; $base = $null
foreach ($candidateCar in $cars.Body.items) {
  foreach ($candidate in 10..150) {
    $from = AmmanMorning $candidate
    $to = AmmanMorning ($candidate + 3) 12
    $probe = Call GET "$api/vehicles/$($candidateCar.vehicleId)/quote?pickupAt=$([uri]::EscapeDataString($from))&returnAt=$([uri]::EscapeDataString($to))&pickupMethod=SelfPickup" $null $null
    if ($probe.Status -eq 200 -and $probe.Body.isAvailable) { $car = $candidateCar; $base = $candidate; break }
  }
  if ($null -ne $base) { break }
}
Step 'a free car and window were found' ($null -ne $base) $(if ($base) { "$($car.make) $($car.model) at +$base" } else { 'nothing free' })
if ($null -eq $base) { exit 1 }

$booking = Call POST "$api/bookings" @{
  vehicleId = $car.vehicleId; pickupAt = (AmmanMorning $base); returnAt = (AmmanMorning ($base + 3) 12)
  pickupMethod = 'SelfPickup'
} $customer.Token
Step 'the booking is created' ($booking.Status -eq 201) "status $($booking.Status) $($booking.Code)"
$bookingId = $booking.Body.bookingId

Write-Host "`n=== 2. Before approval there is nothing to pay ==="
# The POST answers "this platform cannot take money" BEFORE it reads anything, so while no provider
# is configured every checkout gets 503 whatever the booking says. That ordering is deliberate: a
# platform that cannot take money must not create a row saying it is trying to, and answering before
# any read means the endpoint leaks nothing at all about whose booking this is.
#
# The BOOKING-specific reason still reaches the customer, through the GET below, which is what the
# screen gates its button on.
$early = Call POST "$api/bookings/$bookingId/deposit-checkout" $null $customer.Token
Step 'a checkout is refused' ($early.Status -in 409, 503) "status $($early.Status) code $($early.Code)"

$mine = Call GET "$api/bookings/$bookingId" $null $customer.Token
Step 'the customer is told they cannot pay yet' ($mine.Body.payment.canPay -eq $false) "reason $($mine.Body.payment.unavailableReason)"
Step 'and the reason is the booking, not the provider' ($mine.Body.payment.unavailableReason -eq 'booking.not_awaiting_payment')

Write-Host "`n=== 3. The gallery approves, and the deposit falls due ==="
$approve = Call POST "$api/bookings/$bookingId/approve" @{ note = 'Ready for you.' } $dealer
Step 'approve returns 200' ($approve.Status -eq 200) "status $($approve.Status) $($approve.Code)"
Step 'the booking is Approved' ($approve.Body.status -eq 'Approved') $approve.Body.status
Step 'a payment clock now exists' ($null -ne $approve.Body.paymentDeadline) $approve.Body.paymentDeadline
Step 'and nothing is paid' ($approve.Body.depositPaid -eq $false)

$due = Call GET "$api/bookings/$bookingId" $null $customer.Token
Step 'the customer is shown the amount and the deadline' (
  $null -ne $due.Body.payment.amountDue -and $null -ne $due.Body.payment.payBy
) "$($due.Body.payment.amountDue.amount) $($due.Body.payment.amountDue.currency) by $($due.Body.payment.payBy)"
Step 'the amount due is the booking own frozen deposit' (
  $due.Body.payment.amountDue.amount -eq $due.Body.pricing.depositAmount.amount
) "$($due.Body.payment.amountDue.amount) vs $($due.Body.pricing.depositAmount.amount)"

Write-Host "`n=== 4. There is no payment provider, and the platform says so ==="
$checkout = Call POST "$api/bookings/$bookingId/deposit-checkout" $null $customer.Token
Step 'a checkout answers 503, not a fake success' ($checkout.Status -eq 503) "status $($checkout.Status)"
Step 'and names the reason' ($checkout.Code -eq 'payments.provider_unavailable') "code $($checkout.Code)"

Step 'the customer screen says the same thing' ($due.Body.payment.canPay -eq $false)
Step 'and distinguishes it from a closed window' (
  $due.Body.payment.unavailableReason -eq 'payments.provider_unavailable'
) "reason $($due.Body.payment.unavailableReason)"

$rows = & $db "select count(*) from payments;"
Step 'NO payment row was created for a checkout that cannot happen' ([int]$rows -eq 0) "$rows payment row(s)"

Write-Host "`n=== 5. The webhook refuses everything it cannot verify ==="
$hook = Call POST "$api/payments/webhooks/none" @{ type = 'payment.captured'; id = 'evt_forged' } $null
Step 'an unsigned notification is refused' ($hook.Status -eq 401) "status $($hook.Status)"
Step 'and named as untrusted rather than malformed' ($hook.Code -eq 'payments.untrusted_event') "code $($hook.Code)"

# A GENUINELY empty body, not ConvertTo-Json of an empty string: that produces two quote characters,
# which sails past the length validator into the signature check and is a different test.
$emptyStatus = 0
try {
  $e = Invoke-WebRequest -Uri "$api/payments/webhooks/none" -Method POST -Body ([byte[]]@()) `
    -ContentType 'application/json' -UseBasicParsing
  $emptyStatus = [int]$e.StatusCode
} catch { $emptyStatus = [int]$_.Exception.Response.StatusCode }
Step 'an empty body is refused by the validator, before any signature check' ($emptyStatus -eq 400) "status $emptyStatus"

# The bound exists because this endpoint is anonymous and runs against whatever the internet sends.
$hugeStatus = 0
try {
  $h = Invoke-WebRequest -Uri "$api/payments/webhooks/none" -Method POST -Body ('x' * 70000) `
    -ContentType 'application/json' -UseBasicParsing
  $hugeStatus = [int]$h.StatusCode
} catch { $hugeStatus = [int]$_.Exception.Response.StatusCode }
Step 'an oversized body is refused rather than parsed' ($hugeStatus -in 400, 413) "status $hugeStatus"

$receipts = & $db "select count(*) from payment_provider_events;"
Step 'nothing forged was recorded as a real event' ([int]$receipts -eq 0) "$receipts receipt(s)"

$bookingPaid = & $db "select count(*) from bookings where deposit_payment_id is not null;"
Step 'NO booking anywhere is marked paid' ([int]$bookingPaid -eq 0) "$bookingPaid paid booking(s)"

Write-Host "`n=== 6. Only the customer may open their own checkout ==="
$byDealer = Call POST "$api/bookings/$bookingId/deposit-checkout" $null $dealer
Step 'the gallery cannot open the customer checkout' ($byDealer.Status -in 403, 404) "status $($byDealer.Status)"

$anon = Call POST "$api/bookings/$bookingId/deposit-checkout" $null $null
Step 'an anonymous caller cannot either' ($anon.Status -eq 401) "status $($anon.Status)"

# With no provider the answer is 503 for everybody, which leaks strictly LESS than a 404 would: a
# stranger and the owner get byte-identical responses. Once a provider exists the ownership check
# runs and the answer becomes 404, never 403 -- telling a stranger a booking exists is itself a leak.
$stranger = NewCustomer 'stranger'
$byStranger = Call POST "$api/bookings/$bookingId/deposit-checkout" $null $stranger.Token
Step 'another customer learns nothing about the booking' ($byStranger.Status -in 404, 503) "status $($byStranger.Status) code $($byStranger.Code)"
Step 'and gets the same answer the owner does' ($byStranger.Status -eq $early.Status) "stranger $($byStranger.Status), owner $($early.Status)"

# The GET is the one a screen reads, and it must NOT be the same for a stranger.
$strangerGet = Call GET "$api/bookings/$bookingId" $null $stranger.Token
Step 'and cannot read the booking itself at all' ($strangerGet.Status -eq 404) "status $($strangerGet.Status) code $($strangerGet.Code)"

Write-Host "`n=== 7. A gallery reads the customer's history, and only while the booking is live ==="
$rep = Call GET "$api/bookings/$bookingId/customer-reputation" $null $dealer
Step 'the gallery may read it on a live booking' ($rep.Status -eq 200) "status $($rep.Status) $($rep.Code)"
if ($rep.Status -eq 200) {
  Step 'it carries no contact details' (
    -not ($rep.Body.PSObject.Properties.Name -match 'email|phone|nationalId|dateOfBirth')
  ) ("fields: " + (($rep.Body.PSObject.Properties.Name) -join ','))
  Step 'and no per-review rows' (-not ($rep.Body.PSObject.Properties.Name -contains 'reviews'))
  Step 'a first-time customer reads as absent, not bad' ($rep.Body.hasHistory -eq $false) "hasHistory $($rep.Body.hasHistory)"
}

$repByCustomer = Call GET "$api/bookings/$bookingId/customer-reputation" $null $customer.Token
Step 'the customer cannot read it through the dealer route' ($repByCustomer.Status -in 403, 404) "status $($repByCustomer.Status)"

$repByStranger = Call GET "$api/bookings/$bookingId/customer-reputation" $null $stranger.Token
Step 'nor can another customer' ($repByStranger.Status -in 403, 404) "status $($repByStranger.Status)"

$mineRep = Call GET "$api/customers/me/reputation" $null $customer.Token
Step 'the customer CAN read their own' ($mineRep.Status -eq 200) "status $($mineRep.Status) $($mineRep.Code)"

$otherDealerRep = Call GET "$api/bookings/$([guid]::NewGuid())/customer-reputation" $null $dealer
Step 'an unknown booking is not found' ($otherDealerRep.Status -eq 404) "status $($otherDealerRep.Status)"

Write-Host "`n=== 8. Rating a customer is refused until the rental is finished ==="
$rateEarly = Call POST "$api/bookings/$bookingId/customer-rating" @{ rating = 5 } $dealer
Step 'an unfinished booking cannot be rated' ($rateEarly.Code -eq 'review.booking_not_completed') "status $($rateEarly.Status) code $($rateEarly.Code)"

$rateByCustomer = Call POST "$api/bookings/$bookingId/customer-rating" @{ rating = 5 } $customer.Token
Step 'a customer cannot rate themselves through it' ($rateByCustomer.Status -in 403, 404) "status $($rateByCustomer.Status)"

$rateOutOfRange = Call POST "$api/bookings/$bookingId/customer-rating" @{ rating = 6 } $dealer
Step 'a rating outside 1-5 is refused' ($rateOutOfRange.Status -eq 400) "status $($rateOutOfRange.Status)"

Write-Host "`n=== 9. The customer may still cancel, and it costs nothing unpaid ==="
$preview = Call GET "$api/bookings/$bookingId" $null $customer.Token
Step 'the server says cancelling is possible' ($preview.Body.cancellation.canCancel -eq $true)
Step 'and free, because nothing was paid' ($preview.Body.cancellation.isFree -eq $true)

$cancel = Call POST "$api/bookings/$bookingId/cancel" @{ reasonCode = 'PlansChanged'; details = 'Testing.' } $customer.Token
Step 'cancel returns 200' ($cancel.Status -eq 200) "status $($cancel.Status) $($cancel.Code)"
Step 'the booking is Cancelled' ($cancel.Body.status -eq 'Cancelled') $cancel.Body.status

Write-Host "`n=== 10. Everything the booking allowed is now refused ==="
$payAfterCancel = Call POST "$api/bookings/$bookingId/deposit-checkout" $null $customer.Token
Step 'no checkout on a cancelled booking' ($payAfterCancel.Status -in 409, 503) "status $($payAfterCancel.Status) code $($payAfterCancel.Code)"

# A SECOND cancel is idempotent ON PURPOSE, and this is the retry a phone makes when it never saw the
# first response. Answering "this can no longer be cancelled" would tell the customer their
# cancellation FAILED when it had succeeded. The aggregate stays strict -- an admin double-cancelling
# is still refused -- and the retry is recognised in the handler, where the caller's identity is known.
$firstReason = $cancel.Body.cancellationReasonCode
$firstHistory = @($cancel.Body.history).Count
$cancelTwice = Call POST "$api/bookings/$bookingId/cancel" @{ reasonCode = 'FoundBetterPrice'; details = 'Again.' } $customer.Token
Step 'a repeated cancel is idempotent, not an error' ($cancelTwice.Status -eq 200) "status $($cancelTwice.Status) code $($cancelTwice.Code)"
Step 'and does not overwrite the reason the customer first gave' ($cancelTwice.Body.cancellationReasonCode -eq $firstReason) "first '$firstReason', after retry '$($cancelTwice.Body.cancellationReasonCode)'"
Step 'nor write a second cancellation into the history' (@($cancelTwice.Body.history).Count -eq $firstHistory) "$firstHistory then $(@($cancelTwice.Body.history).Count) entries"

# Read through to_jsonb rather than naming the column. It is spelled `to` -- the entity's property is
# `To` and the snake-case convention adds no suffix -- which is a reserved word needing double quotes,
# and PowerShell strips those on the way to a native executable however they are written. The json
# form needs single quotes only, so it survives the trip.
$dbCancels = & $db ('select count(*) from booking_status_changes t where t.booking_id = ''' +
  $bookingId + ''' and to_jsonb(t) ->> ''to'' = ''Cancelled'';')
Step 'the database holds exactly one cancellation for it' ([int]$dbCancels -eq 1) "$dbCancels row(s)"

$approveAfterCancel = Call POST "$api/bookings/$bookingId/approve" @{ note = 'Too late.' } $dealer
Step 'the gallery cannot approve it now' ($approveAfterCancel.Status -eq 409) "status $($approveAfterCancel.Status) code $($approveAfterCancel.Code)"

$repAfterCancel = Call GET "$api/bookings/$bookingId/customer-reputation" $null $dealer
Step 'and the customer history closes with the booking' (
  $repAfterCancel.Code -eq 'review.reputation_not_available'
) "status $($repAfterCancel.Status) code $($repAfterCancel.Code)"

Write-Host "`n=== 11. The database agrees ==="
$live = & $db "select count(*) from payments where status in ('Initiated','Pending');"
Step 'no payment attempt is left open' ([int]$live -eq 0) "$live open attempt(s)"

$orphans = & $db "select count(*) from payment_refunds where status <> 'Settled';"
Step 'no refund is owed' ([int]$orphans -eq 0) "$orphans outstanding refund(s)"

$guards = & $db "select count(*) from pg_indexes where indexname in ('ux_payments_one_live_attempt_per_booking','ix_payment_provider_events_provider_provider_event_id');"
Step 'both payment guards exist in the database' ([int]$guards -eq 2) "$guards of 2"

$paidAnywhere = & $db "select count(*) from bookings where deposit_payment_id is not null;"
Step 'nothing on the platform claims a paid deposit' ([int]$paidAnywhere -eq 0) "$paidAnywhere"

Write-Host ""
Write-Host ("  passed $pass, failed $fail") -ForegroundColor $(if ($fail -eq 0) { 'Green' } else { 'Red' })
if ($fail -gt 0) { exit 1 }
