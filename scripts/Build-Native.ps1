param([switch]$BuildOnly)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$radio=Join-Path $env:USERPROFILE 'radioconda'
$vcvars=Join-Path $env:LOCALAPPDATA 'Microsoft\VisualStudio\BuildTools\VC\Auxiliary\Build\vcvars64.bat'
cmd.exe /c "`"$vcvars`" && set" | ForEach-Object {
  if ($_ -match '^(.*?)=(.*)$') { Set-Item -Path "Env:$($matches[1])" -Value $matches[2] }
}
$env:Path="$radio;$radio\Library\bin;$radio\Scripts;$env:Path"
$source=Join-Path $root 'soapy-vst'
$build=Join-Path $source 'build-vst'
& cmake -S $source -B $build -G Ninja '-DCMAKE_BUILD_TYPE=Release' "-DCMAKE_PREFIX_PATH=$radio\Library" "-DCMAKE_INSTALL_PREFIX=$radio\Library" '-DCMAKE_C_COMPILER=cl' '-DCMAKE_CXX_COMPILER=cl'
if ($LASTEXITCODE) { throw 'Configure failed' }
& cmake --build $build --config Release
if ($LASTEXITCODE) { throw 'Build failed' }
if (-not $BuildOnly) {
  & cmake --install $build --config Release
  if ($LASTEXITCODE) { throw 'Install failed (close GQRX first)' }
}
