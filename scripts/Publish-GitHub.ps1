param([string]$Repository='DynamixYANG/vst-sdr-bridge',[string]$Tag='v2.2.0')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$ghCommand=Get-Command gh -ErrorAction SilentlyContinue
$gh=if($ghCommand){$ghCommand.Source}else{Join-Path $root 'work\bridge-upgrade\gh\bin\gh.exe'}
if(-not (Test-Path -LiteralPath $gh)){throw 'Install GitHub CLI and run gh auth login first.'}
Push-Location $root
try {
 & $gh auth status
 if($LASTEXITCODE){throw 'GitHub authentication is required. Run gh auth login --hostname github.com --web.'}
 $login=& $gh api user --jq .login
 if($LASTEXITCODE -or $Repository.Split('/')[0] -ne $login){throw 'Authenticated account does not match the repository owner.'}
 if(git status --porcelain){throw 'Commit all source changes before publication.'}
 & git rev-parse --verify $Tag
 if($LASTEXITCODE){throw "Missing local tag $Tag"}
 $tagCommit=git rev-list -n 1 $Tag
 $headCommit=git rev-parse HEAD
 if($tagCommit -ne $headCommit){throw 'Release tag must point to the current tested source commit.'}
 $version=$Tag.TrimStart('v')
 $series=($version.Split('.')[0..1] -join '.')
 $out=Join-Path $root ('docs\releases\'+$series)
 . (Join-Path $PSScriptRoot 'Test-ReleaseAcceptance.ps1')
 Test-ReleaseAcceptance $root
 foreach($line in Get-Content (Join-Path $out 'SHA256SUMS.txt')){
  if($line -match '^([0-9a-f]{64})  (.+)$'){
   if((Get-FileHash (Join-Path $out $matches[2]) -Algorithm SHA256).Hash.ToLower() -ne $matches[1]){throw 'Release checksum mismatch.'}
  } else {throw 'Invalid checksum manifest.'}
 }
 $remote=git remote get-url origin 2>$null
 if(-not $remote){
  & $gh repo create $Repository --private --description 'NI VST middleware for GNU Radio and GQRX: native SoapySDR, independent RX/TX, direct TDMS playback and verified 120 MS/s streaming.' --source $root --remote origin --push
  if($LASTEXITCODE){throw 'Repository creation/push failed; an existing repository will not be overwritten.'}
 } else {
  if($remote -notin @("https://github.com/$Repository.git","https://github.com/$Repository","git@github.com:$Repository.git")){throw 'Unexpected origin; refusing to push.'}
  & git push origin main
  if($LASTEXITCODE){throw 'Source push failed.'}
 }
 & git push origin $Tag
 if($LASTEXITCODE){throw 'Tag push failed.'}
 $assets=@(Get-ChildItem $out -Filter *.zip | ForEach-Object FullName)+(Join-Path $out 'SHA256SUMS.txt')
 & $gh release create $Tag @assets --repo $Repository --verify-tag --title "VST Bridge $version - GNU Radio / GQRX middleware" --notes-file (Join-Path $out 'RELEASE-NOTES.md')
 if($LASTEXITCODE){throw 'Release creation failed. Existing releases are not modified automatically.'}
 & $gh release view $Tag --repo $Repository --json url,tagName,assets
 if($LASTEXITCODE){throw 'Release verification failed.'}
} finally {Pop-Location}
