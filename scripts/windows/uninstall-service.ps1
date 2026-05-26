param(
    [string]$ServiceName = "RYM Reportes Natura",
    [int]$Port = 5085
)

$ErrorActionPreference = "Stop"

$existingService = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existingService) {
    if ($existingService.Status -ne "Stopped") {
        Stop-Service -Name $ServiceName -Force
    }

    sc.exe delete $ServiceName | Out-Null
}

$firewallRuleName = "$ServiceName HTTP $Port"
$existingRule = Get-NetFirewallRule -DisplayName $firewallRuleName -ErrorAction SilentlyContinue
if ($existingRule) {
    Remove-NetFirewallRule -DisplayName $firewallRuleName
}
