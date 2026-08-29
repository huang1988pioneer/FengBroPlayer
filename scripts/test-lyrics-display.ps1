$ErrorActionPreference = 'Stop'

$xamlPath = Join-Path $PSScriptRoot '..\Views\MainWindow.axaml'
Add-Type -AssemblyName System.Xml.Linq
$document = [System.Xml.Linq.XDocument]::Load((Resolve-Path $xamlPath))
$avaloniaNamespace = [System.Xml.Linq.XNamespace]::Get('https://github.com/avaloniaui')
$lyricTextBlock = $document.Descendants($avaloniaNamespace + 'TextBlock') |
    Where-Object {
        $textAttribute = $_.Attribute('Text')
        $null -ne $textAttribute -and $textAttribute.Value -eq '{Binding CurrentLyricText}'
    } |
    Select-Object -First 1

if ($null -eq $lyricTextBlock) {
    throw 'Could not find the CurrentLyricText TextBlock in MainWindow.axaml.'
}

$wrappingAttribute = $lyricTextBlock.Attribute('TextWrapping')
$trimmingAttribute = $lyricTextBlock.Attribute('TextTrimming')
$wrapping = if ($null -eq $wrappingAttribute) { $null } else { $wrappingAttribute.Value }
$trimming = if ($null -eq $trimmingAttribute) { $null } else { $trimmingAttribute.Value }

if ($wrapping -ne 'Wrap') {
    throw "CurrentLyricText must wrap long lines; found TextWrapping='$wrapping'."
}

if ($trimming -ne 'None') {
    throw "CurrentLyricText must preserve the complete line; found TextTrimming='$trimming'."
}

Write-Output 'Lyrics display regression check passed: long lyric lines wrap without trimming.'
