<#
.SYNOPSIS
    Enforces the per-project line-coverage gates from spec §9 (Domain >= 90%, Api >= 70%).

.DESCRIPTION
    Runs the test suite with the coverlet collector, then merges every Cobertura
    report it produced.

    Merging is per (assembly, absolute source path, line) and a line counts as
    covered if ANY report hit it. Both test projects emit a report covering the
    same assemblies, so adding the reports' counters together would count every
    line twice and score a line exercised by only one project as half covered.

    Class filenames are relative to that report's own <sources> root, and the
    two reports do not share a root - one is rooted at src\, the other at
    src\WeGo.Domain\. Resolving each filename against its report's root is what
    makes "the same line" comparable between them.
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Debug',
    [switch]$SkipTestRun
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$resultsDirectory = Join-Path $repositoryRoot 'artifacts/coverage'

# Line-coverage floors per assembly. Anything not listed is reported but not gated.
$gates = @{
    'WeGo.Domain' = 90.0
    'WeGo.Api'    = 70.0
}

if (-not $SkipTestRun) {
    if (Test-Path $resultsDirectory) {
        Remove-Item $resultsDirectory -Recurse -Force
    }

    Write-Host 'Running tests with coverage...' -ForegroundColor Cyan
    dotnet test (Join-Path $repositoryRoot 'WeGo.sln') `
        --configuration $Configuration `
        --settings (Join-Path $repositoryRoot 'coverlet.runsettings') `
        --results-directory $resultsDirectory `
        --collect:'XPlat Code Coverage' `
        --nologo -v q

    if ($LASTEXITCODE -ne 0) {
        Write-Host 'Tests failed; coverage gate not evaluated.' -ForegroundColor Red
        exit $LASTEXITCODE
    }
}

$reports = @(Get-ChildItem -Path $resultsDirectory -Filter 'coverage.cobertura.xml' -Recurse -ErrorAction SilentlyContinue)
if ($reports.Count -eq 0) {
    Write-Host "No coverage reports found under $resultsDirectory." -ForegroundColor Red
    exit 1
}

# hits[assembly]["<absolute source path>:line"] = $true once any report hit it.
$hits = @{}

function Resolve-SourcePath {
    param([string[]]$Roots, [string]$RelativePath)

    foreach ($root in $Roots) {
        $candidate = Join-Path $root $RelativePath
        if (Test-Path -LiteralPath $candidate) {
            return (Resolve-Path -LiteralPath $candidate).Path.ToLowerInvariant()
        }
    }

    # Unresolvable (source moved or deleted): fall back to the raw relative path
    # so the line is still counted rather than silently dropped.
    return $RelativePath.ToLowerInvariant()
}

foreach ($report in $reports) {
    $document = New-Object System.Xml.XmlDocument
    $document.Load($report.FullName)

    $roots = @($document.SelectNodes('/coverage/sources/source') | ForEach-Object { $_.InnerText })

    foreach ($package in $document.SelectNodes('/coverage/packages/package')) {
        $assembly = $package.GetAttribute('name')
        if (-not $hits.ContainsKey($assembly)) {
            $hits[$assembly] = @{}
        }

        foreach ($class in $package.SelectNodes('classes/class')) {
            $sourcePath = Resolve-SourcePath -Roots $roots -RelativePath $class.GetAttribute('filename')

            foreach ($line in $class.SelectNodes('lines/line')) {
                $key = '{0}:{1}' -f $sourcePath, $line.GetAttribute('number')
                $wasHit = [int]$line.GetAttribute('hits') -gt 0

                if (-not $hits[$assembly].ContainsKey($key)) {
                    $hits[$assembly][$key] = $wasHit
                }
                elseif ($wasHit) {
                    $hits[$assembly][$key] = $true
                }
            }
        }
    }
}

$totals = @{}
foreach ($assembly in $hits.Keys) {
    $lines = $hits[$assembly]
    $covered = @($lines.Values | Where-Object { $_ }).Count
    $totals[$assembly] = @{ Covered = $covered; Total = $lines.Count }
}

Write-Host ''
Write-Host 'Line coverage' -ForegroundColor Cyan
Write-Host '-------------'

$failed = $false

foreach ($assembly in ($totals.Keys | Sort-Object)) {
    $covered = $totals[$assembly].Covered
    $total = $totals[$assembly].Total
    if ($total -eq 0) { continue }

    $percent = [math]::Round(100.0 * $covered / $total, 2)

    if ($gates.ContainsKey($assembly)) {
        $gate = $gates[$assembly]
        if ($percent -lt $gate) {
            Write-Host ("  {0,-22} {1,6}%  ({2}/{3})  FAIL (gate {4}%)" -f $assembly, $percent, $covered, $total, $gate) -ForegroundColor Red
            $failed = $true
        }
        else {
            Write-Host ("  {0,-22} {1,6}%  ({2}/{3})  ok   (gate {4}%)" -f $assembly, $percent, $covered, $total, $gate) -ForegroundColor Green
        }
    }
    else {
        Write-Host ("  {0,-22} {1,6}%  ({2}/{3})  (not gated)" -f $assembly, $percent, $covered, $total)
    }
}

foreach ($assembly in $gates.Keys) {
    if (-not $totals.ContainsKey($assembly)) {
        Write-Host "  $assembly produced no coverage data - gate cannot be evaluated." -ForegroundColor Red
        $failed = $true
    }
}

Write-Host ''
if ($failed) {
    Write-Host 'Coverage gate FAILED.' -ForegroundColor Red
    exit 1
}

Write-Host 'Coverage gate passed.' -ForegroundColor Green
