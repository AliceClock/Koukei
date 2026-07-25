[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",

    [switch]$SkipUiAutomation
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot

function Invoke-TestCommand {
    param(
        [Parameter(Mandatory)]
        [string[]]$Arguments
    )

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

Push-Location $repositoryRoot
try {
    Write-Host "Running core tests..."
    Invoke-TestCommand @(
        "test",
        "tests/Koukei.Core.Tests/Koukei.Core.Tests.csproj",
        "-c", $Configuration,
        "--nologo",
        "--verbosity:minimal"
    )

    Write-Host "Running UI contract tests..."
    Invoke-TestCommand @(
        "test",
        "tests/Koukei.UI.Tests/Koukei.UI.Tests.csproj",
        "-m:1",
        "-c", $Configuration,
        "-p:Platform=x64",
        "-p:WindowsPackageType=None",
        "-p:WindowsAppSDKSelfContained=true",
        "--filter", "Category!=UIAutomation",
        "--nologo",
        "--verbosity:minimal"
    )

    if (-not $SkipUiAutomation) {
        Write-Host "Running interactive UI navigation smoke test..."
        Invoke-TestCommand @(
            "test",
            "tests/Koukei.UI.Tests/Koukei.UI.Tests.csproj",
            "-c", $Configuration,
            "-p:Platform=x64",
            "--no-build",
            "--no-restore",
            "--filter", "Category=UIAutomation",
            "--nologo",
            "--verbosity:minimal"
        )
    }
}
finally {
    Pop-Location
}
