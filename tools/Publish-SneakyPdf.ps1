param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string] $Runtime = 'win-x64',
    [switch] $NoRestore
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$output = Join-Path $root "artifacts\sneakypdf\$Runtime"
$archive = Join-Path $root "artifacts\sneakypdf\SneakyPdf-$Runtime.zip"
$project = Join-Path $root 'apps\Broadside.Reader\Broadside.Reader.csproj'
$arguments = @('publish', $project, '-c', 'Release', '-r', $Runtime, '--self-contained', 'true',
    '-p:PublishTrimmed=false', '-p:PublishSingleFile=false', '-o', $output)
if ($NoRestore) {
    $arguments += '--no-restore'
}

& dotnet @arguments
if ($LASTEXITCODE -ne 0) {
    throw "SneakyPdf publish failed with exit code $LASTEXITCODE."
}

Compress-Archive -LiteralPath $output -DestinationPath $archive -Force
Write-Output "Portable app: $output\SneakyPdf.exe"
Write-Output "Archive: $archive"
