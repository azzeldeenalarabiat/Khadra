<#
    Brings up the customer-website stack on the SANDBOX payment provider, so a booking can be made,
    approved and paid end to end on this machine.

    Run it from a normal PowerShell window, in the checkout you want to run:

        .\start-customer-web.ps1                        # database khadra_web_it
        .\start-customer-web.ps1 -Database khadra_e2e   # any other LOCAL database
        .\start-customer-web.ps1 -Restart               # replace a running API and both BFFs
        .\start-customer-web.ps1 -Stop                  # stop the API and both BFFs
        .\start-customer-web.ps1 -SetPassword           # save the local Postgres password first

    -SetPassword asks for the password at a MASKED prompt and saves the local connection string
    into the API's user-secrets, the store the API itself reads, then starts the stack. Use it the
    first time, and whenever Postgres answers "28P01: password authentication failed".

    Ports, and why each one:
      7112  https  API            both BFFs proxy here; it also serves the sandbox checkout page
      5112  http   API            the website's server-side renderer reads public data here
      7243  https  console BFF    the console's dev server proxies here
      7244  https  customer BFF   the website's dev server proxies here
      4200         console        (ng serve)
      4400         website        (ng serve)
    A port that is already listening is left alone and reported, so running this twice is safe. The
    one to watch is 7112: an API left running from ANOTHER checkout answers there, with other code
    and another database, and no page says so. The last step reads the payment mode of whatever API
    answers and says plainly when it is not this one; -Restart replaces it.

    Local only, by construction:
    - The API runs as Development, and Program.cs will not start a Production host on the sandbox.
    - The database is the one your connection string names, with only its Database= part
      replaced. It is read where the API itself reads it, in the API's order: this window's
      $env:ConnectionStrings__DefaultConnection first, then the API's user-secrets. A Host that is
      not this machine is refused. The password goes to the API's own process only; it is never
      printed or written anywhere.
    - Mail goes to Mailpit (http://localhost:8025) whatever appsettings.Local.json says, so a test
      booking cannot email a real customer, and a password reset is read there.

    Why Sandbox: a database that has taken sandbox payments refuses to start under any other
    provider (PaymentsStartupCheck), deliberately and in both directions. This script satisfies that
    guard; it does not bend it.

    The sandbox signs its deliveries with Payments:WebhookSecret. If the API's user-secrets hold
    none, one is generated, stored there, and never shown.
#>
param(
    [string]$Database = 'khadra_web_it',
    [string]$DbUser = 'khadra',
    [switch]$SetPassword,
    [switch]$Restart,
    [switch]$Stop
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$logs = Join-Path $env:TEMP 'khadra'
New-Item -ItemType Directory -Force -Path $logs | Out-Null

$apiHttps = 'https://localhost:7112'
$apiHttp  = 'http://localhost:5112'
$website  = 'http://localhost:4400'

function Test-Listening([int]$Port) {
    [bool](Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue)
}

function Start-Service-Logged {
    param([string]$Name, [string]$File, [string[]]$ArgList, [string]$WorkDir)
    $p = Start-Process -FilePath $File -ArgumentList $ArgList -WorkingDirectory $WorkDir `
        -RedirectStandardOutput (Join-Path $logs "$Name.log") `
        -RedirectStandardError  (Join-Path $logs "$Name.err.log") `
        -WindowStyle Hidden -PassThru
    Write-Host ("  {0,-13} pid {1}" -f $Name, $p.Id)
    return $p
}

# Sets variables for the child about to start, then puts the caller's back. Start-Process copies
# this process's environment, and this script runs inside YOUR PowerShell session: without the
# restore, the connection string would stay in that session after the script ends.
function Invoke-WithEnvironment {
    param([System.Collections.IDictionary]$Variables, [scriptblock]$Action)
    $saved = @{}
    foreach ($name in $Variables.Keys) {
        $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
        [Environment]::SetEnvironmentVariable($name, [string]$Variables[$name], 'Process')
    }
    try { & $Action }
    finally {
        foreach ($name in $saved.Keys) { [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process') }
    }
}

# Stops waiting the moment the service exits, and shows the first line it wrote to stderr, which
# is where a startup failure (a refused password, the payments guard) says what it was.
function Wait-Port {
    param([int]$Port, [string]$Label, $Process = $null, [int]$TimeoutSeconds = 240)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-Listening $Port) {
            Write-Host ("  {0,-13} listening on {1}" -f $Label, $Port) -ForegroundColor Green
            return $true
        }
        if ($Process -and $Process.HasExited) {
            Write-Host ("  {0,-13} EXITED before listening on {1}" -f $Label, $Port) -ForegroundColor Red
            $first = Get-Content (Join-Path $logs "$Label.err.log") -ErrorAction SilentlyContinue |
                Where-Object { $_.Trim() } | Select-Object -First 1
            if ($first) { Write-Host ('                ' + ($first -replace '(?i)Password=[^;"]*', 'Password=***')) -ForegroundColor Red }
            return $false
        }
        Start-Sleep -Seconds 2
    }
    Write-Host ("  {0,-13} did NOT come up on {1} - see {2}\{0}.log and .err.log" -f $Label, $Port, $logs) -ForegroundColor Red
    return $false
}

# Only the .NET services: they are what goes stale when the code changes. A process that is not
# one of ours is named and left alone.
function Stop-Listeners([int[]]$Ports) {
    $ids = Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue |
        Where-Object { $Ports -contains $_.LocalPort } |
        Select-Object -ExpandProperty OwningProcess -Unique
    foreach ($id in $ids) {
        $process = Get-Process -Id $id -ErrorAction SilentlyContinue
        if (-not $process) { continue }
        if (@('Khadra.WebAPI', 'Khadra.Bff', 'dotnet') -notcontains $process.ProcessName) {
            Write-Host ("  leaving       {0} (pid {1}): not a Khadra service" -f $process.ProcessName, $id) -ForegroundColor Yellow
            continue
        }
        Write-Host ("  stopping      {0} (pid {1})" -f $process.ProcessName, $id)
        Stop-Process -Id $id -Force
    }
}

# The API's user-secrets, read the way the API reads them: flat "Section:Key" names, which is what
# `dotnet user-secrets set` writes.
function Get-ApiUserSecretsFile {
    $csproj = Get-Content (Join-Path $root 'Khadra.WebAPI\Khadra.WebAPI.csproj') -Raw
    if ($csproj -notmatch '<UserSecretsId>([^<]+)</UserSecretsId>') { throw 'Khadra.WebAPI.csproj names no UserSecretsId.' }
    return Join-Path $env:APPDATA "Microsoft\UserSecrets\$($Matches[1])\secrets.json"
}

function Get-ApiUserSecret([string]$Key) {
    $file = Get-ApiUserSecretsFile
    if (-not (Test-Path $file)) { return $null }
    $property = (Get-Content $file -Raw | ConvertFrom-Json).PSObject.Properties[$Key]
    if ($property) { return [string]$property.Value }
    return $null
}

if ($Stop -or $Restart) {
    Write-Host "`nStopping the API and both BFFs" -ForegroundColor Cyan
    Stop-Listeners @(7112, 5112, 7243, 7244)
    if ($Stop) { return }
    Start-Sleep -Seconds 2
}

if ($SetPassword) {
    Write-Host "`nLocal Postgres password" -ForegroundColor Cyan
    $secure = Read-Host "  Password for the local Postgres user '$DbUser' (not shown)" -AsSecureString
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try {
        $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
        # A ; would end the password inside the connection string, and Windows PowerShell passes a
        # native argument's embedded quotes unescaped: either would save a different password from
        # the one typed. Refused rather than silently mangled.
        if ($plain -match '[";]') { throw 'A password containing " or ; cannot be saved safely this way; set it with dotnet user-secrets directly.' }
        if (-not $plain) { throw 'No password entered; nothing saved.' }
        & dotnet user-secrets set 'ConnectionStrings:DefaultConnection' "Host=localhost;Port=5432;Database=$Database;Username=$DbUser;Password=$plain" --project (Join-Path $root 'Khadra.WebAPI') | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'dotnet user-secrets could not save the connection string.' }
    } finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
        $plain = $null
    }
    Write-Host "  saved ConnectionStrings:DefaultConnection ($Database, user $DbUser, password not shown)"
    Write-Host "  into $(Get-ApiUserSecretsFile)"
    # Anything this window carries would outrank what was just saved, exactly as it would for the API.
    if ($env:ConnectionStrings__DefaultConnection) {
        Write-Host '  NOTE: this window also sets $env:ConnectionStrings__DefaultConnection, which outranks user-secrets.' -ForegroundColor Yellow
    }
}

