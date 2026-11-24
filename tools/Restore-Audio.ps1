$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\.deps\audio'))
$version = '2.2.1'
$packages = @{
    'naudio.core' = '794645DBFD30E4880663D52D9D9224D55C301FA54228FF623C95D572C7887347'
    'naudio.wasapi' = 'C8396B8DBAB86A2619EDFD9158BEAF18C21CB86677099B5594950FD8651B9D1A'
}
New-Item -ItemType Directory -Force $root | Out-Null
function Restore-File([string]$Uri, [string]$Path, [string]$Hash) {
    if (!(Test-Path -LiteralPath $Path)) {
        & curl.exe --fail --silent --show-error --location $Uri -o "$Path.download"
        if ($LASTEXITCODE -ne 0 -or (Get-FileHash -LiteralPath "$Path.download").Hash -ne $Hash) {
            throw 'Could not verify the audio dependency download.'
        }
        Move-Item -LiteralPath "$Path.download" -Destination $Path
    }
    if ((Get-FileHash -LiteralPath $Path).Hash -ne $Hash) { throw 'Audio dependency hash mismatch.' }
}

foreach ($id in @('naudio.core', 'naudio.wasapi')) {
    $archive = Join-Path $root "$id.$version.zip"
    Restore-File "https://api.nuget.org/v3-flatcontainer/$id/$version/$id.$version.nupkg" $archive $packages[$id]
    $folder = Join-Path $root $id
    if (!(Test-Path -LiteralPath (Join-Path $folder 'lib\netstandard2.0'))) {
        Expand-Archive -LiteralPath $archive -DestinationPath $folder -Force
    }
}
Restore-File "https://raw.githubusercontent.com/naudio/NAudio/v$version/license.txt" (Join-Path $root 'LICENSE.txt') `
    '303E01786B271EB464B3F43D18A375F2F17D37BC708A37C2FDF45B289FB5BFAA'
Write-Output $root
