[CmdletBinding()]
param([switch]$SkipFrontend)

$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $root
try {
    if (!$SkipFrontend) {
        & npm.cmd --prefix frontend ci --no-audit --no-fund
        if ($LASTEXITCODE -ne 0) { throw 'Frontend dependencies failed to restore.' }
        & npm.cmd --prefix frontend run build
        if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed.' }
    }
    if (!(Test-Path 'frontend\dist\index.html')) { throw 'Vue output is missing.' }

    $webview = & (Join-Path $PSScriptRoot 'Restore-WebView2.ps1')
    $core = Join-Path $webview 'lib\net462\Microsoft.Web.WebView2.Core.dll'
    $winforms = Join-Path $webview 'lib\net462\Microsoft.Web.WebView2.WinForms.dll'
    $audio = & (Join-Path $PSScriptRoot 'Restore-Audio.ps1')
    $audioCore = Join-Path $audio 'naudio.core\lib\netstandard2.0\NAudio.Core.dll'
    $audioWasapi = Join-Path $audio 'naudio.wasapi\lib\netstandard2.0\NAudio.Wasapi.dll'
    $framework = "$env:SystemRoot\Microsoft.NET\Framework64\v4.0.30319"
    $output = Join-Path $root 'bin'
    New-Item -ItemType Directory -Force $output | Out-Null
    $exe = Join-Path $output 'Caraxes-Pro-HUB.exe'
    $sources = @(Get-ChildItem 'src' -Filter '*.cs' -File | ForEach-Object FullName)
    & "$framework\csc.exe" /nologo /platform:x64 /target:winexe `
        "/out:$exe" /win32icon:assets\workspace.ico /reference:System.Windows.Forms.dll /reference:System.Drawing.dll `
        /reference:System.Web.Extensions.dll "/reference:$core" "/reference:$winforms" `
        "/reference:$framework\netstandard.dll" "/reference:$audioCore" "/reference:$audioWasapi" @sources
    if ($LASTEXITCODE -ne 0) { throw 'C# host build failed.' }

    Copy-Item -LiteralPath $core, $winforms, $audioCore, $audioWasapi, (Join-Path $webview 'runtimes\win-x64\native\WebView2Loader.dll') -Destination $output -Force
    Copy-Item -LiteralPath (Join-Path $audio 'LICENSE.txt') -Destination (Join-Path $output 'NAudio-LICENSE.txt') -Force
    Copy-Item -LiteralPath (Join-Path $webview 'LICENSE.txt') -Destination (Join-Path $output 'WebView2-LICENSE.txt') -Force
    Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $output 'LICENSE.txt') -Force
    New-Item -ItemType Directory -Force (Join-Path $output 'ui') | Out-Null
    Copy-Item -Path 'frontend\dist\*' -Destination (Join-Path $output 'ui') -Recurse -Force
    Write-Output $exe
}
finally { Pop-Location }
