$ErrorActionPreference = 'SilentlyContinue'
$inputJson = [Console]::In.ReadToEnd() | ConvertFrom-Json
$filePath = $inputJson.tool_input.file_path
if (-not $filePath) { exit 0 }

if ($filePath -match '[\\/]Migrations[\\/]') {
    # Never hand-edit an EXISTING migration (one that may already have run somewhere).
    # Only the newest migration file may be edited (e.g. to add raw SQL EF cannot scaffold).
    $migrationsDir = Split-Path -Parent $filePath
    $newest = Get-ChildItem -Path $migrationsDir -Filter '*.cs' -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^\d{14}_' -and $_.Name -notmatch '\.Designer\.cs$' } |
        Sort-Object Name |
        Select-Object -Last 1

    $leaf = Split-Path -Leaf $filePath
    $isNewest = $newest -and ($leaf -eq $newest.Name)

    if (-not $isNewest) {
        [Console]::Error.WriteLine("Blocked: never hand-edit an existing migration. Use 'dotnet ef migrations add <Name>' and edit only the newest one.")
        exit 2
    }
}
if ($filePath -match 'appsettings\.Local\.json$') {
    [Console]::Error.WriteLine("Blocked: appsettings.Local.json holds local secrets and must not be edited by automation.")
    exit 2
}
if ($filePath -match '\.env(\.|$)' -and $filePath -notmatch '\.env\.example$') {
    [Console]::Error.WriteLine("Blocked: .env files hold secrets and must not be edited by automation.")
    exit 2
}
exit 0
