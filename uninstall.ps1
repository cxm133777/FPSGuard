$ErrorActionPreference = 'Stop'
$installDir = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\FpsGuard'))
$expected = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs'))
if ((Split-Path -Parent $installDir) -ne $expected -or (Split-Path -Leaf $installDir) -ne 'FpsGuard') { throw '安装路径校验失败。' }
$exe = Join-Path $installDir 'FpsGuard.exe'
if (Test-Path -LiteralPath $exe) {
    Start-Process -FilePath $exe -ArgumentList '--exit' -WindowStyle Hidden -Wait
    $deadline = (Get-Date).AddSeconds(10)
    do {
        $running = @(Get-Process -Name FpsGuard -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $exe })
        if ($running.Count -eq 0) { break }
        Start-Sleep -Milliseconds 250
    } while ((Get-Date) -lt $deadline)
    if ($running.Count -gt 0) { throw '程序尚未退出，未执行卸载。请从托盘菜单退出后重试。' }
}
$recoveryPath = Join-Path $env:LOCALAPPDATA 'FpsGuard\recovery.xml'
if (Test-Path -LiteralPath $recoveryPath) {
    [xml]$recovery = Get-Content -LiteralPath $recoveryPath -Raw
    if ($recovery.RecoveryState.WallpaperStopped -eq 'true' -or $recovery.SelectNodes('//PriorityChange').Count -gt 0) {
        throw '仍有待恢复的优化记录。请重新打开 FPS 轻守，暂停守护后退出，再卸载。'
    }
}
Remove-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name FpsGuard -ErrorAction SilentlyContinue
if (Test-Path -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\FpsGuard') {
    Remove-Item -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\FpsGuard'
}
$links = @((Join-Path ([Environment]::GetFolderPath('Desktop')) 'FPS 轻守.lnk'), (Join-Path ([Environment]::GetFolderPath('Programs')) 'FPS 轻守\FPS 轻守.lnk'))
foreach ($link in $links) { if (Test-Path -LiteralPath $link) { Remove-Item -LiteralPath $link } }
foreach ($name in @('FpsGuard.exe', '使用说明.md', 'uninstall.ps1')) {
    $target = Join-Path $installDir $name
    if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target }
}
Write-Output 'FPS 轻守已卸载；本地日志、设置及桌面源码文件夹保留。'
