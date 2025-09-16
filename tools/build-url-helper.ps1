param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $projectDir "UrlFromWindow/UrlFromWindow.csproj"

Write-Host "Building UrlFromWindow ($Configuration)"
dotnet restore $proj
dotnet publish $proj -c $Configuration -r win-x64 -p:PublishSingleFile=true --self-contained true /p:IncludeNativeLibrariesForSelfExtract=true

$publishDir = Join-Path $projectDir "UrlFromWindow/bin/$Configuration/net6.0-windows/win-x64/publish"
$exe = Join-Path $publishDir "UrlFromWindow.exe"

if (!(Test-Path $exe)) { throw "Build failed, exe not found: $exe" }

$dest = Join-Path $projectDir "../bin/windows/UrlFromWindow.exe"
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $dest) | Out-Null
Copy-Item $exe $dest -Force
Write-Host "Copied to $dest"

