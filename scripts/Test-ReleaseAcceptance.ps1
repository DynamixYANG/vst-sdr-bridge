# Shared local publication gate. USER_ACCEPTED is deliberately distinct from PASS.
function Test-ReleaseAcceptance([string]$Root) {
 foreach($test in @('grc-normal-2.2-20min','tx-controls-2.2','independence-2.2','independence-footer-2.2')) {
  $path=Join-Path $Root "tests\artifacts\$test\result.json"
  if(-not (Test-Path -LiteralPath $path) -or (Get-Content -Raw -LiteralPath $path | ConvertFrom-Json).status -ne 'PASS') {throw "Acceptance missing or failed: $test"}
 }
 $accepted=Get-Content -Raw -Encoding utf8 (Join-Path $Root 'tests\artifacts\gqrx-tdms-2.2-accepted17\result.json') | ConvertFrom-Json
 if($accepted.status -ne 'USER_ACCEPTED' -or -not $accepted.acceptance.user_authorized -or $accepted.seconds -lt 1000 -or
    -not $accepted.checks.rates -or -not $accepted.checks.no_hardware_or_logger_errors -or -not $accepted.checks.no_rx_drop -or -not $accepted.checks.no_display_skip) {throw 'Explicit user-accepted TDMS/GQRX record missing or unstable.'}
 $review=Get-Content -Raw (Join-Path $Root 'tests\artifacts\release-review-2.2.json') | ConvertFrom-Json
 if($review.status -ne 'PASS') {throw 'Final UI build review missing or failed.'}
 foreach($pair in @(@('VSTHub.exe','dist\VSTHub\VSTHub.exe'),@('vstSupport.dll','soapy-vst\build-vst\vstSupport.dll'))) {
  if((Get-FileHash (Join-Path $Root $pair[1]) -Algorithm SHA256).Hash.ToLower() -ne $review.release_binary_sha256.($pair[0])) {throw "Final build identity changed: $($pair[0])"}
 }
}
