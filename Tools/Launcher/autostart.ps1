# Vision-to-Wall: register / remove "start the exhibition at login" for the current Windows user.
#
#   autostart.ps1 install [-DelaySec 20]   creates a shortcut in the user's Startup folder -> start_exhibition.bat
#   autostart.ps1 uninstall                removes that shortcut
#   autostart.ps1 status                   shows whether it is registered
#
# Only the current user's Startup folder is touched (no admin, no registry, no task scheduler).
# Run it yourself; the development sessions never run it.

param(
    [Parameter(Position = 0)]
    [ValidateSet('install', 'uninstall', 'status')]
    [string]$Action = 'status',
    [double]$DelaySec = 20
)

$ErrorActionPreference = 'Stop'
$startup = [Environment]::GetFolderPath('Startup')
$link = Join-Path $startup 'VisionToWall Exhibition.lnk'
$target = Join-Path $PSScriptRoot 'start_exhibition.bat'

switch ($Action) {
    'install' {
        $shell = New-Object -ComObject WScript.Shell
        $sc = $shell.CreateShortcut($link)
        $sc.TargetPath = $target
        $sc.Arguments = "-DelaySec $DelaySec"
        $sc.WorkingDirectory = $PSScriptRoot
        $sc.WindowStyle = 7  # minimized: the bat only launches the hidden watchdog and exits
        $sc.Description = 'Vision-to-Wall exhibition (hand sensor + game watchdog)'
        $sc.Save()
        Write-Host "registered: $link"
        Write-Host "  -> $target -DelaySec $DelaySec"
    }
    'uninstall' {
        if (Test-Path $link) { Remove-Item -Force $link; Write-Host "removed: $link" }
        else { Write-Host "not registered: $link" }
    }
    'status' {
        if (Test-Path $link) {
            $sc = (New-Object -ComObject WScript.Shell).CreateShortcut($link)
            Write-Host "registered: $link -> $($sc.TargetPath) $($sc.Arguments)"
        } else { Write-Host "not registered ($link)" }
    }
}
