param([string]$Source='C:\Users\Public\Documents\National Instruments\FPGA Extensions Bitfiles\NI PXIe-5644R\NI Streaming for VST.lvbitx')
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
$manifest=Get-Content -LiteralPath (Join-Path $taskRoot 'hardware\ni-pxie-5644r\manifest.json') -Raw | ConvertFrom-Json
$sourceFile=Get-Item -LiteralPath $Source -ErrorAction Stop
if($sourceFile.PSIsContainer -or $sourceFile.Length -ne $manifest.bytes){throw 'Unexpected NI bitfile size/type'}
$hash=(Get-FileHash -LiteralPath $sourceFile.FullName -Algorithm SHA256).Hash.ToLower()
if($hash -ne $manifest.sha256){throw 'NI bitfile differs from the recorded PXIe-5644R build; do not substitute another FPGA design'}
$destination=Join-Path $taskRoot 'hardware\ni-pxie-5644r\local\NI Streaming for VST.lvbitx'
New-Item -ItemType Directory -Force (Split-Path -Parent $destination) | Out-Null
if(Test-Path -LiteralPath $destination){
 if((Get-FileHash -LiteralPath $destination).Hash.ToLower() -ne $hash){throw 'Local bitfile differs; refusing overwrite'}
}elseif($sourceFile.FullName -ne $destination){Copy-Item -LiteralPath $sourceFile.FullName -Destination $destination}
if((Get-FileHash -LiteralPath $destination).Hash.ToLower() -ne $hash){throw 'Imported copy failed verification'}
[pscustomobject]@{status='VERIFIED';path=$destination;sha256=$hash;git_policy='Local vendor binary excluded from Git and public releases'}
