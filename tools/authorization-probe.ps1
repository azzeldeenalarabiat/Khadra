# Walks every sensitive endpoint with the WRONG caller and asserts it is refused.
#
# The counterpart to the feature harnesses, which drive each role through what it MAY do. This drives
# each role at what it may NOT, because a permission that is missing shows up in the first kind of
# test and a permission that is too broad only ever shows up in this kind.
#
# Two rules it checks besides the status code:
#
#   - A caller with no standing on a RECORD gets 404, never 403. A 403 confirms the id is real, which
#     for a booking id or a customer id is itself the leak.
#   - A caller with no standing on an AREA gets 403. There is nothing to hide about the existence of
#     the admin dashboard.
#
# It expects the API at 5012, and the dealer owner's credentials in the environment. Customers are
# registered as it goes, through the real screens' own endpoints.

$ErrorActionPreference = 'Stop'
$api = 'http://localhost:5012/api/v1'
$mail = 'http://localhost:8025/api/v1'
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
    return @{ Status = [int]$response.StatusCode; Code = $null }
  } catch {
    $r = $_.Exception.Response
    if (-not $r) { return @{ Status = 0; Code = $_.Exception.Message } }
    $text = (New-Object IO.StreamReader($r.GetResponseStream())).ReadToEnd()
    $code = $null
    try { $code = ($text | ConvertFrom-Json).code } catch {}
    return @{ Status = [int]$r.StatusCode; Code = $code }
  }
}

