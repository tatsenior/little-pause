param([string]$OutputPath = (Join-Path $PSScriptRoot '小憩倒计时.exe'), [switch]$IncludeTests)
$ErrorActionPreference = 'Stop'
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
$references = @('System.dll', 'System.Core.dll', 'System.Runtime.Serialization.dll', 'System.Drawing.dll', 'System.Windows.Forms.dll', 'WPF\WindowsBase.dll', 'WPF\PresentationCore.dll', 'WPF\PresentationFramework.dll', 'System.Xaml.dll')
$arguments = @('/nologo', '/target:winexe', '/platform:x64', '/optimize+', '/utf8output', ('/out:' + $OutputPath))
$arguments += '/win32icon:' + (Join-Path $PSScriptRoot 'assets\clock.ico')
foreach ($reference in $references) { $arguments += '/reference:' + (Join-Path $framework $reference) }
$arguments += Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName }
if ($IncludeTests) {
    $arguments += '/define:DIAGNOSTICS'
    $arguments += Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'tests') -Filter '*.cs' | ForEach-Object { $_.FullName }
}
& $compiler $arguments
if ($LASTEXITCODE -ne 0) { throw '编译失败' }
Write-Host ('Built: ' + $OutputPath)
