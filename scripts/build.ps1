[CmdletBinding()]
param(
    [switch]$Test,
    [switch]$Live,
    [ValidateSet('win-x64', 'win-arm64')][string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $sdk = & dotnet --version
    if ($LASTEXITCODE -ne 0 -or $sdk -notmatch '^11\.') { throw 'Install the .NET 11 SDK; see global.json.' }
    Write-Host "Using .NET SDK $sdk"
    if ($Test -or $Live) {
        $testArgs = @('run', '--project', 'tests/BlueGauge.Tests/BlueGauge.Tests.csproj', '-c', 'Release')
        if ($Live) { $testArgs += @('--', '--live') }
        & dotnet @testArgs
        if ($LASTEXITCODE -ne 0) { throw "Tests failed (exit $LASTEXITCODE)." }
    }

    $destination = Join-Path $root "artifacts/$Runtime"
    & dotnet publish src/BlueGauge/BlueGauge.csproj -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o $destination
    if ($LASTEXITCODE -ne 0) { throw "Publish failed (exit $LASTEXITCODE). Close BlueGauge before rebuilding if the executable is in use." }
    Write-Host "Built artifacts/$Runtime/BlueGauge.exe"
} finally { Pop-Location }