Write-Host "`nPrerequisites" -ForegroundColor Cyan
foreach ($dependency in @(@{ Port = 5432; Name = 'Postgres' }, @{ Port = 6379; Name = 'Redis' }, @{ Port = 1025; Name = 'Mailpit' })) {
    if (-not (Test-Listening $dependency.Port)) {
        throw "$($dependency.Name) is not listening on $($dependency.Port). Start the containers first: docker compose up -d, in the checkout that holds your .env."
    }
}
Write-Host '  containers    Postgres, Redis and Mailpit are up'

# The API's own order: an environment variable outranks user-secrets.
$connection = $env:ConnectionStrings__DefaultConnection
$source = 'this window'
if (-not $connection) {
    $connection = Get-ApiUserSecret 'ConnectionStrings:DefaultConnection'
    $source = 'the API user-secrets'
}
if (-not $connection) {
    throw "No connection string. Set ConnectionStrings:DefaultConnection in the API's user-secrets (CLAUDE.md, 'Dev secrets') or `$env:ConnectionStrings__DefaultConnection in this window. Any local database will do, because this script replaces the name."
}
$hostName = [regex]::Match($connection, '(?i)(?:^|;)\s*(?:Host|Server)\s*=\s*([^;]*)').Groups[1].Value.Trim()
if (@('localhost', '127.0.0.1', '::1') -notcontains $hostName) {
    throw 'The connection string in user-secrets does not point at this machine. This script only ever runs against a local database.'
}
if ($connection -notmatch '(?i)(?:^|;)\s*Database\s*=') {
    throw 'The connection string in user-secrets names no Database=, so there is nothing to replace.'
}
$connection = [regex]::Replace($connection, '(?i)((?:^|;)\s*Database\s*=)[^;]*', { param($m) $m.Groups[1].Value + $Database })
Write-Host "  database      $Database on $hostName (credentials from $source, not shown)"

