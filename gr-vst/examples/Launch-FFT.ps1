param(
  [double]$Rate = 10e6,
  [double]$Center = 1e9,
  [double]$Ref = 0,
  [double]$Seconds = 0,
  [switch]$HeadlessSmoke,
  [switch]$StreamingBitfile
)
$ErrorActionPreference = 'Stop'
$rc = 'C:\Users\yang\radioconda'
$root = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path "$rc\python.exe")) { throw "radioconda not found at $rc" }
$env:PATH = "$rc;$rc\Library\bin;$rc\Scripts;" + $env:PATH
$env:QT_PLUGIN_PATH = "$rc\Library\plugins"
$env:PYTHONPATH = Join-Path $root 'python'
$script = Join-Path $PSScriptRoot 'run_fft.py'
$args = @($script, '--rate', $Rate, '--center', $Center, '--ref', $Ref, '--seconds', $Seconds)
if ($HeadlessSmoke) { $args += '--headless-smoke' }
if ($StreamingBitfile) { $args += '--streaming-bitfile' }
& "$rc\python.exe" @args
exit $LASTEXITCODE