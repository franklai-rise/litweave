$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$webRoot = Join-Path $repoRoot 'web'
$artifacts = Join-Path $repoRoot 'artifacts'
$publish = Join-Path $artifacts 'publish'

Push-Location $webRoot
npm ci
npm run build
Pop-Location

Remove-Item -LiteralPath $artifacts -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $publish | Out-Null
dotnet publish (Join-Path $repoRoot 'src\LitWeave\LitWeave.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:Version=0.1.0 -o $publish

$zip = Join-Path $artifacts 'LitWeave-0.1.0-win-x64-portable.zip'
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath (Join-Path $artifacts 'SHA256SUMS.txt') -Value "$hash  $(Split-Path $zip -Leaf)" -Encoding utf8NoBOM
Write-Host "Portable package: $zip"
Write-Host "SHA-256: $hash"
