$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$viewPath = Join-Path $repoRoot 'Views\MainWindow.axaml'
$codeBehindPath = Join-Path $repoRoot 'Views\MainWindow.axaml.cs'
$view = Get-Content -Raw -LiteralPath $viewPath
$codeBehind = Get-Content -Raw -LiteralPath $codeBehindPath

$failures = [System.Collections.Generic.List[string]]::new()

if ($view -notmatch 'Command="\{Binding \$parent\[Window\]\.\(\(vm:MainViewModel\)DataContext\)\.SelectMediaCommand\}"') {
    $failures.Add('Playlist row must bind Button.Command to SelectMediaCommand so an ordinary left click switches media.')
}

if ($view -match 'PointerPressed="OnPlaylistRowPointerPressed"') {
    $failures.Add('Playlist row must not rely on a bubbling PointerPressed handler because Button handles that event first.')
}

if ($codeBehind -notmatch 'PlaylistList\.AddHandler\(PointerPressedEvent,\s*OnPlaylistRowPointerPressed,\s*RoutingStrategies\.Tunnel,\s*handledEventsToo:\s*true\)') {
    $failures.Add('Playlist selection handling must be registered as a tunneling handled-events-too listener.')
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Output 'PASS: ordinary playlist left click is wired to media switching; range selection is intercepted in the tunnel.'