if (-not (Get-ApiUserSecret 'Payments:WebhookSecret')) {
    $bytes = New-Object byte[] 32
    $random = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $random.GetBytes($bytes)
    $random.Dispose()
    & dotnet user-secrets set 'Payments:WebhookSecret' ([Convert]::ToBase64String($bytes)) --project (Join-Path $root 'Khadra.WebAPI') | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not store Payments:WebhookSecret in the API user-secrets.' }
    Write-Host '  webhook       generated Payments:WebhookSecret and stored it in user-secrets (not shown)'
} else {
    Write-Host '  webhook       Payments:WebhookSecret found in user-secrets'
}

$apiEnvironment = [ordered]@{
    ASPNETCORE_ENVIRONMENT               = 'Development'
    ASPNETCORE_URLS                      = "$apiHttps;$apiHttp"
    ConnectionStrings__DefaultConnection = $connection
    Payments__Provider                   = 'Sandbox'
    # The sandbox's checkout page is served by the API itself. The customer is sent back to the
    # website afterwards, and every emailed customer link points there too.
    Payments__SandboxConsoleBaseUrl      = $apiHttps
    Payments__ReturnUrlBase              = $website
    App__CustomerAppBaseUrl              = $website
    Email__Provider                      = 'Smtp'
    Email__Host                          = 'localhost'
    Email__Port                          = '1025'
    Email__UseStartTls                   = 'false'
}
$bffEnvironment = [ordered]@{
    BffSecurity__ApiBaseUrl                                        = "$apiHttps/"
    ReverseProxy__Clusters__webapi__Destinations__primary__Address = "$apiHttps/"
}
$websiteEnvironment = [ordered]@{
    KHADRA_API_URL         = $apiHttp
    KHADRA_PUBLIC_BASE_URL = $website
}

$startApi      = -not ((Test-Listening 7112) -or (Test-Listening 5112))
$startConsole  = -not (Test-Listening 7243)
$startCustomer = -not (Test-Listening 7244)

# Built once, up front: two `dotnet run` of one project at the same moment race for its obj folder.
Write-Host "`nBuilding" -ForegroundColor Cyan
if ($startApi) {
    & dotnet build (Join-Path $root 'Khadra.WebAPI') -nologo -v q -clp:ErrorsOnly
    if ($LASTEXITCODE -ne 0) { throw 'Khadra.WebAPI did not build.' }
    Write-Host '  Khadra.WebAPI built'
}
if ($startConsole -or $startCustomer) {
    & dotnet build (Join-Path $root 'Khadra.Bff') -nologo -v q -clp:ErrorsOnly
    if ($LASTEXITCODE -ne 0) { throw 'Khadra.Bff did not build.' }
    Write-Host '  Khadra.Bff built'
}

