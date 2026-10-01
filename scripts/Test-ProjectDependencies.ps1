param([string]$DataDirectory=(Join-Path $env:LOCALAPPDATA 'VSTHub'),[string]$OutputPath)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
$settings=Get-Content -LiteralPath (Join-Path $DataDirectory 'settings.json') -Raw | ConvertFrom-Json
$manifest=Get-Content -LiteralPath (Join-Path $taskRoot 'hardware\ni-pxie-5644r\manifest.json') -Raw | ConvertFrom-Json
$rows=[Collections.Generic.List[object]]::new()
function CheckFile([string]$name,[string]$category,[string]$path,[string]$expectedHash='',[bool]$required=$true){
 $exists=Test-Path -LiteralPath $path -PathType Leaf
 $hash=if($exists -and $expectedHash){(Get-FileHash -LiteralPath $path).Hash.ToLower()}else{$null}
 $version=if($exists){(Get-Item -LiteralPath $path).VersionInfo.FileVersion}else{$null}
 $script:rows.Add([pscustomobject]@{name=$name;category=$category;path=$path;required=$required;exists=$exists;file_version=$version;sha256=$hash;pass=($exists -and (-not $expectedHash -or $hash -eq $expectedHash))})
}
CheckFile 'Selected TX waveform' 'project asset' $settings.tx.waveform_path
if([IO.Path]::GetExtension($settings.tx.waveform_path) -eq '.cs16'){
 $sidecar=[IO.Path]::ChangeExtension($settings.tx.waveform_path,'.json')
 CheckFile 'TX waveform metadata' 'project asset' $sidecar
 $waveformMeta=Get-Content -LiteralPath $sidecar -Raw | ConvertFrom-Json
 CheckFile 'TX waveform hash' 'project asset' $settings.tx.waveform_path $waveformMeta.sha256
}
CheckFile 'Archived FPGA bitfile' 'project asset' $settings.bitfile_path $manifest.sha256
CheckFile 'Driver-installed named FPGA bitfile' 'NI installation dependency' $manifest.installed_source $manifest.sha256
CheckFile 'NI-RFSA' 'NI installation dependency' (Join-Path $env:ProgramFiles 'IVI Foundation\IVI\Bin\niRFSA_64.dll')
CheckFile 'NI-RFSG' 'NI installation dependency' (Join-Path $env:ProgramFiles 'IVI Foundation\IVI\Bin\niRFSG_64.dll')
CheckFile 'NI FPGA runtime' 'NI installation dependency' (Join-Path $env:SystemRoot 'System32\NiFpga.dll')
CheckFile 'GNU Radio Companion' 'installed client' $settings.gnu_radio_path '' $false
CheckFile 'GQRX' 'installed client' $settings.gqrx_path '' $false
$radioLibrary=Split-Path -Parent (Split-Path -Parent $settings.gqrx_path)
CheckFile 'SoapySDR runtime' 'installed client' (Join-Path $radioLibrary 'bin\SoapySDR.dll')
$nativeHash=(Get-FileHash -LiteralPath (Join-Path $taskRoot 'soapy-vst\build-vst\vstSupport.dll')).Hash.ToLower()
CheckFile 'Installed VST plugin' 'project plugin installed in client' (Join-Path $radioLibrary 'lib\SoapySDR\modules0.8\vstSupport.dll') $nativeHash
$assetsLocal=($settings.tx.waveform_path.StartsWith((Join-Path $taskRoot 'waveform')+'\',[StringComparison]::OrdinalIgnoreCase) -and $settings.bitfile_path.StartsWith((Join-Path $taskRoot 'hardware')+'\',[StringComparison]::OrdinalIgnoreCase))
$allPass=(@($rows | Where-Object {$_.required -and -not $_.pass}).Count -eq 0 -and $assetsLocal)
$result=[ordered]@{status=$(if($allPass){'PASS'}else{'FAIL'});project_assets_local=$assetsLocal;items=$rows;note='NI driver initialization still selects the installed bitfile by filename; keep its vendor-installed copy. Generated settings/logs stay in LOCALAPPDATA.'}
$json=$result | ConvertTo-Json -Depth 8
if($OutputPath){$json | Set-Content -Encoding utf8 -LiteralPath $OutputPath}
$json
if(-not $allPass){throw 'Dependency audit failed'}
