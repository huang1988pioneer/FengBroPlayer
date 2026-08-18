param(
    [string]$Executable = (Join-Path $PSScriptRoot '..\bin\Debug\net10.0\FengBroPlayer.exe'),
    [int]$ExitTimeoutSeconds = 5
)

$ErrorActionPreference = 'Stop'
$resolvedExecutable = (Resolve-Path -LiteralPath $Executable).Path
$process = Start-Process -FilePath $resolvedExecutable -WorkingDirectory (Split-Path $resolvedExecutable) -PassThru

try {
    $windowDeadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        Start-Sleep -Milliseconds 100
        $process.Refresh()
    } while (-not $process.HasExited -and $process.MainWindowHandle -eq 0 -and [DateTime]::UtcNow -lt $windowDeadline)

    if ($process.HasExited) {
        throw "Player exited before creating its main window (exit code $($process.ExitCode))."
    }
    if ($process.MainWindowHandle -eq 0) {
        throw 'Player did not create a main window within 10 seconds.'
    }
    if (-not $process.CloseMainWindow()) {
        throw 'Could not request a normal main-window close.'
    }

    $exitTimer = [Diagnostics.Stopwatch]::StartNew()
    if (-not $process.WaitForExit($ExitTimeoutSeconds * 1000)) {
        $process.Refresh()
        Write-Error (
            "Window closed but process remained after {0}s: pid={1} cpu={2:N2}s working-mb={3:N1} handles={4} threads={5}" -f
            $ExitTimeoutSeconds,
            $process.Id,
            $process.CPU,
            ($process.WorkingSet64 / 1MB),
            $process.HandleCount,
            $process.Threads.Count
        )
    }

    $exitTimer.Stop()
    Write-Output ("PASS: player exited {0}ms after its main window closed." -f $exitTimer.ElapsedMilliseconds)
}
finally {
    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        $process.WaitForExit(3000) | Out-Null
    }
    $process.Dispose()
}
