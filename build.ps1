param([switch]$SkipSoapy,[switch]$SelfTest)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$sdk=Join-Path $root 'tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $sdk)) {
  $sdk=(Get-Command dotnet -ErrorAction Stop).Source
}
foreach ($name in @('VSTHub')) {
  if (Get-Process $name -ErrorAction SilentlyContinue) { throw "Close $name before publishing. The running EXE cannot be overwritten." }
}
if (-not $SkipSoapy) {
  & (Join-Path $root 'scripts\Build-Native.ps1') -BuildOnly
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$out=Join-Path $root 'dist\VSTHub'
& $sdk publish (Join-Path $root 'src\Vst.Hub\Vst.Hub.csproj') -c Release -o $out --nologo
if ($LASTEXITCODE) { throw 'Publish failed' }
if ($SelfTest) {
  $process=Start-Process (Join-Path $out 'VSTHub.exe') -ArgumentList @('--self-test','--data-dir',(Join-Path $root 'tests\artifacts')) -WindowStyle Hidden -PassThru
  $process.WaitForExit()
  if ($process.ExitCode) { throw 'Self-test failed; see tests/artifacts/selftest-result.json' }
}
Write-Host "Ready: $(Join-Path $out 'VSTHub.exe')"
