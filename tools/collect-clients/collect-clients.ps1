param(
    [string]$Source = 'X:\Отделы ИТФ\Отдел программирования\Клиенты',
    [string]$OutputFolder = ''
)

$ErrorActionPreference = 'Continue'
[Console]::OutputEncoding = [Text.Encoding]::UTF8

function Pause-AndExit([int]$code) {
    Write-Host ''
    Read-Host 'Нажмите Enter, чтобы закрыть окно' | Out-Null
    exit $code
}

Write-Host 'Сбор данных клиентов для Yellow Black Notes' -ForegroundColor Yellow
Write-Host "Откуда: $Source"

if (-not (Test-Path -LiteralPath $Source)) {
    Write-Host "Папка не найдена: $Source" -ForegroundColor Red
    Write-Host 'Проверьте, что диск X: подключён, и запустите ещё раз.'
    Pause-AndExit 1
}

$Source = (Resolve-Path -LiteralPath $Source).ProviderPath.TrimEnd('\')
$LongPrefix = '\\?\'
$LongSource = if ($Source.StartsWith('\\')) { '\\?\UNC\' + $Source.Substring(2) } else { $LongPrefix + $Source }
$stamp = Get-Date -Format 'yyyy-MM-dd_HH-mm'
$desktop = if ($OutputFolder) { $OutputFolder } else { [Environment]::GetFolderPath('Desktop') }
$target = Join-Path $desktop "Клиенты_выгрузка_$stamp"
$filesDir = Join-Path $target 'Файлы'
New-Item -ItemType Directory -Force -Path $filesDir | Out-Null
Write-Host "Куда:   $target"
Write-Host ''

$patterns = @('*.doc', '*.docx', '*.docm', '*.rtf', '*.odt',
              '*.xls', '*.xlsx', '*.xlsm', '*.ods', '*.csv',
              '*.txt')

Write-Host 'Копирую Word, Excel и текстовые файлы. Исходные файлы не меняются…'
$log = Join-Path $target 'журнал_копирования.txt'
$robocopyArgs = @($Source, $filesDir) + $patterns + @('/S', '/R:1', '/W:1', '/XJ', '/XF', '~$*', '/XD', 'bin', 'obj', 'packages', 'node_modules', '.git', '/MAX:20000000', '/NP', ('/UNILOG:' + $log), '/TEE', '/NFL', '/NDL')
& robocopy @robocopyArgs | Out-Null
$rc = $LASTEXITCODE
if ($rc -ge 8) {
    Write-Host "Часть файлов скопировать не удалось (код $rc). Подробности: $log" -ForegroundColor Yellow
}

Write-Host 'Составляю список всех файлов в папке клиентов…'
$all = Get-ChildItem -LiteralPath $LongSource -Recurse -File -Force -ErrorAction SilentlyContinue
$all | ForEach-Object {
    [pscustomobject]@{
        'Путь'      = $_.FullName.Substring($LongSource.Length).TrimStart('\')
        'Тип'       = $_.Extension.ToLowerInvariant()
        'Размер_КБ' = [math]::Round($_.Length / 1KB, 1)
        'Изменён'   = $_.LastWriteTime.ToString('yyyy-MM-dd HH:mm')
    }
} | Export-Csv -LiteralPath (Join-Path $target 'все_файлы.csv') -NoTypeInformation -Encoding UTF8 -Delimiter ';'

$copied = Get-ChildItem -LiteralPath ($LongPrefix + $filesDir) -Recurse -File -ErrorAction SilentlyContinue
$summary = @(
    "Источник: $Source"
    "Дата: $(Get-Date -Format 'yyyy-MM-dd HH:mm')"
    "Компьютер: $env:COMPUTERNAME"
    "Всего файлов в папке клиентов: $($all.Count)"
    "Скопировано Word, Excel и текстовых: $($copied.Count)"
    ''
    'По типам (скопированные):'
) + ($copied | Group-Object { $_.Extension.ToLowerInvariant() } | Sort-Object Count -Descending |
        ForEach-Object { "  $($_.Name): $($_.Count)" }) + @(
    ''
    'Все типы файлов в папке клиентов:'
) + ($all | Group-Object { $_.Extension.ToLowerInvariant() } | Sort-Object Count -Descending |
        ForEach-Object { "  $(if ($_.Name) { $_.Name } else { '(без расширения)' }): $($_.Count)" })
$summary | Set-Content -LiteralPath (Join-Path $target 'итог.txt') -Encoding UTF8

Write-Host 'Упаковываю в один архив…'
$zip = "$target.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$skipped = New-Object System.Collections.Generic.List[string]
$rootName = Split-Path $target -Leaf
$stream = [IO.File]::Open($zip, [IO.FileMode]::CreateNew)
$archive = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create, $false, [Text.Encoding]::UTF8)
try {
    foreach ($f in [IO.Directory]::EnumerateFiles('\\?\' + $target, '*', [IO.SearchOption]::AllDirectories)) {
        $rel = $f.Substring(4 + $target.Length).TrimStart('\')
        try {
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $f, ($rootName + '/' + ($rel -replace '\\', '/')), [IO.Compression.CompressionLevel]::Optimal)
        }
        catch {
            $skipped.Add($rel + ' — ' + $_.Exception.InnerException.Message)
        }
    }
}
finally {
    $archive.Dispose()
    $stream.Dispose()
}
if ($skipped.Count -gt 0) {
    Write-Host "Не попали в архив: $($skipped.Count). Список в файле не_вошли_в_архив.txt рядом с архивом" -ForegroundColor Yellow
    $skipped | Set-Content -LiteralPath "$target-не_вошли_в_архив.txt" -Encoding UTF8
}

Write-Host ''
$summary | Select-Object -First 5 | ForEach-Object { Write-Host $_ }
if (Test-Path -LiteralPath $zip) {
    $mb = [math]::Round((Get-Item -LiteralPath $zip).Length / 1MB, 1)
    Write-Host ''
    Write-Host "Готово. Архив на рабочем столе: $(Split-Path $zip -Leaf) ($mb МБ)" -ForegroundColor Green
    Write-Host 'Перенесите этот архив на домашний компьютер.'
}
Pause-AndExit 0
