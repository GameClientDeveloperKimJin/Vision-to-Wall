# Vision-to-Wall exhibition launcher (Windows PowerShell 5.1).
#
#   exhibition.ps1 start    start the watchdog in the background (hidden) and return
#   exhibition.ps1 watch    the watchdog itself: hand sensor -> wait until ready -> game, restart either if it dies
#   exhibition.ps1 stop     stop the watchdog, the hand sensor (normal stop, exposure back to auto), then the game
#   exhibition.ps1 status   watchdog / sensor / game state and the last log lines
#
# Settings: launcher.json next to this script. Logs: logs\. Runtime files: run\.
# Changes nothing in the system: no registry, firewall or task scheduler (autostart is a separate, user-run script).
# Keep this file ASCII: Windows PowerShell 5.1 reads BOM-less scripts in the ANSI code page.

param(
    [Parameter(Position = 0)]
    [ValidateSet('start', 'watch', 'stop', 'status')]
    [string]$Action = 'status',
    [string]$Settings = '',
    [double]$DelaySec = -1   # overrides launcher.json startDelaySec (autostart uses it to wait for login / camera)
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2

$Here = $PSScriptRoot
$RunDir = Join-Path $Here 'run'
$LogDir = Join-Path $Here 'logs'
$StopRequest = Join-Path $RunDir 'stop.request'
$SensorStop = Join-Path $RunDir 'sensor.stop'
$SensorStatus = Join-Path $RunDir 'sensor.status.json'
$StateFile = Join-Path $RunDir 'state.json'
$MutexName = 'Local\VisionToWall.Exhibition.Watchdog'
$LauncherLog = Join-Path $LogDir ('launcher_{0}.log' -f (Get-Date -Format 'yyyyMMdd'))

New-Item -ItemType Directory -Force -Path $RunDir, $LogDir | Out-Null

# ------------------------------------------------------------------ settings

function Resolve-FromHere([string]$path) {
    if ([string]::IsNullOrWhiteSpace($path)) { return '' }
    if ([System.IO.Path]::IsPathRooted($path)) { return [System.IO.Path]::GetFullPath($path) }
    return [System.IO.Path]::GetFullPath((Join-Path $Here $path))
}

function Read-Settings {
    $path = if ($Settings) { $Settings } else { Join-Path $Here 'launcher.json' }
    $s = [ordered]@{
        gameExe               = '..\..\Builds\Windows\Release\VisionToWall.exe'
        gameArgs              = @()
        handSensorDir         = '..\HandSensor'
        sensorArgs            = @()
        sensorReadyTimeoutSec = 40
        sensorHangSec         = 15
        gameHangSec           = 30
        checkIntervalSec      = 2
        startDelaySec         = 0
        crashLoopCount        = 5
        crashLoopWindowSec    = 300
        crashLoopPauseSec     = 60
        stopSensorWaitSec     = 10
        stopGameWaitSec       = 10
        logKeepDays           = 14
    }
    if (Test-Path $path) {
        $json = Get-Content -Raw -Encoding UTF8 $path | ConvertFrom-Json
        foreach ($p in $json.PSObject.Properties) {
            if ($s.Contains($p.Name)) { $s[$p.Name] = $p.Value }
        }
    }
    if ($DelaySec -ge 0) { $s.startDelaySec = $DelaySec }
    $s.gameExe = Resolve-FromHere $s.gameExe
    $s.handSensorDir = Resolve-FromHere $s.handSensorDir
    $s.python = Join-Path $s.handSensorDir '.venv\Scripts\python.exe'
    $s.sensorScript = Join-Path $s.handSensorDir 'hand_sensor.py'
    $exeBase = [System.IO.Path]::GetFileNameWithoutExtension($s.gameExe)
    $s.gameName = $exeBase
    $s.gameConfig = Join-Path (Split-Path $s.gameExe -Parent) ($exeBase + '_Data\StreamingAssets\config.json')
    return $s
}

function Read-GameConfig($s) {
    # Same file the game reads; the sensor gets it through --config so F1 changes reach both.
    if (-not (Test-Path $s.gameConfig)) { return $null }
    try { return Get-Content -Raw -Encoding UTF8 $s.gameConfig | ConvertFrom-Json } catch { return $null }
}

function Test-ExternalSensor($s) {
    $c = Read-GameConfig $s
    if ($null -eq $c -or -not ($c.PSObject.Properties.Name -contains 'sensorMode')) { return $true }
    return ($c.sensorMode -eq 'external')
}

function Test-ManualExposure($s) {
    # Manual exposure stays in the camera driver after a hard kill (see Tools/HandSensor/README.md 5).
    $joined = ' ' + (@($s.sensorArgs) -join ' ') + ' '
    if ($joined -match '--exposure\s+auto\s') { return $false }
    if ($joined -match '--exposure\s+-?\d') { return $true }
    $c = Read-GameConfig $s
    if ($null -eq $c -or -not ($c.PSObject.Properties.Name -contains 'cameraExposure')) { return $false }
    $v = [string]$c.cameraExposure
    return -not ([string]::IsNullOrWhiteSpace($v) -or $v.Trim().ToLower() -eq 'auto')
}

# ------------------------------------------------------------------ log / state

function Write-Log([string]$message) {
    $line = '{0}  {1}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $message
    Add-Content -Path $LauncherLog -Value $line -Encoding UTF8
    Write-Host $line
}

function Save-State($st) {
    $tmp = $StateFile + '.tmp'
    ($st | ConvertTo-Json -Depth 4) | Set-Content -Path $tmp -Encoding UTF8
    Move-Item -Force $tmp $StateFile
}

function Remove-OldLogs($s) {
    $limit = (Get-Date).AddDays(-[double]$s.logKeepDays)
    Get-ChildItem -Path $LogDir -File -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -lt $limit } | Remove-Item -Force -ErrorAction SilentlyContinue
}

