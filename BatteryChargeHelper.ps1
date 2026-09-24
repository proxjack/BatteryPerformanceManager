<#
Persistent elevated helper for Battery and Performance Manager.

Why it exists: launching a NEW elevated PowerShell process on every profile
switch (what the previous approach did, one Scheduled Task per profile) costs
about 10-12 seconds on this machine - not because of the DellBIOSProvider
module itself (Import-Module + mounting DellSmbios: take a few milliseconds),
but because of the generic overhead of creating an elevated process (verified:
identical when launching manually with "Run as administrator", so independent
of the Task Scheduler - most likely real-time antivirus scanning of a freshly
created elevated process).

This script starts ONCE (through the '\BatteryChargeManager\Helper' Scheduled
Task, started on demand by the tray app on the first switch) and keeps
listening on a local named pipe for the rest of the login session, applying
switches in-process. It exits on its own at logoff (Scheduled Tasks with
LogonType=Interactive are terminated automatically by Windows at logoff).

It doesn't replace Set-DellBatteryChargeProfile.ps1 (which stays usable from
the command line for manual testing) - it duplicates its validation/write
logic because that script is meant for a single run and ends with exit,
while this one has to loop indefinitely.

It also handles the Dell Optimizer thermal mode (Optimized/Cool/Quiet/Ultra).
Protocol: one line per request - a charge profile id ('60_65', ...) or
'thermal:<mode>' ('thermal:quiet', ...). Response: 'OK' or 'ERROR: ...'.
#>

$ErrorActionPreference = 'Stop'
$PipeName = 'BatteryChargeManagerHelper'
$LogPath = Join-Path $PSScriptRoot 'dell-battery-charge-log.jsonl'
$ThermalLogPath = Join-Path $PSScriptRoot 'dell-thermal-mode-log.jsonl'

# The thermal mode is NOT exposed by DellBIOSProvider on this XPS 14
# (DellSmbios:\PowerManagement\ThermalManagement doesn't exist): Dell Optimizer
# sets it through the SMBIOS interface ("User Selectable Thermal Tables") and
# keeps it in sync with the Windows power mode. Going through its official CLI
# (which requires admin, so it's fine here) has exactly the same effect as a
# click in the Dell Optimizer UI, sync included.
$DoCliPath = Join-Path $env:ProgramFiles 'Dell\DellOptimizer\do-cli.exe'
$DoCliTimeoutMs = 60000

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-IsAdministrator)) {
    Write-Error "This script must be run as Administrator."
    exit 1
}

if (-not (Get-Module -ListAvailable -Name DellBIOSProvider)) {
    Write-Error "The DellBIOSProvider module is not installed. Run first: Install-Module -Name DellBIOSProvider -Scope AllUsers -Force"
    exit 1
}

Import-Module DellBIOSProvider

if (-not (Test-Path DellSmbios:\PowerManagement)) {
    Write-Error "The DellSmbios:\PowerManagement path is not available on this BIOS."
    exit 1
}

function Get-CurrentChargeState {
    [PSCustomObject]@{
        Timestamp            = [datetime]::UtcNow.ToString('o')
        PrimaryBattChargeCfg = (Get-Item DellSmbios:\PowerManagement\PrimaryBattChargeCfg -ErrorAction SilentlyContinue).CurrentValue
        CustomChargeStart    = (Get-Item DellSmbios:\PowerManagement\CustomChargeStart -ErrorAction SilentlyContinue).CurrentValue
        CustomChargeStop     = (Get-Item DellSmbios:\PowerManagement\CustomChargeStop -ErrorAction SilentlyContinue).CurrentValue
    }
}

function Set-ChargeProfile {
    param([Parameter(Mandatory = $true)][string]$Profile)

    switch ($Profile) {
        '60_65'      { $cfg = 'Custom'; $start = 60; $stop = 65 }
        '75_80'      { $cfg = 'Custom'; $start = 75; $stop = 80 }
        'standard'   { $cfg = 'Standard'; $start = $null; $stop = $null }
        'fastcharge' { $cfg = 'Express'; $start = $null; $stop = $null }
        default      { throw "Unknown profile: '$Profile'" }
    }

    if ($cfg -eq 'Custom') {
        if ($start -lt 50 -or $start -gt 95) { throw "CustomChargeStart out of range (50-95): $start" }
        if ($stop -lt 55 -or $stop -gt 100) { throw "CustomChargeStop out of range (55-100): $stop" }
        if (($stop - $start) -lt 5) { throw "Start and stop must be at least 5 percentage points apart (current: $($stop - $start))" }
    }

    $before = Get-CurrentChargeState
    ($before | ConvertTo-Json -Compress) | Add-Content -Path $LogPath

    Set-Item -Path DellSmbios:\PowerManagement\PrimaryBattChargeCfg -Value $cfg

    if ($cfg -eq 'Custom') {
        # The BIOS validates each single write against the CURRENT value of the
        # other bound (not against the final pair): see Set-DellBatteryChargeProfile.ps1
        # for the full explanation. Same fix here: try one order, and if it fails
        # (the range is moving in the opposite direction) try the other one.
        try {
            Set-Item -Path DellSmbios:\PowerManagement\CustomChargeStop -Value $stop
            Set-Item -Path DellSmbios:\PowerManagement\CustomChargeStart -Value $start
        } catch {
            Set-Item -Path DellSmbios:\PowerManagement\CustomChargeStart -Value $start
            Set-Item -Path DellSmbios:\PowerManagement\CustomChargeStop -Value $stop
        }
    }

    $after = Get-CurrentChargeState
    ($after | ConvertTo-Json -Compress) | Add-Content -Path $LogPath
}

