# Builds the signed release APK and copies it to the dist folder.
#
# Usage:
#   powershell -ExecutionPolicy Bypass -File .\build-release.ps1

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
Push-Location $root
try {
    if (-not $env:JAVA_HOME) {
        $jdk = 'C:\dev\jdk\jdk-21.0.1'
        if (Test-Path $jdk) {
            $env:JAVA_HOME = $jdk
        }
    }
    if ($env:JAVA_HOME) {
        $env:Path = "$env:JAVA_HOME\bin;$env:Path"
    }

    & .\gradlew.bat assembleRelease --no-daemon
    if ($LASTEXITCODE -ne 0) {
        throw "Gradle build failed with code $LASTEXITCODE"
    }

    $apk = Join-Path $root 'app\build\outputs\apk\release\app-release.apk'
    if (-not (Test-Path $apk)) {
        throw "APK not found: $apk"
    }

    $dist = Join-Path $root 'dist'
    New-Item -ItemType Directory -Force $dist | Out-Null
    $target = Join-Path $dist 'ZapretOBKHOD.apk'
    Copy-Item $apk $target -Force

    # Verify the signature.
    $buildTools = Get-ChildItem (Join-Path $env:LOCALAPPDATA 'Android\Sdk\build-tools') -Directory |
        Sort-Object Name -Descending | Select-Object -First 1
    $apksigner = Join-Path $buildTools.FullName 'apksigner.bat'
    if (Test-Path $apksigner) {
        & $apksigner verify --print-certs $target
        if ($LASTEXITCODE -ne 0) {
            throw "Signature verification failed"
        }
    } else {
        Write-Host 'apksigner not found, skipping signature check.'
    }

    $size = [math]::Round((Get-Item $target).Length / 1MB, 2)
    Write-Host "Release APK: $target ($size MB)"
} finally {
    Pop-Location
}