function Quote([string]$a) {
    if ($a -match '[\s"]') { return '"' + ($a -replace '"', '\"') + '"' }
    return $a
}

# ------------------------------------------------------------------ process lookup

function Get-SensorProcesses {
    # All python processes running hand_sensor.py (the venv python.exe is a launcher with a child interpreter).
    @(Get-CimInstance Win32_Process -Filter "Name='python.exe'" -ErrorAction SilentlyContinue |
        Where-Object { $_.CommandLine -and $_.CommandLine -match 'hand_sensor\.py' })
}

function Get-SensorRoot {
    # Topmost sensor process (its parent is not a sensor too). Returns a System.Diagnostics.Process or $null.
    $all = @(Get-SensorProcesses)
    if ($all.Count -eq 0) { return $null }
    $ids = @($all | ForEach-Object { $_.ProcessId })
    $root = $all | Where-Object { $ids -notcontains $_.ParentProcessId } | Select-Object -First 1
    if ($null -eq $root) { $root = $all[0] }
    try { return Get-Process -Id $root.ProcessId -ErrorAction Stop } catch { return $null }
}

function Get-GameProcess($s) {
    $procs = @(Get-Process -Name $s.gameName -ErrorAction SilentlyContinue)
    foreach ($p in $procs) {
        try { if ($p.Path -and ([System.IO.Path]::GetFullPath($p.Path) -ieq $s.gameExe)) { return $p } } catch { }
    }
    if ($procs.Count -gt 0) { return $procs[0] }  # same name elsewhere (copied build): still one game
    return $null
}

function Stop-Tree([int]$processId) {
    & taskkill.exe /PID $processId /T /F 2>&1 | Out-Null
}

function Test-Alive($p) {
    if ($null -eq $p) { return $false }
    try { $p.Refresh(); return -not $p.HasExited } catch { return $false }
}

# ------------------------------------------------------------------ start

