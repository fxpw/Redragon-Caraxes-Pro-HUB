[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    New-Item -ItemType Directory -Force obj | Out-Null
    & "$env:SystemRoot\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /platform:x64 `
        /out:obj\WorkspaceTests.exe src\HidDevice.cs src\CanvasFrame.cs src\PhysicalKeys.cs src\LayerRemap.cs tests\WorkspaceTests.cs
    if ($LASTEXITCODE -ne 0) { throw 'Workspace test build failed.' }
    & .\obj\WorkspaceTests.exe
    if ($LASTEXITCODE -ne 0) { throw 'Workspace checks failed.' }
}
finally { Pop-Location }