Write-Host "`nServices" -ForegroundColor Cyan
$api = $consoleBff = $customerBff = $site = $console = $null
if ($startApi) {
    $api = Invoke-WithEnvironment $apiEnvironment {
        Start-Service-Logged 'web-api' 'dotnet' @('run', '--project', 'Khadra.WebAPI', '--no-launch-profile', '--no-build') $root
    }
} else {
    Write-Host '  web-api       7112/5112 already in use - left alone, checked below' -ForegroundColor Yellow
}
if ($startConsole) {
    $consoleBff = Invoke-WithEnvironment $bffEnvironment {
        Start-Service-Logged 'console-bff' 'dotnet' @('run', '--project', 'Khadra.Bff', '--launch-profile', 'https', '--no-build') $root
    }
} else {
    Write-Host '  console-bff   7243 already in use - left alone' -ForegroundColor Yellow
}
if ($startCustomer) {
    $customerBff = Invoke-WithEnvironment $bffEnvironment {
        Start-Service-Logged 'customer-bff' 'dotnet' @('run', '--project', 'Khadra.Bff', '--launch-profile', 'customer-web', '--no-build') $root
    }
} else {
    Write-Host '  customer-bff  7244 already in use - left alone' -ForegroundColor Yellow
}
if (-not (Test-Listening 4400)) {
    $site = Invoke-WithEnvironment $websiteEnvironment {
        Start-Service-Logged 'website' 'cmd.exe' @('/c', 'npx', 'ng', 'serve', '--host', 'localhost', '--port', '4400') (Join-Path $root 'Khadra.Web')
    }
} else {
    Write-Host '  website       4400 already in use - left alone' -ForegroundColor Yellow
}
if (-not (Test-Listening 4200)) {
    $console = Start-Service-Logged 'console' 'cmd.exe' @('/c', 'npm', 'start') (Join-Path $root 'Khadra.Dashboard')
} else {
    Write-Host '  console       4200 already in use - left alone' -ForegroundColor Yellow
}

Write-Host "`nWaiting" -ForegroundColor Cyan
$apiUp = Wait-Port 7112 'web-api' $api
if (-not $apiUp -and $api -and (Select-String -Path (Join-Path $logs 'web-api.err.log') -Pattern '28P01' -Quiet -ErrorAction SilentlyContinue)) {
    Write-Host '                Postgres refused the password. Put the right one where this script reads it:' -ForegroundColor Yellow
    Write-Host '                  dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=khadra_web_it;Username=khadra;Password=<yours>" --project Khadra.WebAPI' -ForegroundColor Yellow
    Write-Host '                or set $env:ConnectionStrings__DefaultConnection in this window. Then run it again.' -ForegroundColor Yellow
}
$null = Wait-Port 7243 'console-bff' $consoleBff
$null = Wait-Port 7244 'customer-bff' $customerBff
$null = Wait-Port 4400 'website' $site
$null = Wait-Port 4200 'console' $console

# Ready means the database answered; Development applies pending migrations before it listens.
Write-Host "`nChecking the API" -ForegroundColor Cyan
$ready = $null
$deadline = (Get-Date).AddSeconds($(if ($apiUp) { 120 } else { 0 }))
while (-not $ready -and (Get-Date) -lt $deadline) {
    try { $ready = Invoke-WebRequest "$apiHttp/health/ready" -UseBasicParsing -TimeoutSec 10 }
    catch { Start-Sleep -Seconds 2 }
}
if ($ready) {
    Write-Host ("  health        {0} {1}" -f $ready.StatusCode, $ready.Content) -ForegroundColor Green
} else {
    Write-Host "  health        /health/ready never answered 200 - see $logs\web-api.log and web-api.err.log" -ForegroundColor Red
}
try {
    $mode = (Invoke-RestMethod "$apiHttp/api/v1/app-config" -TimeoutSec 10).payments.mode
    if ($mode -eq 'Sandbox') {
        Write-Host "  payments      $mode" -ForegroundColor Green
    } else {
        Write-Host "  payments      $mode - not the API this script configures. Run again with -Restart." -ForegroundColor Red
    }
} catch {
    Write-Host '  payments      /api/v1/app-config did not answer' -ForegroundColor Red
}

Write-Host "`nReady" -ForegroundColor Cyan
Write-Host "  Website        $website"
Write-Host '  Console        http://localhost:4200'
Write-Host '  Mail (Mailpit) http://localhost:8025   every email this stack sends lands here'
Write-Host "  API            $apiHttps   (Scalar at /scalar/v1)"
Write-Host "`n  Logs: $logs`n"