function Start-Sensor($s) {
    if (-not (Test-Path $s.python)) { throw "hand sensor venv not found: $($s.python) (see Tools/HandSensor/README.md 1)" }
    Remove-Item -Force -ErrorAction SilentlyContinue $SensorStop, $SensorStatus
    $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    $out = Join-Path $LogDir "sensor_$stamp.out.log"
    $err = Join-Path $LogDir "sensor_$stamp.err.log"
    $argList = @('hand_sensor.py', '--stop-file', $SensorStop, '--status-file', $SensorStatus)
    if (Test-Path $s.gameConfig) { $argList += @('--config', $s.gameConfig) }
    $argList += @($s.sensorArgs)
    $argString = ($argList | ForEach-Object { Quote ([string]$_) }) -join ' '
    $p = Start-Process -FilePath $s.python -ArgumentList $argString -WorkingDirectory $s.handSensorDir `
        -WindowStyle Hidden -RedirectStandardOutput $out -RedirectStandardError $err -PassThru
    $null = $p.Handle  # keep the handle so ExitCode is available after exit
    Write-Log ("sensor started pid {0}: {1} (log {2})" -f $p.Id, $argString, (Split-Path $out -Leaf))
    return $p
}

function Wait-SensorReady($s, $p) {
    $deadline = (Get-Date).AddSeconds([double]$s.sensorReadyTimeoutSec)
    while ((Get-Date) -lt $deadline) {
        if (-not (Test-Alive $p)) { return 'exited' }
        $st = Read-SensorStatus
        if ($null -ne $st -and $st.state -eq 'running') { return 'running' }
        Start-Sleep -Milliseconds 250
    }
    return 'timeout'
}

function Read-SensorStatus {
    if (-not (Test-Path $SensorStatus)) { return $null }
    try { return Get-Content -Raw $SensorStatus | ConvertFrom-Json } catch { return $null }
}

function Start-Game($s) {
    if (-not (Test-Path $s.gameExe)) { throw "game build not found: $($s.gameExe) (launcher.json gameExe)" }
    $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
    $log = Join-Path $LogDir "game_$stamp.log"
    $argList = @('-logFile', $log) + @($s.gameArgs)
    $argString = ($argList | ForEach-Object { Quote ([string]$_) }) -join ' '
    $p = Start-Process -FilePath $s.gameExe -ArgumentList $argString -WorkingDirectory (Split-Path $s.gameExe -Parent) -PassThru
    $null = $p.Handle
    Write-Log ("game started pid {0}: {1}" -f $p.Id, $argString)
    return $p
}

# ------------------------------------------------------------------ stop

function Restore-Exposure($s) {
    # After a hard kill the camera keeps the manual exposure; open it once with auto and exit.
    if (-not (Test-Path $s.python)) { return }
    $cfg = if (Test-Path $s.gameConfig) { @('--config', $s.gameConfig) } else { @() }
    Write-Log 'restoring camera auto exposure (hand_sensor.py --exposure auto --duration 3)'
    $argString = ((@('hand_sensor.py', '--exposure', 'auto', '--duration', '3', '--no-body', '--quiet') + $cfg) |
        ForEach-Object { Quote ([string]$_) }) -join ' '
    $p = Start-Process -FilePath $s.python -ArgumentList $argString -WorkingDirectory $s.handSensorDir `
        -WindowStyle Hidden -PassThru
    if (-not $p.WaitForExit(30000)) { Stop-Tree $p.Id; Write-Log 'exposure restore did not finish in 30 s' }
}

