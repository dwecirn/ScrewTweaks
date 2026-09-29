# ScrewTweaks release helper.
#
# The suite ships several independently versioned plugins from one repository, so a release is a
# *bundle*, not a single version number. This script answers the only question that is actually
# tedious: which plugins changed since the last release, and therefore which versions need bumping.
#
#   pwsh tools/release.ps1                 # report
#   pwsh tools/release.ps1 -Package        # report + build and zip the bundle
#   pwsh tools/release.ps1 -Since <ref>    # compare against a specific ref
#
# Versioning policy (see README):
#   - a plugin's version lives in its own .csproj and only moves when that plugin changes
#   - bump minor for new settings/features, patch for fixes, major for public API changes
#   - the git tag is a date, and identifies the bundle rather than any component's version

[CmdletBinding()]
param(
    # Git ref to compare against. Defaults to the most recent tag, if there is one.
    [string]$Since = '',

    # Also build in Release and produce dist/ScrewTweaks-<date>.zip.
    [switch]$Package
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    # ---------------------------------------------------------------- components
    $projects = Get-ChildItem 'src' -Directory | Where-Object { Test-Path "$($_.FullName)\$($_.Name).csproj" }

    function Get-Version([string]$csproj) {
        [xml]$xml = Get-Content $csproj
        $v = @($xml.Project.PropertyGroup) | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1
        if ($v) { $v } else { '0.0.0' }
    }

    # ---------------------------------------------------------------- comparison base
    $sinceLabel = $Since
    if (-not $Since) {
        $lastTag = git describe --tags --abbrev=0 2>$null
        if ($LASTEXITCODE -eq 0 -and $lastTag) {
            $Since = $lastTag
            $sinceLabel = "$lastTag (most recent tag)"
        }
        else {
            $Since = ''
            $sinceLabel = '(no previous release tag - first release)'
        }
    }

    $dateTag = Get-Date -Format 'yyyy.MM.dd'

    Write-Host ''
    Write-Host 'ScrewTweaks release check' -ForegroundColor Cyan
    Write-Host "  comparing against : $sinceLabel"
    Write-Host "  date tag would be : $dateTag"
    Write-Host ''

    # ---------------------------------------------------------------- report
    $rows = foreach ($p in $projects) {
        $changed = $false
        if ($Since) {
            $diff = git diff --name-only "$Since..HEAD" -- "src/$($p.Name)"
            $changed = [bool]($diff | Where-Object { $_ })
        }
        else {
            $changed = $true   # first release: everything is new
        }

        [pscustomobject]@{
            Component = $p.Name
            Version   = Get-Version "$($p.FullName)\$($p.Name).csproj"
            Changed   = if ($changed) { 'changed' } else { '-' }
        }
    }

    $rows | Format-Table -AutoSize | Out-String | Write-Host

    $changedNames = @($rows | Where-Object { $_.Changed -eq 'changed' } | ForEach-Object { $_.Component })
    if ($changedNames.Count -eq 0) {
        Write-Host 'Nothing changed since the last release.' -ForegroundColor Yellow
    }
    else {
        Write-Host "Changed: $($changedNames -join ', ')" -ForegroundColor Green
        Write-Host '  -> bump only those components.'
        Write-Host '  -> describe the changes in the GitHub release notes (no CHANGELOG in the repo).'
        Write-Host '  -> if a public API changed (PanelHost.Register, ITireModel, IBrakeAid, IDriveAid),'
        Write-Host '     bump major and state the minimum in the [BepInDependency] attributes.'
    }

    # ---------------------------------------------------------------- package
    if ($Package) {
        $stage = "dist\ScrewTweaks-$dateTag"
        $zip = "dist\ScrewTweaks-$dateTag.zip"
        Remove-Item 'dist' -Recurse -Force -ErrorAction SilentlyContinue
        New-Item -ItemType Directory -Force -Path "$stage\plugins" | Out-Null

        Write-Host ''
        Write-Host 'Building Release...' -ForegroundColor Cyan
        dotnet build ScrewTweaks.sln -c Release --no-restore -v minimal -m:1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'build failed' }

        foreach ($p in $projects) {
            $dll = "src\$($p.Name)\bin\Release\$($p.Name).dll"
            if (-not (Test-Path $dll)) { throw "missing $dll" }
            Copy-Item $dll "$stage\plugins\" -Force
        }

        foreach ($doc in 'LICENSE', 'README.md', 'README.zh-CN.md', 'README.ja.md') {
            if (Test-Path $doc) { Copy-Item $doc $stage -Force }
        }

        Compress-Archive -Path $stage -DestinationPath $zip -Force
        Write-Host "Packaged -> $zip" -ForegroundColor Green
        Write-Host '  (the bundle carries each plugin at its own version; see the table above)'
    }
}
finally {
    Pop-Location
}