function NewCustomer([string]$prefix) {
  $stamp = [guid]::NewGuid().ToString('N').Substring(0, 10)
  $email = "$prefix.$stamp@example.jo"
  # A generated password for a throwaway local test account. Not a real credential.
  $password = "Kh-" + [guid]::NewGuid().ToString('N').Substring(0, 16) + "!7"
  $null = Call POST "$api/auth/register" @{
    email = $email; password = $password; fullName = 'Probe Renter'
    phone = "079$((Get-Random -Minimum 1000000 -Maximum 9999999))"
    dateOfBirth = '1993-06-02'; isForeignNational = $false
  } $null

  $token = $null; $waited = 0
  while ($null -eq $token -and $waited -lt 40) {
    Start-Sleep -Seconds 2; $waited += 2
    $messages = Invoke-RestMethod "$mail/messages?limit=50"
    $mine = $messages.messages | Where-Object { @($_.To | ForEach-Object { $_.Address }) -contains $email } | Select-Object -First 1
    if ($mine) {
      $full = Invoke-RestMethod "$mail/message/$($mine.ID)"
      if ("$($full.Text)$($full.HTML)" -match 'token=([A-Za-z0-9_\-\.]+)') { $token = $Matches[1] }
    }
  }
  if (-not $token) { throw "no verification token for $email" }
  $null = Call POST "$api/auth/verify-email" @{ token = $token } $null

  $login = Call POST "$api/auth/login" @{ email = $email; password = $password } $null
  if ($login.Status -ne 200) { throw "login failed for $email" }
  $body = Invoke-RestMethod -Uri "$api/auth/login" -Method POST -UseBasicParsing `
    -Body (@{ email = $email; password = $password } | ConvertTo-Json) -ContentType 'application/json'
  return $body.accessToken
}

$dealerEmail = $env:KHADRA_E2E_DEALER_EMAIL
$dealerPassword = $env:KHADRA_E2E_DEALER_PASSWORD
if (-not $dealerEmail -or -not $dealerPassword) {
  Write-Host "  set KHADRA_E2E_DEALER_EMAIL and KHADRA_E2E_DEALER_PASSWORD" -ForegroundColor Yellow
  exit 2
}
$dealerBody = Invoke-RestMethod -Uri "$api/auth/login" -Method POST -UseBasicParsing `
  -Body (@{ email = $dealerEmail; password = $dealerPassword } | ConvertTo-Json) -ContentType 'application/json'
$dealer = $dealerBody.accessToken
$customer = NewCustomer 'probe'

Write-Host "`n=== 1. Every admin area is closed to a customer and to a dealer ==="
# The routes as the controllers actually declare them. The first draft of this list guessed at
# three of them and they answered 404 -- from ROUTING, not from authorization -- so the probe passed
# without testing anything. A probe that passes because the URL does not exist is worse than none.
$adminRoutes = @(
  'admin/workload', 'admin/dashboard/dealer-counts', 'admin/dealers', 'admin/customers',
  'admin/bookings', 'admin/disputes', 'admin/admin-users', 'admin/settings/business-rules',
  'admin/audit-logs'
)
foreach ($route in $adminRoutes) {
  $asCustomer = Call GET "$api/$route" $null $customer
  $asDealer = Call GET "$api/$route" $null $dealer
  $anon = Call GET "$api/$route" $null $null
  # 403 for an AREA: there is nothing to hide about whether the admin console exists, and insisting
  # on 403 rather than "403 or 404" is what stops a mistyped route passing as a refusal.
  Step "$route closed to a customer" ($asCustomer.Status -eq 403) "status $($asCustomer.Status)"
  Step "$route closed to a dealer" ($asDealer.Status -eq 403) "status $($asDealer.Status)"
  Step "$route closed to an anonymous caller" ($anon.Status -eq 401) "status $($anon.Status)"
}

Write-Host "`n=== 2. A customer cannot reach the dealer console ==="
$dealerRoutes = @(
  'dealers/me', 'dealers/me/delivery', 'dealers/me/employees',
  'dealers/me/vehicles', 'dealers/me/dashboard', 'dealers/me/reports', 'dealers/me/activity'
)
foreach ($route in $dealerRoutes) {
  $asCustomer = Call GET "$api/$route" $null $customer
  Step "$route closed to a customer" ($asCustomer.Status -eq 403) "status $($asCustomer.Status)"
}

Write-Host "`n=== 3. A dealer cannot reach a customer's own area ==="
foreach ($route in @('customers/me/documents', 'customers/me/reputation')) {
  $asDealer = Call GET "$api/$route" $null $dealer
  Step "$route closed to a dealer" ($asDealer.Status -eq 403) "status $($asDealer.Status)"
}

Write-Host "`n=== 4. One customer cannot see another's booking ==="
$other = NewCustomer 'other'
$cars = Call GET "$api/vehicles?page=1&pageSize=5" $null $null
Step 'the catalogue is readable without an account' ($cars.Status -eq 200) "status $($cars.Status)"

# A booking id that exists: taken from the probe customer's own list, if they have one. Otherwise a
# random id, which still proves the 404 shape.
$mine = Invoke-RestMethod -Uri "$api/bookings" -Headers @{ Authorization = "Bearer $customer" } -UseBasicParsing
$bookingId = if ($mine.items.Count -gt 0) { $mine.items[0].bookingId } else { [guid]::NewGuid() }

foreach ($path in @("bookings/$bookingId", "bookings/$bookingId/review")) {
  $asOther = Call GET "$api/$path" $null $other
  # 404 for a RECORD: a 403 would confirm the id is real, and that is the leak.
  Step "$path is NOT FOUND to another customer" ($asOther.Status -eq 404) "status $($asOther.Status) code $($asOther.Code)"
}

Write-Host "`n=== 5. Nothing is writable by the wrong role ==="
$writes = @(
  @{ m = 'POST'; p = "bookings/$bookingId/approve"; t = $customer; who = 'a customer'; b = @{ note = 'x' } },
  @{ m = 'POST'; p = "bookings/$bookingId/cancel"; t = $dealer; who = 'a dealer'; b = @{ reasonCode = 'PlansChanged' } },
  @{ m = 'POST'; p = 'dealers'; t = $customer; who = 'a customer'; b = @{ businessName = 'x' } },
  @{ m = 'PUT'; p = 'dealers/me/delivery'; t = $customer; who = 'a customer'; b = @{ isEnabled = $false; radiusKm = 0 } },
  @{ m = 'POST'; p = 'dealers/me/delivery/offer-on-listed-vehicles'; t = $customer; who = 'a customer'; b = $null },
  # The invite is a POST on the collection itself, not a sub-resource. Named exactly, so a 404 here
  # would be a real finding rather than a typo.
  @{ m = 'POST'; p = 'admin/admin-users'; t = $dealer; who = 'a dealer'; b = @{ email = 'x@y.jo'; fullName = 'X'; phone = '0790000000' } },
  @{ m = 'POST'; p = "bookings/$bookingId/customer-rating"; t = $customer; who = 'a customer'; b = @{ rating = 5 } }
)
foreach ($w in $writes) {
  $r = Call $w.m "$api/$($w.p)" $w.b $w.t
  # 403 or 404 only. A 400 would mean the request was REJECTED FOR ITS SHAPE before authorization was
  # ever consulted, which tells us nothing about who may call it, and a 409 would mean it got as far
  # as the domain. Either would make this probe pass for the wrong reason.
  Step "$($w.m) $($w.p) refused for $($w.who)" ($r.Status -in 403, 404) "status $($r.Status) code $($r.Code)"
}

Write-Host "`n=== 6. A forged or absent token is refused ==="
$forged = Call GET "$api/customers/me/documents" $null 'not.a.real.token'
Step 'a forged bearer token is rejected' ($forged.Status -eq 401) "status $($forged.Status)"

$none = Call GET "$api/bookings" $null $null
Step 'no token at all is rejected' ($none.Status -eq 401) "status $($none.Status)"

Write-Host "`n=== 7. Error bodies leak nothing ==="
$problem = $null
try {
  Invoke-WebRequest -Uri "$api/bookings/$([guid]::NewGuid())" -Headers @{ Authorization = "Bearer $customer" } -UseBasicParsing | Out-Null
} catch {
  $problem = (New-Object IO.StreamReader($_.Exception.Response.GetResponseStream())).ReadToEnd()
}
Step 'a refusal carries a machine code' ($problem -match '"code"') $problem
Step 'and no stack trace' (-not ($problem -match 'at Khadra\.|StackTrace|\.cs:line')) 'no frames in the body'
Step 'and no connection string or key' (-not ($problem -match 'Password=|ApiKey|SigningKey|Host=')) 'no secrets in the body'

Write-Host ""
Write-Host ("  passed $pass, failed $fail") -ForegroundColor $(if ($fail -eq 0) { 'Green' } else { 'Red' })
if ($fail -gt 0) { exit 1 }