function Stop-Sensor($s, [string]$why) {
    $root = Get-SensorRoot
    if ($null -eq $root) { return }
    Write-Log "stopping sensor pid $($root.Id) ($why): stop file"
    New-Item -ItemType File -Force -Path $SensorStop | Out-Null
    $deadline = (Get-Date).AddSeconds([double]$s.stopSensorWaitSec)
    while ((Get-Date) -lt $deadline -and @(Get-SensorProcesses).Count -gt 0) { Start-Sleep -Milliseconds 250 }
    if (@(Get-SensorProcesses).Count -eq 0) { Write-Log 'sensor stopped normally'; return }

    # A sensor started by hand has no stop file: closing its console window is a normal stop too (ConsoleStop).
    foreach ($p in @(Get-SensorProcesses | ForEach-Object { Get-Process -Id $_.ProcessId -ErrorAction SilentlyContinue })) {
        if ($p -and $p.MainWindowHandle -ne [IntPtr]::Zero) { [void]$p.CloseMainWindow() }
    }
    $deadline = (Get-Date).AddSeconds(6)
    while ((Get-Date) -lt $deadline -and @(Get-SensorProcesses).Count -gt 0) { Start-Sleep -Milliseconds 250 }
    if (@(Get-SensorProcesses).Count -eq 0) { Write-Log 'sensor stopped (console close)'; return }

    Write-Log 'sensor did not stop - killing'
    foreach ($c in Get-SensorProcesses) { Stop-Tree $c.ProcessId }
    if (Test-ManualExposure $s) { Restore-Exposure $s }
}

function Stop-Game($s, [string]$why) {
    $p = Get-GameProcess $s
    if ($null -eq $p) { return }
    Write-Log "stopping game pid $($p.Id) ($why)"
    [void]$p.CloseMainWindow()
    if ($p.WaitForExit([int]$s.stopGameWaitSec * 1000)) { Write-Log 'game closed'; return }
    Write-Log 'game did not close - killing'
    Stop-Tree $p.Id
}

function Stop-All($s, [string]$why) {
    Stop-Sensor $s $why  # sensor first: its normal stop sets the camera back to auto exposure
    Stop-Game $s $why
    Remove-Item -Force -ErrorAction SilentlyContinue $SensorStop
}

# ------------------------------------------------------------------ watchdog

function Test-WatchdogRunning {
    $m = New-Object System.Threading.Mutex($false, $MutexName)
    $free = $false
    try { $free = $m.WaitOne(0) } catch [System.Threading.AbandonedMutexException] { $free = $true }
    if ($free) { $m.ReleaseMutex() }
    $m.Dispose()
    return -not $free
}

function New-RestartTracker { return @{ times = New-Object System.Collections.ArrayList; count = 0; pauseUntil = [datetime]::MinValue } }

function Test-CanRestart($s, $t, [string]$name) {
    # Crash loop guard: too many restarts in the window -> pause before the next try.
    $now = Get-Date
    if ($now -lt $t.pauseUntil) { return $false }
    while ($t.times.Count -gt 0 -and ($now - $t.times[0]).TotalSeconds -gt [double]$s.crashLoopWindowSec) { $t.times.RemoveAt(0) }
    if ($t.times.Count -ge [int]$s.crashLoopCount) {
        $t.pauseUntil = $now.AddSeconds([double]$s.crashLoopPauseSec)
        $t.times.Clear()
        Write-Log ("{0}: {1} restarts in {2} s - pausing {3} s" -f $name, $s.crashLoopCount, $s.crashLoopWindowSec, $s.crashLoopPauseSec)
        return $false
    }
    [void]$t.times.Add($now)
    $t.count++
    return $true
}

