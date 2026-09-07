# Cancels the bookings left behind by end-to-end test runs, through the platform's own admin
# endpoint rather than by touching the database.
#
# WHY A SCRIPT AND NOT A DELETE. A booking is a financial record: it is not ISoftDeletable, there is
# no delete anywhere in the model, and CLAUDE.md forbids reaching past the domain to remove one. The
# supported way to end a booking is for an administrator to cancel it, which is exactly what the
# admin console's Cancel button does and exactly what this calls. Every cancellation lands in the
# booking's status history and in the audit trail, the way a real one would.
#
# It cancels ONLY bookings belonging to accounts the test harness created - addresses matching the
# throwaway pattern below. Anything you booked yourself is left alone.
#
# YOUR PASSWORD IS NEVER STORED. It is read straight into a SecureString, used for one sign-in, and
# discarded when the script exits.

param(
    [string]$ApiBase = 'http://localhost:5012/api/v1',
    [string]$Reason  = 'Test data from an automated end-to-end run.',
    [switch]$WhatIf
)

$ErrorActionPreference = 'Stop'

# The addresses the harness registers under. Nothing else is touched.
$testEmailPattern = '^(rana|omar|lock)\.\d+@example\.jo$'

Write-Host ''
Write-Host '  Cancel test bookings' -ForegroundColor Green
Write-Host '  --------------------'
Write-Host "  API: $ApiBase"
Write-Host ''

$email = Read-Host '  Administrator email'
$secure = Read-Host '  Password' -AsSecureString
$plain = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
    [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))

try {
    $login = Invoke-RestMethod -Method POST -Uri "$ApiBase/auth/login" -ContentType 'application/json' `
        -Body (@{ email = $email; password = $plain } | ConvertTo-Json) -TimeoutSec 30
} finally {
    # Out of memory as soon as it has been used, whether the sign-in worked or not.
    $plain = $null
    [GC]::Collect()
}

$headers = @{ Authorization = "Bearer $($login.accessToken)" }
Write-Host "  Signed in as $($login.user.fullName)" -ForegroundColor Green

# Page through every booking the platform has, not just the first page.
$all = @()
$page = 1
do {
    $batch = Invoke-RestMethod -Uri "$ApiBase/admin/bookings?page=$page&pageSize=100" -Headers $headers -TimeoutSec 60
    $all += $batch.items
    $page++
} while ($page -le $batch.totalPages)

# A booking row carries the customer's ID, never their email, so the test accounts have to be
# resolved first and matched by id.
$customers = @{}
$cpage = 1
do {
    $cbatch = Invoke-RestMethod -Uri "$ApiBase/admin/customers?page=$cpage&pageSize=100" -Headers $headers -TimeoutSec 60
    foreach ($c in $cbatch.items) {
        if ($c.email -match $testEmailPattern) { $customers[$c.userId] = $c.email }
    }
    $cpage++
} while ($cpage -le $cbatch.totalPages)

Write-Host "  $($customers.Count) test account(s) recognised."

# Only a live booking can be cancelled; the rest have already ended.
$targets = $all | Where-Object {
    $_.status -in @('Requested', 'Approved', 'Confirmed') -and $customers.ContainsKey($_.customerId)
}

Write-Host ''
Write-Host "  $($all.Count) booking(s) on the platform; $($targets.Count) belong to test accounts and are still live."
Write-Host ''

if ($targets.Count -eq 0) { Write-Host '  Nothing to do.'; exit 0 }

if ($WhatIf) {
    $targets | ForEach-Object { "    would cancel $($_.reference)  $($_.status)  $($_.periodStart)" }
    Write-Host ''
    Write-Host '  -WhatIf given: nothing was changed.' -ForegroundColor Yellow
    exit 0
}

$done = 0; $failed = 0
foreach ($b in $targets) {
    try {
        $null = Invoke-RestMethod -Method POST -Uri "$ApiBase/admin/bookings/$($b.bookingId)/cancel" `
            -Headers $headers -ContentType 'application/json' `
            -Body (@{ reason = $Reason } | ConvertTo-Json) -TimeoutSec 30
        $done++
        Write-Host "    cancelled $($b.reference)"
    } catch {
        $failed++
        Write-Host "    FAILED $($b.reference): $($_.Exception.Message)" -ForegroundColor Red
    }
}

Write-Host ''
Write-Host "  Cancelled $done, failed $failed." -ForegroundColor Green
Write-Host '  Each one is recorded in the booking history and the audit trail, as an admin cancellation.'
Write-Host ''
