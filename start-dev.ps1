<#
    Brings the whole Khadra development stack up.

    Run it from a normal PowerShell window:

        .\start-dev.ps1

    Each service is started with Start-Process so it inherits YOUR environment.
    That detail matters: the API reads its connection string from dotnet
    user-secrets, which live under your profile, so a service launched from a
    different account or an empty environment block starts and then dies with
    "ConnectionStrings:DefaultConnection is required."

    Ports, and why each one:
      5432/6379/1025/8025  Postgres, Redis, Mailpit          (docker compose)
      7012  https  API     the BFF proxies here
      5012  http   API     bound to 0.0.0.0 so a phone on the Wi-Fi can reach it
      7243  https  BFF     the console's dev-server proxies here
      5243  http   BFF
      4200         Console (ng serve)
      4300         Customer app (static build, bound to 0.0.0.0)
#>

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$logs = Join-Path $env:TEMP 'khadra'
New-Item -ItemType Directory -Force -Path $logs | Out-Null

function Start-Service-Logged {
    param([string]$Name, [string]$File, [string[]]$ArgList, [string]$WorkDir)
    $p = Start-Process -FilePath $File -ArgumentList $ArgList -WorkingDirectory $WorkDir `
        -RedirectStandardOutput (Join-Path $logs "$Name.log") `
        -RedirectStandardError  (Join-Path $logs "$Name.err.log") `
        -WindowStyle Hidden -PassThru
    Write-Host ("  {0,-10} pid {1}" -f $Name, $p.Id)
}

function Wait-Port {
    param([int]$Port, [string]$Label, [int]$TimeoutSeconds = 180)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue) {
            Write-Host ("  {0,-10} listening on {1}" -f $Label, $Port) -ForegroundColor Green
            return $true
        }
        Start-Sleep -Seconds 2
    }
    Write-Host ("  {0,-10} did NOT come up on {1} - see {2}\{0}.log" -f $Label, $Port, $logs) -ForegroundColor Red
    return $false
}

Write-Host "`nContainers" -ForegroundColor Cyan
Push-Location $root
docker compose up -d | Out-Null
Pop-Location
Wait-Port 5432 'postgres' 120 | Out-Null

Write-Host "`nServices" -ForegroundColor Cyan
$env:ASPNETCORE_ENVIRONMENT = 'Development'

# Both endpoints: HTTPS for the BFF's proxy, plain HTTP on every interface for a phone.
$env:ASPNETCORE_URLS = 'https://localhost:7012;http://0.0.0.0:5012'
Start-Service-Logged 'api' 'dotnet' @('run', '--project', 'Khadra.WebAPI', '--no-launch-profile') $root

Remove-Item Env:\ASPNETCORE_URLS -ErrorAction SilentlyContinue
Start-Service-Logged 'bff' 'dotnet' @('run', '--project', 'Khadra.Bff', '--launch-profile', 'https') $root

Start-Service-Logged 'console' 'cmd.exe' @('/c', 'npm', 'start') (Join-Path $root 'Khadra.Dashboard')
Start-Service-Logged 'app' 'cmd.exe' @('/c', 'npx', '--yes', 'http-server', 'build/web', '-p', '4300', '-a', '0.0.0.0', '-c-1', '--cors', '--silent') (Join-Path $root 'Khadra.Mobile')

Write-Host "`nWaiting" -ForegroundColor Cyan
Wait-Port 7012 'api'     | Out-Null
Wait-Port 7243 'bff'     | Out-Null
Wait-Port 4300 'app'     | Out-Null
Wait-Port 4200 'console' | Out-Null

$lan = (Get-NetIPAddress -AddressFamily IPv4 |
    Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' -and $_.InterfaceAlias -notlike '*WSL*' } |
    Select-Object -First 1).IPAddress

Write-Host "`nReady" -ForegroundColor Cyan
Write-Host "  Console        http://localhost:4200"
Write-Host "  Customer app   http://localhost:4300"
Write-Host "  Mail (Mailpit) http://localhost:8025"
if ($lan) {
    # No rebuild needed when this address changes: the web build reads its API host
    # from whatever address served the page. See lib/core/config/app_environment.dart.
    Write-Host "  On your phone  http://${lan}:4300" -ForegroundColor Yellow
    Write-Host "                 (same Wi-Fi, and the firewall must allow inbound 4300 and 5012)"
}
Write-Host "`n  Logs: $logs`n"


