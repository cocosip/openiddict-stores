# Sharp.OpenIddict.Stores — local verification gates (CI-01)
#
# Runs every check that can be executed without a database server: restore, Release build for all five
# target frameworks, unit and integration tests (SQLite runs for real, the server suites skip honestly), the
# package asset checks, and the SQL manifest/digest checks. Every step fails the script, so a gate cannot be
# "passed" by swallowing an exit code.
#
# Deliberately not here: any database server. Installing or connecting to one is out of scope by the user's
# instruction, so the server-backed acceptance stays in CI-02 on a dedicated environment.
#
# Usage:  pwsh ./scripts/verify.ps1 [-SkipPack]
#
# No parameters are required. The script is safe to run repeatedly.

[CmdletBinding()]
param(
    [switch] $SkipPack
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'Sharp.OpenIddict.Stores.slnx'
$artifacts = Join-Path $root 'artifacts'

$steps = [System.Collections.Generic.List[object]]::new()

function Invoke-Step {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [scriptblock] $Action
    )

    Write-Host ''
    Write-Host "=== $Name ===" -ForegroundColor Cyan

    $started = Get-Date

    # Any non-zero exit inside the script block terminates the script: no gate may be skipped silently, and
    # the caller's exit code must reflect the failure.
    & $Action

    if ($LASTEXITCODE -ne 0 -and $null -ne $LASTEXITCODE) {
        throw "$Name failed with exit code $LASTEXITCODE."
    }

    $steps.Add([pscustomobject]@{
        Name     = $Name
        Duration = ((Get-Date) - $started).TotalSeconds
    })
}

Push-Location $root
try {
    Invoke-Step 'Restore' {
        dotnet restore $solution
    }

    Invoke-Step 'Build (Release, all target frameworks, no incremental)' {
        # --no-incremental matters: a stale bin/ produced a false positive during development, and a gate that
        # can report success from stale output is worse than no gate.
        dotnet build $solution -c Release --no-incremental
    }

    # Pack before testing: the package asset tests read artifacts/ during the test run, so on a clean
    # checkout (or after any packaging change) the tests must see the packages built from this exact tree.
    # The old order - tests first, pack after - fed the tests whatever stale nupkgs happened to be present.
    if (-not $SkipPack) {
        Invoke-Step 'Pack (eight packages, local verification only)' {
            if (Test-Path $artifacts) {
                Remove-Item $artifacts -Recurse -Force
            }

            dotnet pack $solution -c Release --no-build -o $artifacts
        }

        Invoke-Step 'Package contents' {
            # The eight-pack shape, the SQL resources, and the absence of driver dependencies are asserted
            # by tests; this only checks that packing actually produced them.
            $packages = @(Get-ChildItem (Join-Path $artifacts '*.nupkg') -ErrorAction Stop)

            if ($packages.Count -ne 8) {
                throw "Expected 8 packages, found $($packages.Count)."
            }

            Write-Host "  $($packages.Count) packages packed."
            $packages | ForEach-Object { Write-Host "    $($_.Name)" }
        }
    }

    Invoke-Step 'Tests (unit + integration; SQLite runs, server suites skip)' {
        dotnet test $solution -c Release --no-build
    }

    Write-Host ''
    Write-Host 'All gates passed.' -ForegroundColor Green
}
finally {
    Pop-Location

    if ($steps.Count -gt 0) {
        Write-Host ''
        Write-Host 'Timings:' -ForegroundColor DarkGray
        $steps | ForEach-Object { Write-Host ("  {0,-62} {1,6:N1}s" -f $_.Name, $_.Duration) -ForegroundColor DarkGray }
    }
}
