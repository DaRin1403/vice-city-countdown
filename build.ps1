$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'The .NET Framework C# compiler (csc.exe) was not found.'
}

$dist = Join-Path $PSScriptRoot 'dist'
$app = Join-Path $dist 'ViceCityCountdown.exe'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$sources = (Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs').FullName
& $compiler /nologo /codepage:65001 /target:winexe /optimize+ /out:$app /reference:System.Drawing.dll /reference:System.Windows.Forms.dll @sources
if ($LASTEXITCODE -ne 0) { throw "Build failed ($LASTEXITCODE)." }

Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'fonts') -Destination $dist -Recurse -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'docs') -Destination $dist -Recurse -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md'),(Join-Path $PSScriptRoot 'countdown.ini') -Destination $dist -Force
$zip = Join-Path $PSScriptRoot 'ViceCityCountdown-Windows.zip'
$files = Get-ChildItem -LiteralPath $dist
Compress-Archive -LiteralPath $files.FullName -DestinationPath $zip -Force
Get-Item -LiteralPath $zip
