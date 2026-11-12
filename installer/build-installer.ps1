$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$csproj = Join-Path $root 'YellowBlackEditor.csproj'
$version = ([xml](Get-Content $csproj -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$publish = Join-Path $root 'bin\publish\win-x64'

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'Inno Setup 6 не найден. Установите его с https://jrsoftware.org/isdl.php' }

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish $csproj -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o $publish
if ($LASTEXITCODE -ne 0) { throw 'Сборка не удалась' }
Get-ChildItem $publish -Filter *.pdb | Remove-Item

& $iscc "/DMyAppVersion=$version" "/DSourceDir=$publish" (Join-Path $PSScriptRoot 'YellowBlackNotes.iss')
if ($LASTEXITCODE -ne 0) { throw 'Установщик не собрался' }

$setup = Join-Path $PSScriptRoot "Output\YellowBlackNotes-Setup-$version.exe"
Write-Host "Готово: $setup ($([math]::Round((Get-Item $setup).Length / 1MB, 1)) МБ)"
