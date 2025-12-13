# Setting Up Automated Maintenance

## Windows Task Scheduler Configuration

### Create Scheduled Task

```powershell
# Run as Administrator
$action = New-ScheduledTaskAction -Execute "powershell.exe" `
    -Argument "-ExecutionPolicy Bypass -File D:\Repositories\Hartonomous\scripts\scheduled_maintenance.ps1" `
    -WorkingDirectory "D:\Repositories\Hartonomous"

$trigger = New-ScheduledTaskTrigger -Daily -At "2:00AM"

$principal = New-ScheduledTaskPrincipal -UserId "SYSTEM" -LogonType ServiceAccount

$settings = New-ScheduledTaskSettingsSet `
    -StartWhenAvailable `
    -RunOnlyIfNetworkAvailable `
    -DontStopIfGoingOnBatteries `
    -AllowStartIfOnBatteries

Register-ScheduledTask `
    -TaskName "Hartonomous_Maintenance" `
    -Action $action `
    -Trigger $trigger `
    -Principal $principal `
    -Settings $settings `
    -Description "Daily backup, VACUUM, and health checks for Hartonomous database"
```

### Verify Task Created

```powershell
Get-ScheduledTask -TaskName "Hartonomous_Maintenance"
```

### Manual Test Run

```powershell
Start-ScheduledTask -TaskName "Hartonomous_Maintenance"

# Check logs
Get-Content .\logs\maintenance_$(Get-Date -Format 'yyyyMMdd').log -Tail 50
```

### Monitor Task History

```powershell
# View last run result
Get-ScheduledTaskInfo -TaskName "Hartonomous_Maintenance" | 
    Select-Object LastRunTime, LastTaskResult, NextRunTime
```

## Alternative: Cron (WSL/Linux)

```bash
# Add to crontab
crontab -e

# Run at 2 AM daily
0 2 * * * cd /mnt/d/Repositories/Hartonomous && pwsh -File scripts/scheduled_maintenance.ps1
```

## Monitoring Alerts

### Email Notifications (PowerShell)

Add to `scheduled_maintenance.ps1`:

```powershell
if ($exitCode -ne 0) {
    Send-MailMessage `
        -From "hartonomous@yourserver.com" `
        -To "admin@yourserver.com" `
        -Subject "Hartonomous Maintenance FAILED" `
        -Body (Get-Content $logFile -Raw) `
        -SmtpServer "smtp.yourserver.com"
}
```

### Event Log Integration

```powershell
# Write to Windows Event Log
New-EventLog -Source "Hartonomous" -LogName "Application" -ErrorAction SilentlyContinue
Write-EventLog -LogName "Application" -Source "Hartonomous" -EventId 1000 -EntryType Information -Message "Maintenance completed"
```
