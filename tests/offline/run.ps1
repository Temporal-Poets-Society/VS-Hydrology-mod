param([switch] $Baseline)
$ErrorActionPreference = 'Stop'
$projectRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '../..')).Path
$runRoot = Join-Path $projectRoot 'testruns/issue20'
New-Item -ItemType Directory -Force -Path $runRoot | Out-Null
$names = @('APPDATA', 'DOTNET_CLI_HOME', 'NUGET_PACKAGES', 'DOTNET_CLI_TELEMETRY_OPTOUT', 'DOTNET_GENERATE_ASPNET_CERTIFICATE')
$saved = @{}
foreach ($name in $names) { $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
try {
    # No user NuGet settings or caches are needed: this project has no package dependencies.
    $env:APPDATA = Join-Path $runRoot 'appdata'
    $env:DOTNET_CLI_HOME = Join-Path $runRoot 'dotnet'
    $env:NUGET_PACKAGES = Join-Path $runRoot 'packages'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    $config = Join-Path $runRoot 'NuGet.Config'
    Set-Content -LiteralPath $config -Value '<configuration><packageSources><clear /></packageSources></configuration>'
    $project = Join-Path $PSScriptRoot 'Issue20.csproj'
    & dotnet restore $project --configfile $config
    if ($LASTEXITCODE -ne 0) { throw 'Offline restore failed' }
    $extra = @()
    if ($Baseline) {
        # Read the installed playtest source without checking out or editing another worktree.
        $source = & git -C $projectRoot show '37d7dad:src/Rules/H2Filling.cs'
        if ($LASTEXITCODE -ne 0) { throw 'Cannot read 0.1.11 baseline' }
        $baselineSource = Join-Path $runRoot 'H2-baseline.cs'
        Set-Content -LiteralPath $baselineSource -Value $source
        # BASELINE leaves out the suite's cases for rules 0.1.11 does not have (pockets, barriers, held levels)
        $extra = @("-p:FillingSource=$baselineSource", '-p:DefineConstants=BASELINE')
    }
    & dotnet build $project -c Release --no-restore @extra
    if ($LASTEXITCODE -ne 0) { throw 'Offline build failed' }
    & dotnet (Join-Path $PSScriptRoot 'bin/Release/net8.0/Issue20.dll')
    $testExit = $LASTEXITCODE
    if ($Baseline) {
        if ($testExit -ne 1) { throw 'Expected baseline regression failures (exit 1)' }
        Write-Output 'Baseline fails as expected. Run without -Baseline to rebuild and check the fix.'
    } elseif ($testExit -ne 0) { throw "Offline tests failed (exit $testExit)" }
} finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process') }
}
