param(
    [string]$ServiceName = "RYM Reportes Natura",
    [string]$PublishPath = "C:\Services\RymReportes",
    [int]$Port = 5085
)

$ErrorActionPreference = "Stop"

$exePath = Join-Path $PublishPath "RymReportes.Web.exe"
if (-not (Test-Path $exePath)) {
    throw "No se encontro $exePath. Publique la aplicacion antes de instalar el servicio."
}

$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService) {
    if ($existingService.Status -ne "Stopped") {
        Stop-Service -Name $ServiceName -Force
    }

    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

New-Service `
    -Name $ServiceName `
    -BinaryPathName "`"$exePath`"" `
    -DisplayName $ServiceName `
    -Description "Genera y envia reportes Natura de eventos RYM." `
    -StartupType Automatic | Out-Null

$firewallRuleName = "$ServiceName HTTP $Port"
$existingRule = Get-NetFirewallRule -DisplayName $firewallRuleName -ErrorAction SilentlyContinue
if (-not $existingRule) {
    New-NetFirewallRule `
        -DisplayName $firewallRuleName `
        -Direction Inbound `
        -Protocol TCP `
        -LocalPort $Port `
        -Action Allow | Out-Null
}

Start-Service -Name $ServiceName
Get-Service -Name $ServiceName
