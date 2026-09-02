$ErrorActionPreference = 'SilentlyContinue'
$inputJson = [Console]::In.ReadToEnd() | ConvertFrom-Json
$cmd = $inputJson.tool_input.command
if (-not $cmd) { exit 0 }

$patterns = @(
    'rm\s+-rf\b',
    'git\s+push\s+(--force|-f)\b',
    'dotnet\s+ef\s+database\s+drop',
    'docker\s+compose\s+down\s+.*-v',
    'drop\s+table',
    'drop\s+database'
)
foreach ($p in $patterns) {
    if ($cmd -match $p) {
        [Console]::Error.WriteLine("Blocked dangerous command pattern ($p). If this is intentional and reviewed, ask the user to run it manually.")
        exit 2
    }
}
exit 0
