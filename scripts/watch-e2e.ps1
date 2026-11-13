<#
.SYNOPSIS
    Runs the Playwright end-to-end tests in a visible browser so you can watch them.

.EXAMPLE
    pwsh scripts/watch-e2e.ps1                       # every test, slowed down
    pwsh scripts/watch-e2e.ps1 -Filter PublicBooking  # only the tests whose name contains "PublicBooking"
    pwsh scripts/watch-e2e.ps1 -SlowMo 1000           # one second per action
#>
param(
    [string] $Filter,
    [int] $SlowMo = 400
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot

# Styled pages are nicer to watch; the stylesheet is git-ignored, so build it when the CLI is there.
$tailwind = Join-Path $root 'tools/tailwindcss.exe'
if (Test-Path $tailwind) {
    & $tailwind -i (Join-Path $root 'src/Presentation/Salon.Web/Styles/app.css') -o (Join-Path $root 'src/Presentation/Salon.Web/wwwroot/css/app.css') --minify | Out-Null
}

$env:E2E_HEADED = '1'
$env:E2E_SLOWMO = "$SlowMo"
try {
    $arguments = @('test', (Join-Path $root 'tests/Salon.Tests.E2E'))
    if ($Filter) {
        $arguments += @('--filter', "FullyQualifiedName~$Filter")
    }

    & dotnet @arguments
    exit $LASTEXITCODE
}
finally {
    Remove-Item Env:E2E_HEADED, Env:E2E_SLOWMO -ErrorAction SilentlyContinue
}