function Invoke-Watch {
    $s = Read-Settings
    $mutex = New-Object System.Threading.Mutex($false, $MutexName)
    $owned = $false
    try { $owned = $mutex.WaitOne(0) } catch [System.Threading.AbandonedMutexException] { $owned = $true }
    if (-not $owned) { Write-Log 'watchdog already running - not starting a second one'; return }

    try {
        Remove-OldLogs $s
        Remove-Item -Force -ErrorAction SilentlyContinue $StopRequest
        Write-Log ("watchdog started pid {0}, game {1}" -f $PID, $s.gameExe)
        if ([double]$s.startDelaySec -gt 0) {
            Write-Log "waiting $($s.startDelaySec) s before start (startDelaySec)"
            $until = (Get-Date).AddSeconds([double]$s.startDelaySec)
            while ((Get-Date) -lt $until -and -not (Test-Path $StopRequest)) { Start-Sleep -Milliseconds 500 }
        }

        $useSensor = Test-ExternalSensor $s
        if (-not $useSensor) { Write-Log 'config sensorMode is not external - hand sensor not started (the game opens the webcam)' }
        $sensorT = New-RestartTracker
        $gameT = New-RestartTracker
        $sensor = $null
        $game = $null
        $sensorStaleSince = $null
        $gameHungSince = $null
        $started = Get-Date

        if ($useSensor -and -not (Test-Path $StopRequest)) {
            $sensor = Get-SensorRoot
            if ($null -ne $sensor) {
                Write-Log "sensor already running pid $($sensor.Id) - using it (no duplicate)"
            } else {
                $sensor = Start-Sensor $s
                $ready = Wait-SensorReady $s $sensor
                Write-Log "sensor ready check: $ready"
            }
        }
        if (-not (Test-Path $StopRequest)) {
            $game = Get-GameProcess $s
            if ($null -ne $game) { Write-Log "game already running pid $($game.Id) - using it (no duplicate)" }
            else { $game = Start-Game $s }
        }

        while (-not (Test-Path $StopRequest)) {
            $now = Get-Date
            if ($useSensor) {
                if (-not (Test-Alive $sensor)) {
                    $other = Get-SensorRoot  # someone started one by hand: adopt it instead of a duplicate
                    if ($null -ne $other) {
                        $sensor = $other
                        Write-Log "sensor pid $($sensor.Id) found - adopting"
                    } else {
                        $code = if ($null -ne $sensor) { try { $sensor.ExitCode } catch { '?' } } else { '-' }
                        if (Test-CanRestart $s $sensorT 'sensor') {
                            Write-Log ("sensor exited (code {0}) - restart #{1}" -f $code, $sensorT.count)
                            $sensor = Start-Sensor $s
                        }
                    }
                    $sensorStaleSince = $null
                } else {
                    $st = Read-SensorStatus
                    $age = if ($null -ne $st) { ([DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds() / 1000.0) - [double]$st.time } else { 0 }
                    if ($null -ne $st -and $st.state -eq 'running' -and $age -gt [double]$s.sensorHangSec) {
                        Write-Log ("sensor heartbeat {0:N0} s old - killing hung sensor pid {1}" -f $age, $sensor.Id)
                        Stop-Tree $sensor.Id
                        Remove-Item -Force -ErrorAction SilentlyContinue $SensorStatus
                    }
                }
            }

            if (-not (Test-Alive $game)) {
                $other = Get-GameProcess $s
                if ($null -ne $other) {
                    $game = $other
                    Write-Log "game pid $($game.Id) found - adopting"
                } else {
                    $code = if ($null -ne $game) { try { $game.ExitCode } catch { '?' } } else { '-' }
                    if (Test-CanRestart $s $gameT 'game') {
                        Write-Log ("game exited (code {0}) - restart #{1}" -f $code, $gameT.count)
                        $game = Start-Game $s
                    }
                }
                $gameHungSince = $null
            } elseif (-not $game.Responding) {
                if ($null -eq $gameHungSince) { $gameHungSince = $now }
                elseif (($now - $gameHungSince).TotalSeconds -gt [double]$s.gameHangSec) {
                    Write-Log ("game not responding for {0} s - killing pid {1}" -f $s.gameHangSec, $game.Id)
                    Stop-Tree $game.Id
                    $gameHungSince = $null
                }
            } else {
                $gameHungSince = $null
            }

            Save-State ([ordered]@{
                watchdogPid    = $PID
                startedAt      = $started.ToString('s')
                updatedAt      = $now.ToString('s')
                sensorPid      = if (Test-Alive $sensor) { $sensor.Id } else { $null }
                gamePid        = if (Test-Alive $game) { $game.Id } else { $null }
                sensorRestarts = $sensorT.count
                gameRestarts   = $gameT.count
                useSensor      = $useSensor
            })
            Start-Sleep -Seconds ([double]$s.checkIntervalSec)
        }

        Write-Log 'stop requested'
        Stop-All $s 'stop request'
        Write-Log ("watchdog exit: sensor restarts {0}, game restarts {1}" -f $sensorT.count, $gameT.count)
    } finally {
        Remove-Item -Force -ErrorAction SilentlyContinue $StopRequest
        $mutex.ReleaseMutex()
        $mutex.Dispose()
    }
}

# ------------------------------------------------------------------ actions

switch ($Action) {
    'start' {
        if (Test-WatchdogRunning) { Write-Log 'start: watchdog already running - nothing to do'; break }
        $s = Read-Settings
        if (-not (Test-Path $s.gameExe)) { Write-Log "start: game build not found: $($s.gameExe)"; exit 1 }
        $ps = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
        $argString = '-NoProfile -ExecutionPolicy Bypass -File {0} watch' -f (Quote (Join-Path $Here 'exhibition.ps1'))
        if ($Settings) { $argString += ' -Settings ' + (Quote $Settings) }
        if ($DelaySec -ge 0) { $argString += ' -DelaySec ' + $DelaySec }
        $w = Start-Process -FilePath $ps -ArgumentList $argString -WindowStyle Hidden -PassThru
        Write-Log "start: watchdog launched pid $($w.Id) (log $LauncherLog)"
    }
    'watch' { Invoke-Watch }
    'stop' {
        $s = Read-Settings
        if (Test-WatchdogRunning) {
            Write-Log 'stop: asking the watchdog to stop'
            New-Item -ItemType File -Force -Path $StopRequest | Out-Null
            $deadline = (Get-Date).AddSeconds([double]$s.stopSensorWaitSec + [double]$s.stopGameWaitSec + 30)
            while ((Get-Date) -lt $deadline -and (Test-WatchdogRunning)) { Start-Sleep -Milliseconds 500 }
            if (Test-WatchdogRunning) { Write-Log 'stop: watchdog did not exit in time - stopping processes directly' }
        }
        # Covers no watchdog (processes started by hand) and a watchdog that did not finish.
        Stop-All $s 'stop script'
        Remove-Item -Force -ErrorAction SilentlyContinue $StopRequest
        Write-Log 'stop: done'
    }
    'status' {
        $s = Read-Settings
        Write-Host ('watchdog : {0}' -f $(if (Test-WatchdogRunning) { 'running' } else { 'not running' }))
        $sr = Get-SensorRoot
        $st = Read-SensorStatus
        Write-Host ('sensor   : {0}' -f $(if ($sr) { "pid $($sr.Id)" } else { 'not running' }))
        if ($st) { Write-Host ('           status {0}, fps {1}, frames {2}, exposure {3}' -f $st.state, $st.fps, $st.frames, $st.exposure) }
        $g = Get-GameProcess $s
        Write-Host ('game     : {0}' -f $(if ($g) { "pid $($g.Id), responding $($g.Responding)" } else { 'not running' }))
        Write-Host ('build    : {0} ({1})' -f $s.gameExe, $(if (Test-Path $s.gameExe) { 'found' } else { 'MISSING' }))
        if (Test-Path $StateFile) {
            $state = Get-Content -Raw $StateFile | ConvertFrom-Json
            Write-Host ('restarts : sensor {0}, game {1} (since {2})' -f $state.sensorRestarts, $state.gameRestarts, $state.startedAt)
        }
        if (Test-Path $LauncherLog) { Write-Host '--- log'; Get-Content -Tail 10 $LauncherLog | ForEach-Object { Write-Host $_ } }
    }
}
