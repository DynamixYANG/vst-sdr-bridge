param([string]$Waveform='waveform\nr-tm3.1a-fdd-4x20mhz-120msps.tdms',[string]$DataDirectory=(Join-Path $env:LOCALAPPDATA 'VSTHub'))
$ErrorActionPreference='Stop'
if(Get-Process VSTHub -ErrorAction SilentlyContinue){throw 'Close VSTHub before updating its saved settings'}
$taskRoot=Split-Path -Parent $PSScriptRoot
$path=if([IO.Path]::IsPathRooted($Waveform)){$Waveform}else{Join-Path $taskRoot $Waveform}
$path=(Resolve-Path -LiteralPath $path -ErrorAction Stop).Path
$waveformRoot=(Resolve-Path -LiteralPath (Join-Path $taskRoot 'waveform')).Path
if(-not $path.StartsWith($waveformRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Choose a waveform inside this project'}
if([IO.Path]::GetExtension($path) -eq '.cs16'){
 $metadata=Get-Content -LiteralPath ([IO.Path]::ChangeExtension($path,'.json')) -Raw | ConvertFrom-Json
 if($metadata.format -ne 'CS16_LE_IQ' -or $metadata.sha256 -ne (Get-FileHash -LiteralPath $path).Hash.ToLower() -or $metadata.samples*4 -ne (Get-Item -LiteralPath $path).Length){throw 'CS16 metadata/hash mismatch'}
}elseif([IO.Path]::GetExtension($path) -notin @('.tdms','.tmds')){throw 'Choose TDMS or CS16 IQ'}
$bitfile=Join-Path $taskRoot 'hardware\ni-pxie-5644r\local\NI Streaming for VST.lvbitx'
$manifest=Get-Content -LiteralPath (Join-Path $taskRoot 'hardware\ni-pxie-5644r\manifest.json') -Raw | ConvertFrom-Json
if((Get-FileHash -LiteralPath $bitfile).Hash.ToLower() -ne $manifest.sha256){throw 'Import the recorded NI bitfile first'}
$settingsPath=Join-Path $DataDirectory 'settings.json'
if(-not (Test-Path -LiteralPath $settingsPath)){throw 'Start and close the Hub once to create settings.json, then rerun'}
$settings=Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
if($metadata -and $metadata.rate_hz -ne $settings.tx.rate_hz){throw 'Waveform rate differs from saved TX rate'}
$backup=Join-Path $DataDirectory ('backups\settings-before-project-assets-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff')+'.json')
New-Item -ItemType Directory -Force (Split-Path -Parent $backup) | Out-Null
Copy-Item -LiteralPath $settingsPath -Destination $backup
$settings.bitfile_path=$bitfile
$settings.tx.waveform_path=$path
$settings.tx.rf_enabled=$false
$temporary=$settingsPath+'.tmp'
$settings | ConvertTo-Json -Depth 12 | Set-Content -Encoding utf8 -LiteralPath $temporary
Move-Item -LiteralPath $temporary -Destination $settingsPath -Force
[pscustomobject]@{status='CONFIGURED';waveform=$path;bitfile=$bitfile;rf_enabled=$false;backup=$backup}
