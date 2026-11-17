# Runs JVM unit tests for the Android build.
#
# Why a copy: the Kotlin compiler cannot read classpath entries whose path
# contains the '!' character, and this project lives in such a directory.
# Tests are therefore executed from a temporary copy of the project.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File .\run-tests.ps1
#   powershell -ExecutionPolicy Bypass -File .\run-tests.ps1 -KeepCopy

param(
    [switch]$KeepCopy
)

$ErrorActionPreference = 'Stop'

$source = $PSScriptRoot
$target = Join-Path $env:TEMP 'ZapretOBKHOD-android-tests'

if (Test-Path $target) {
    Remove-Item $target -Recurse -Force
}
New-Item -ItemType Directory -Force $target | Out-Null

# Copy sources and build configuration only.
$robocopyArgs = @($source, $target, '/E', '/NFL', '/NDL', '/NJH', '/NJS',
    '/XD', 'build', '.gradle', '.idea', '.kotlin',
    '/XF', '*.apk', '*.aab')
& robocopy @robocopyArgs | Out-Null
if ($LASTEXITCODE -ge 8) {
    throw "robocopy failed with code $LASTEXITCODE"
}

if (-not $env:JAVA_HOME) {
    $jdk = 'C:\dev\jdk\jdk-21.0.1'
    if (Test-Path $jdk) {
        $env:JAVA_HOME = $jdk
    }
}
if ($env:JAVA_HOME) {
    $env:Path = "$env:JAVA_HOME\bin;$env:Path"
}

Push-Location $target
try {
    & .\gradlew.bat testDebugUnitTest --no-daemon
    $code = $LASTEXITCODE
} finally {
    Pop-Location
}

if (-not $KeepCopy) {
    Remove-Item $target -Recurse -Force -ErrorAction SilentlyContinue
}

if ($code -ne 0) {
    Write-Host "Tests FAILED (exit code $code)." -ForegroundColor Red
    exit $code
}

Write-Host 'All unit tests passed.' -ForegroundColor Green
