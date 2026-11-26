[CmdletBinding()]
param([switch]$Test, [switch]$Live)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$msbuild = $null
if (Test-Path $vswhere) {
    $msbuild = & $vswhere -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\Current\Bin\MSBuild.exe' |
        Where-Object { Test-Path (Join-Path (Split-Path $_) 'Roslyn\Microsoft.CSharp.Core.targets') } | Select-Object -First 1
}
if (-not $msbuild) {
    $msbuild = Get-ChildItem "$env:ProgramFiles\Microsoft Visual Studio\*\*\MSBuild\Current\Bin\MSBuild.exe", "${env:ProgramFiles(x86)}\Microsoft Visual Studio\*\*\MSBuild\Current\Bin\MSBuild.exe" -ErrorAction SilentlyContinue |
        Where-Object { Test-Path (Join-Path $_.DirectoryName 'Roslyn\Microsoft.CSharp.Core.targets') } | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $msbuild) { throw 'MSBuild with the C# compiler was not found. Install Visual Studio 2022 Build Tools (.NET desktop workload).' }
$winmd = Get-ChildItem (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\UnionMetadata') -Recurse -Filter Windows.winmd | Where-Object { $_.FullName -notmatch '\\Facade\\' } | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $winmd) { throw 'Windows SDK Windows.winmd was not found.' }
Write-Host "Using MSBuild: $msbuild"
Write-Host "Using Windows.winmd: $($winmd.FullName)"
& $msbuild (Join-Path $root 'src\BlueGauge\BlueGauge.csproj') /t:Rebuild /p:Configuration=Release /p:WindowsWinMd="$($winmd.FullName)" /v:minimal
if ($LASTEXITCODE) { throw "Build failed with exit code $LASTEXITCODE" }
New-Item -ItemType Directory -Force (Join-Path $root 'artifacts') | Out-Null
Copy-Item (Join-Path $root 'src\BlueGauge\bin\Release\BlueGauge.exe') (Join-Path $root 'artifacts\BlueGauge.exe') -Force
Write-Host "Built artifacts\BlueGauge.exe"
if ($Test -or $Live) {
    & $msbuild (Join-Path $root 'tests\BlueGauge.Tests\BlueGauge.Tests.csproj') /t:Rebuild /p:Configuration=Release /v:minimal
    if ($LASTEXITCODE) { throw "Test build failed with exit code $LASTEXITCODE" }
    $testExe = Join-Path $root 'tests\BlueGauge.Tests\bin\Release\BlueGauge.Tests.exe'
    if ($Live) { & $testExe --live } else { & $testExe }
    if ($LASTEXITCODE) { throw "Tests failed with exit code $LASTEXITCODE" }
}
