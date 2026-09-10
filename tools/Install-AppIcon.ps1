[CmdletBinding()]
param([switch]$CreateShortcuts)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$assets = Join-Path $root 'src\BleHid.App\Assets'
[void][IO.Directory]::CreateDirectory($assets)
if (-not (Test-Path -LiteralPath (Join-Path $assets 'app-icon.png'))) { throw 'PNG do ícone não encontrado nos assets.' }
Add-Type -AssemblyName System.Drawing
$source = [Drawing.Image]::FromFile((Join-Path $assets 'app-icon.png'))
$frames = @()
try {
 foreach ($size in @(16,24,32,48,64,128,256)) {
  $bitmap = [Drawing.Bitmap]::new($size,$size)
  $graphics = [Drawing.Graphics]::FromImage($bitmap)
  $buffer = [IO.MemoryStream]::new()
  try {
   $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
   $graphics.DrawImage($source,0,0,$size,$size)
   $bitmap.Save($buffer,[Drawing.Imaging.ImageFormat]::Png)
   $frames += [pscustomobject]@{ Size=$size; Bytes=$buffer.ToArray() }
  } finally { $buffer.Dispose(); $graphics.Dispose(); $bitmap.Dispose() }
 }
} finally { $source.Dispose() }
$writer = [IO.BinaryWriter]::new([IO.File]::Create((Join-Path $assets 'app-icon.ico')))
try {
 $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$frames.Count)
 $offset = 6 + 16 * $frames.Count
 foreach ($frame in $frames) {
  $dimension = if ($frame.Size -eq 256) { 0 } else { $frame.Size }
  $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
  $writer.Write([byte]0); $writer.Write([byte]0)
  $writer.Write([uint16]1); $writer.Write([uint16]32)
  $writer.Write([uint32]$frame.Bytes.Length); $writer.Write([uint32]$offset)
  $offset += $frame.Bytes.Length
 }
 foreach ($frame in $frames) { $writer.Write([byte[]]$frame.Bytes) }
} finally { $writer.Dispose() }
if (-not $CreateShortcuts) { Write-Output 'ICO regenerado a partir do PNG versionado.'; return }
$exe = Join-Path $root 'publish\app\BleHid.App.exe'
if (-not (Test-Path -LiteralPath $exe)) { throw 'Aplicativo não encontrado.' }
$shell = New-Object -ComObject WScript.Shell
foreach ($folder in @([Environment]::GetFolderPath('Desktop'),[Environment]::GetFolderPath('Programs'))) {
 $path = Join-Path $folder 'iPhone BLE Control.lnk'
 $link = $shell.CreateShortcut($path)
 if ((Test-Path -LiteralPath $path) -and $link.TargetPath -ne $exe) { throw 'Outro atalho com mesmo nome.' }
 $link.TargetPath = $exe
 $link.Arguments = '--screen-layout'
 $link.WorkingDirectory = Split-Path $exe
 $link.IconLocation = Join-Path $assets 'app-icon.ico'
 $link.Description = 'Controle o iPhone com teclado e mouse do PC'
 $link.Save()
 Write-Output $path
}