function Set-ThermalMode {
    param([Parameter(Mandatory = $true)][string]$Mode)

    # Whitelist: only these values ever reach the do-cli command line.
    # They are the exact names Dell Optimizer uses (see its Service.log).
    switch ($Mode) {
        'optimized' { $value = 'Optimized' }
        'cool'      { $value = 'Cool' }
        'quiet'     { $value = 'Quiet' }
        'ultra'     { $value = 'Ultra' }
        default     { throw "Unknown thermal mode: '$Mode'" }
    }

    if (-not (Test-Path $DoCliPath)) {
        throw "Dell Optimizer CLI not found at '$DoCliPath': is Dell Optimizer installed?"
    }

    # Process instead of '& do-cli 2>&1': with $ErrorActionPreference='Stop', in
    # Windows PowerShell 5.1 a line on a native executable's stderr becomes an
    # exception. It also gives us a timeout.
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $DoCliPath
    $psi.Arguments = "/configure -name=SystemPowerConfiguration.ThermalMode -value=$value"
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true

    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $process = [Diagnostics.Process]::Start($psi)
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit($DoCliTimeoutMs)) {
        $process.Kill()
        throw "do-cli did not respond within $($DoCliTimeoutMs / 1000) seconds."
    }
    $process.WaitForExit()

    # Single line: the response over the pipe is line-based.
    $output = (($stdout.Result + ' ' + $stderr.Result) -replace '\s+', ' ').Trim()

    [PSCustomObject]@{
        Timestamp  = [datetime]::UtcNow.ToString('o')
        Requested  = $value
        ExitCode   = $process.ExitCode
        DurationMs = $stopwatch.ElapsedMilliseconds
        Output     = $output
    } | ConvertTo-Json -Compress | Add-Content -Path $ThermalLogPath

    if ($process.ExitCode -ne 0) {
        throw "do-cli returned exit code $($process.ExitCode): $output"
    }
}

Add-Type -AssemblyName System.Core

# Named pipe restricted to the current user only: no other local process/user
# can send commands to this elevated helper.
$currentUserSid = [Security.Principal.WindowsIdentity]::GetCurrent().User
$pipeSecurity = New-Object System.IO.Pipes.PipeSecurity
$rule = New-Object System.IO.Pipes.PipeAccessRule($currentUserSid, [System.IO.Pipes.PipeAccessRights]::ReadWrite, [System.Security.AccessControl.AccessControlType]::Allow)
$pipeSecurity.AddAccessRule($rule)

Write-Host "Helper started, listening on pipe '$PipeName'. It stays active until logoff."

while ($true) {
    $pipe = $null
    try {
        $pipe = New-Object System.IO.Pipes.NamedPipeServerStream(
            $PipeName,
            [System.IO.Pipes.PipeDirection]::InOut,
            1,
            [System.IO.Pipes.PipeTransmissionMode]::Byte,
            [System.IO.Pipes.PipeOptions]::None,
            1024, 1024,
            $pipeSecurity)

        $pipe.WaitForConnection()

        $reader = New-Object System.IO.StreamReader($pipe)
        $writer = New-Object System.IO.StreamWriter($pipe)
        $writer.AutoFlush = $true

        $request = $reader.ReadLine()
        try {
            if ($request -like 'thermal:*') {
                Set-ThermalMode -Mode $request.Substring('thermal:'.Length)
            } else {
                Set-ChargeProfile -Profile $request
            }
            $writer.WriteLine("OK")
        } catch {
            $writer.WriteLine("ERROR: $($_.Exception.Message)")
        }
    } catch {
        # Another helper instance is already listening on the same pipe, or a
        # client disconnected halfway: exit if the pipe is already taken,
        # otherwise start over with the next iteration.
        if ($_.Exception -is [System.IO.IOException] -and $_.Exception.Message -match 'already exist') {
            Write-Host "Another helper instance is already listening. Exiting."
            exit 0
        }
    } finally {
        if ($pipe) { $pipe.Dispose() }
    }
}
