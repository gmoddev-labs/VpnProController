param([switch]$Installer, [string]$IsccPath, [string]$Version = '0.5.0')
$ErrorActionPreference = 'Stop'
$ProjectRoot = $PSScriptRoot
& dotnet run --project (Join-Path $ProjectRoot 'Tests\VpnPro.Tests.csproj') -c Release -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { throw '[VPNPro:Build] Tests failed.' }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw '[VPNPro:Build] Version must be major.minor.patch.' }
$AppOutput = Join-Path $ProjectRoot $(if ($Installer) { 'dist\FrameworkApp' } else { 'App' })
$PublishOptions = @('-c','Release','-m:2','-p:UseSharedCompilation=false',"-p:Version=$Version")
if ($Installer) { $PublishOptions += @('--self-contained','false','-p:WindowsAppSDKSelfContained=true') }
& dotnet publish (Join-Path $ProjectRoot 'Windows\VpnPro.Windows.csproj') @PublishOptions -o $AppOutput
if ($LASTEXITCODE -ne 0) { throw '[VPNPro:Build] Publish failed.' }
$RecoveryOptions = @('-c','Release','-m:2',"-p:Version=$Version",'-p:UseSharedCompilation=false')
if ($Installer) { $RecoveryOptions += @('--self-contained','false') }
& dotnet publish (Join-Path $ProjectRoot 'Recovery\VpnPro.Recovery.csproj') @RecoveryOptions -o (Join-Path $AppOutput 'Recovery')
if ($LASTEXITCODE -ne 0) { throw '[VPNPro:Build] Recovery publish failed.' }
foreach ($File in 'VpnPro.Windows.exe','VpnPro.Windows.pri','App.xbf','Recovery\VpnPro.Recovery.exe') {
    if (!(Test-Path -LiteralPath (Join-Path $AppOutput $File))) { throw "[VPNPro:Build] Missing published file: $File" }
}
if ($Installer) {
    foreach ($Directory in $AppOutput, (Join-Path $AppOutput 'Recovery')) {
        if (Test-Path (Join-Path $Directory 'coreclr.dll')) { throw '[VPNPro:Build] Shared-runtime package contains a private .NET runtime.' }
    }
    if (!$IsccPath) {
        $Candidates = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe")
        $IsccPath = $Candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    }
    if (!$IsccPath) { throw '[VPNPro:Build] Inno Setup 6.7+ required. Pass -IsccPath.' }
    & $IsccPath /Q "/DAppVersion=$Version" "/DAppSource=$AppOutput" (Join-Path $ProjectRoot 'Installer\Controller.iss')
    if ($LASTEXITCODE -ne 0) { throw '[VPNPro:Build] Installer build failed.' }
    $Setup = Get-Item (Join-Path $ProjectRoot "dist\VpnProController-$Version-Setup-x64.exe")
    $Hash = (Get-FileHash $Setup.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$Hash  $($Setup.Name)" | Set-Content (Join-Path $ProjectRoot 'dist\SHA256SUMS.txt')
    [ordered]@{ SchemaVersion=1; Version=$Version; Channel='stable'; Platform='windows-x64';
        Asset=$Setup.Name; Sha256=$Hash; Repository='gmoddev/VpnProController';
        ReleaseUrl="https://github.com/gmoddev/VpnProController/releases/tag/v$Version";
        RequiresAuthentication=$true } | ConvertTo-Json | Set-Content (Join-Path $ProjectRoot 'dist\update.json')
}
Write-Output "[VPNPro:Build] Ready: $AppOutput"
