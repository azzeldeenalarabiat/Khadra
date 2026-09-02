$ErrorActionPreference = 'SilentlyContinue'
$inputJson = [Console]::In.ReadToEnd() | ConvertFrom-Json
$filePath = $inputJson.tool_input.file_path
if (-not $filePath) { exit 0 }
if ($filePath -match 'node_modules|[\\/](bin|obj|dist)[\\/]|\.claude[\\/]worktrees') { exit 0 }

if ($filePath -match '\.cs$') {
    dotnet format Khadra.slnx --include $filePath --no-restore | Out-Null
}
elseif ($filePath -match '\.(ts|html)$') {
    npx --prefix Khadra.Dashboard prettier --write $filePath | Out-Null
}
exit 0
