param([string]$OutputDirectory = (Join-Path (Split-Path $PSScriptRoot -Parent) 'dist'))
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path $PSScriptRoot -Parent
$taskVersion = (Get-Content -LiteralPath (Join-Path $taskRoot 'VERSION') -Raw).Trim()
if ($taskVersion -notmatch '^\d+\.\d+(\.\d+)?$') { throw 'Invalid VERSION' }
if ($env:GITHUB_REF_TYPE -eq 'tag' -and $env:GITHUB_REF_NAME -ne ('v' + $taskVersion)) { throw 'Tag does not match VERSION' }
$taskChangelog = Get-Content -LiteralPath (Join-Path $taskRoot 'CHANGELOG.md') -Raw -Encoding UTF8
if ($taskChangelog -notmatch ('(?m)^## v' + [regex]::Escape($taskVersion) + '(\s|$)')) { throw 'Missing changelog for this version' }
$taskStage = Join-Path $OutputDirectory 'package'
New-Item -ItemType Directory -Path $taskStage -Force | Out-Null
$taskExecutable = Join-Path $taskStage '小憩倒计时.exe'
& (Join-Path $taskRoot 'build.ps1') -OutputPath $taskExecutable
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
$taskMetadata = (Get-Item -LiteralPath $taskExecutable).VersionInfo
$taskExpectedFileVersion = (($taskVersion.Split('.') + @('0', '0', '0', '0')) | Select-Object -First 4) -join '.'
if ($taskMetadata.FileVersion -ne $taskExpectedFileVersion -or $taskMetadata.ProductVersion -ne $taskVersion) { throw 'Assembly version does not match VERSION' }
foreach ($taskDocument in @('README.md', 'CHANGELOG.md', 'VERSION')) { Copy-Item -LiteralPath (Join-Path $taskRoot $taskDocument) -Destination $taskStage -Force }
$taskZipName = 'LittlePause-v' + $taskVersion + '-win-x64.zip'
$taskZip = Join-Path $OutputDirectory $taskZipName
$taskFiles = @('小憩倒计时.exe', 'README.md', 'CHANGELOG.md', 'VERSION') | ForEach-Object { Join-Path $taskStage $_ }
Compress-Archive -LiteralPath $taskFiles -DestinationPath $taskZip -Force
$taskHash = (Get-FileHash -LiteralPath $taskZip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'SHA256SUMS.txt'), ($taskHash + '  ' + $taskZipName + [Environment]::NewLine), [Text.Encoding]::ASCII)
if ($env:GITHUB_OUTPUT) { 'version=' + $taskVersion | Out-File -LiteralPath $env:GITHUB_OUTPUT -Encoding utf8 -Append }
Write-Host ('Packaged: ' + $taskZipName)
