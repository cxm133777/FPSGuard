$ErrorActionPreference = 'Stop'
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\FpsGuard'
$exe = Join-Path $installDir 'FpsGuard.exe'
if (Test-Path -LiteralPath $exe) {
    Start-Process -FilePath $exe -ArgumentList '--exit' -WindowStyle Hidden -Wait
    $deadline = (Get-Date).AddSeconds(10)
    do {
        $running = @(Get-Process -Name FpsGuard -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe })
        if ($running.Count -eq 0) { break }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    if ($running.Count -gt 0) { throw '现有实例未能退出，请从托盘菜单退出后再安装。' }
}
New-Item -ItemType Directory -Path $installDir -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'bin\FpsGuard.exe') -Destination $exe -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'uninstall.ps1') -Destination (Join-Path $installDir 'uninstall.ps1') -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '使用说明.md') -Destination (Join-Path $installDir '使用说明.md') -Force
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if (-not (Test-Path -LiteralPath $runKey)) { New-Item -Path $runKey | Out-Null }
New-ItemProperty -Path $runKey -Name 'FpsGuard' -Value ('"' + $exe + '" --background') -PropertyType String -Force | Out-Null
$shell = New-Object -ComObject WScript.Shell
$desktop = [Environment]::GetFolderPath('Desktop')
$shortcut = $shell.CreateShortcut((Join-Path $desktop 'FPS 轻守.lnk'))
$shortcut.TargetPath = $exe
$shortcut.WorkingDirectory = $installDir
$shortcut.Description = '轻量 FPS 游戏守护与闲置内存清理'
$shortcut.Save()
$startDir = Join-Path ([Environment]::GetFolderPath('Programs')) 'FPS 轻守'
New-Item -ItemType Directory -Path $startDir -Force | Out-Null
$shortcut = $shell.CreateShortcut((Join-Path $startDir 'FPS 轻守.lnk'))
$shortcut.TargetPath = $exe
$shortcut.Save()
$uninstall = Join-Path $installDir 'uninstall.ps1'
$powershell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$uninstallCommand = '"' + $powershell + '" -NoProfile -ExecutionPolicy Bypass -File "' + $uninstall + '"'
$uninstallKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\FpsGuard'
New-Item -Path $uninstallKey -Force | Out-Null
$values = @{ DisplayName='FPS 轻守'; DisplayVersion='3.1.0'; Publisher='本地定制工具'; InstallLocation=$installDir; DisplayIcon=$exe; UninstallString=$uninstallCommand }
foreach ($name in $values.Keys) { New-ItemProperty -Path $uninstallKey -Name $name -Value $values[$name] -PropertyType String -Force | Out-Null }
New-ItemProperty -Path $uninstallKey -Name 'NoModify' -Value 1 -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name 'NoRepair' -Value 1 -PropertyType DWord -Force | Out-Null
New-ItemProperty -Path $uninstallKey -Name 'EstimatedSize' -Value 128 -PropertyType DWord -Force | Out-Null
Start-Process -FilePath $exe -ArgumentList '--background' -WindowStyle Hidden
Write-Output "已安装并开启登录自启：$exe"
