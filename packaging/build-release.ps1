param(
    [string]$Version = '0.2.1-beta.1',
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$webRoot = Join-Path $repoRoot 'web'
$releaseRoot = if ($OutputDirectory) { [IO.Path]::GetFullPath($OutputDirectory, $repoRoot) } else { Join-Path $repoRoot "artifacts\\releases\\v$Version" }
$publish = Join-Path $releaseRoot 'publish'

Push-Location $webRoot
npm ci
npm run build
Pop-Location

New-Item -ItemType Directory -Force -Path $publish | Out-Null
dotnet publish (Join-Path $repoRoot 'src\LitWeave\LitWeave.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:Version=$Version -o $publish

$zip = Join-Path $releaseRoot "LitWeave-$Version-win-x64-portable.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath (Join-Path $releaseRoot 'SHA256SUMS.txt') -Value "$hash  $(Split-Path $zip -Leaf)" -Encoding utf8NoBOM
Write-Host "Portable package: $zip"
Write-Host "SHA-256: $hash"
