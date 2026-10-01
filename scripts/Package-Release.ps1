param([string]$Version='2.1.0',[switch]$AllowIncomplete)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
if(-not $AllowIncomplete){
 foreach($test in @('grc-120-20min','gqrx-tdms-120-20min','independence')){
  $file=Join-Path $root "tests\artifacts\$test\result.json"
  if(-not (Test-Path -LiteralPath $file) -or (Get-Content -Raw $file | ConvertFrom-Json).status -ne 'PASS'){throw "Acceptance missing or failed: $test"}
 }
}
$stage=Join-Path $root ('work\release-stage-'+(Get-Date -Format yyyyMMdd-HHmmss))
$out=Join-Path $root 'docs\releases\2.1'
New-Item -ItemType Directory -Force $stage,$out,(Join-Path $stage 'VST-Bridge'),(Join-Path $stage 'SoapyVST') | Out-Null
Copy-Item (Join-Path $root 'dist\VSTHub\VSTHub.exe') (Join-Path $stage 'VST-Bridge\VSTHub.exe')
foreach($dir in @('examples','waveforms')){
 Get-ChildItem (Join-Path $root $dir) -File -Recurse | Where-Object { $_.FullName -notmatch '[\\/]__pycache__[\\/]' } | ForEach-Object {
  $relative=$_.FullName.Substring($root.Length+1)
  $destination=Join-Path $stage "VST-Bridge\$relative"
  New-Item -ItemType Directory -Force (Split-Path -Parent $destination) | Out-Null
  Copy-Item -LiteralPath $_.FullName -Destination $destination
 }
}
New-Item -ItemType Directory -Force (Join-Path $stage 'VST-Bridge\docs') | Out-Null
Get-ChildItem (Join-Path $root 'docs') -Filter *.md -File | Copy-Item -Destination (Join-Path $stage 'VST-Bridge\docs')
Copy-Item (Join-Path $root 'docs\images') (Join-Path $stage 'VST-Bridge\docs') -Recurse
Copy-Item (Join-Path $root 'README.md'),(Join-Path $root 'README.zh-CN.md'),(Join-Path $root 'THIRD-PARTY-NOTICES.md') (Join-Path $stage 'VST-Bridge')
New-Item -ItemType Directory -Force (Join-Path $stage 'VST-Bridge\scripts') | Out-Null
Copy-Item (Join-Path $root 'scripts\Launch-Example.ps1'),(Join-Path $root 'scripts\generate_nr_four_carrier.py'),(Join-Path $root 'scripts\README.md') (Join-Path $stage 'VST-Bridge\scripts')
New-Item -ItemType Directory -Force (Join-Path $stage 'VST-Bridge\tests\artifacts') | Out-Null
foreach($test in @('grc-120-20min','gqrx-tdms-120-20min','independence')){
 $dest=Join-Path $stage "VST-Bridge\tests\artifacts\$test"
 New-Item -ItemType Directory -Force $dest | Out-Null
Get-ChildItem (Join-Path $root "tests\artifacts\$test") -File | Copy-Item -Destination $dest
}
Copy-Item (Join-Path $root 'tests\GQRX-CAPTURE.md') (Join-Path $stage 'VST-Bridge\tests')
Copy-Item (Join-Path $root 'tests\review_rf_spectrum.py'),(Join-Path $root 'tests\gqrx-capture-instrumentation.patch') (Join-Path $stage 'VST-Bridge\tests')
Copy-Item (Join-Path $root 'tests\artifacts\release-binary-hashes.json') (Join-Path $stage 'VST-Bridge\tests\artifacts')
@'
# Start VST Bridge

Run VSTHub.exe in this directory. NI drivers/bitfile and radioconda must already be installed.
The application installs its matching embedded SoapySDR plugin. See README.zh-CN.md or README.md.
The source-tree path dist/VSTHub/VSTHub.exe in those documents corresponds to ./VSTHub.exe in this package.
GNU Radio example: examples/grc/vst_bridge_120_duplex.grc.
TDMS example: waveforms/nr-tm3.1a-fdd-4x20mhz-120msps.tdms.
Validation and application captures: docs/VALIDATION.md and tests/artifacts.
Build tools require the full source repository; only Launch-Example.ps1 and waveform generation are standalone here.
'@ | Set-Content -Encoding utf8 (Join-Path $stage 'VST-Bridge\START-HERE.md')
Copy-Item (Join-Path $root 'soapy-vst\build-vst\vstSupport.dll') (Join-Path $stage 'SoapyVST')
Copy-Item (Join-Path $root 'soapy-vst\README.md') (Join-Path $stage 'SoapyVST')
@'
# Soapy VST plugin

Windows x64 / SoapySDR 0.8. Close GQRX and GNU Radio before installation.
Copy vstSupport.dll to radioconda/Library/lib/SoapySDR/modules0.8.
Start the matching VST Bridge release (VSTHub.exe), which owns the NI session.
GQRX device: soapy=0,driver=vst,resource=RIO0
GNU Radio / Soapy: driver=vst,resource=RIO0
RX readStream and TX writeStream support CF32/CS16; TX is 120 MS/s.
The application package embeds this same DLL and normally installs it automatically.
Do not run a legacy producer alongside the Bridge.
'@ | Set-Content -Encoding utf8 (Join-Path $stage 'SoapyVST\INSTALL.md')
Compress-Archive -Path (Join-Path $stage 'VST-Bridge') -DestinationPath (Join-Path $out "VST-Bridge-$Version-windows-x64.zip") -Force
Compress-Archive -Path (Join-Path $stage 'SoapyVST') -DestinationPath (Join-Path $out "SoapyVST-$Version-windows-x64.zip") -Force
$hashes=Get-ChildItem $out -Filter *.zip | ForEach-Object { (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLower()+'  '+$_.Name }
$hashes | Set-Content -Encoding ascii (Join-Path $out 'SHA256SUMS.txt')
Write-Output $out
