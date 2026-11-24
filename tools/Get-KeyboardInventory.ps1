[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$interfaces = @(Get-PnpDevice -PresentOnly -Class Keyboard | ForEach-Object {
    $device = $_
    # The final PnP path segment is host-specific. Omit it from shareable output.
    $interfaceId = ($device.InstanceId -split '\\', 3)[0..1] -join '\'
    $interfaceId = $interfaceId -replace '(?i)_[0-9a-f]{12}(?=&COL[0-9a-f]{2}$|$)', ''
    $description = Get-PnpDeviceProperty -InstanceId $device.InstanceId `
        -KeyName DEVPKEY_Device_BusReportedDeviceDesc -ErrorAction SilentlyContinue
    [pscustomobject]@{
        interfaceId = $interfaceId
        name = $device.FriendlyName
        busDescription = $description.Data
        status = $device.Status.ToString()
    }
} | Sort-Object interfaceId, name, busDescription, status -Unique)

ConvertTo-Json -InputObject $interfaces -Depth 3
