[CmdletBinding()]
param(
    [switch]$Test,
    [switch]$Live,
    [ValidateSet('win-x64', 'win-arm64')][string]$Runtime = 'win-x64',
    [string]$OutputDirectory
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

    $destination = if (!$OutputDirectory) { Join-Path $root "artifacts/$Runtime" }
        elseif ([IO.Path]::IsPathRooted($OutputDirectory)) { [IO.Path]::GetFullPath($OutputDirectory) }
        else { [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory)) }
    & dotnet publish src/BlueGauge/BlueGauge.csproj -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $destination
    if ($LASTEXITCODE -ne 0) { throw "Publish failed (exit $LASTEXITCODE). Close BlueGauge before rebuilding if the executable is in use." }
    $exe = Join-Path $destination 'BlueGauge.exe'
    if (($Test -or $Live) -and $Runtime -eq "win-$(([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture).ToString().ToLowerInvariant())") {
        $report = Join-Path ([IO.Path]::GetTempPath()) ('BlueGauge-taskbar-' + [guid]::NewGuid() + '.txt')
        try {
            $process = Start-Process -FilePath $exe -ArgumentList "--taskbar-diagnostic `"$report`"" -PassThru
            if (-not $process.WaitForExit(30000)) { Stop-Process -Id $process.Id; throw 'Portable taskbar diagnostic timed out.' }
            if (-not (Test-Path -LiteralPath $report)) { throw "Portable taskbar diagnostic produced no report ($($process.ExitCode))." }
            Get-Content -LiteralPath $report
            if ($process.ExitCode -ne 0) { throw 'Portable taskbar diagnostic failed.' }
        } finally { Remove-Item -LiteralPath $report -ErrorAction SilentlyContinue }
    }
    Write-Host "Built $exe"
} finally { Pop-Location }
