param([switch]$CompileOnly)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$radio=Join-Path $env:USERPROFILE 'radioconda'
$env:PATH="$radio;$radio\Library\bin;$radio\Scripts;"+$env:PATH
$env:GR_CONF_DEFAULT_BUFFER_SIZE='1048576'
$env:SOAPY_SDR_ROOT=Join-Path $radio 'Library'
& (Join-Path $radio 'Scripts\grcc.exe') -o (Join-Path $root 'examples\grc') (Join-Path $root 'examples\grc\vst_bridge_120_duplex.grc')
if($LASTEXITCODE){throw 'GRC compilation failed'}
if(-not $CompileOnly){& (Join-Path $radio 'python.exe') (Join-Path $root 'examples\grc\vst_bridge_120_duplex.py')}
