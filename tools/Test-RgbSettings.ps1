[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    New-Item -ItemType Directory -Force obj | Out-Null
    & "$env:SystemRoot\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /platform:x64 `
        /out:obj\RgbSettingsTests.exe src\HidDevice.cs src\RgbSettings.cs tests\RgbSettingsTests.cs
    if ($LASTEXITCODE -ne 0) { throw 'RGB regression test build failed.' }
    & .\obj\RgbSettingsTests.exe
    if ($LASTEXITCODE -ne 0) { throw 'RGB regression checks failed.' }
}
finally { Pop-Location }
