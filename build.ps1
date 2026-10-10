$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$src = Join-Path $PSScriptRoot 'src'
$out = Join-Path $PSScriptRoot 'bin'
New-Item -ItemType Directory -Path $out -Force | Out-Null
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 "/out:$out\FpsGuard.exe" "/win32manifest:$src\app.manifest" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.Xml.dll "$src\Core.cs" "$src\Native.cs" "$src\GameDiscovery.cs" "$src\FrameAnalysis.cs" "$src\Program.cs" "$src\Dashboard.cs" "$src\ModernUI.cs" "$src\DpiLayout.cs" "$src\AssemblyInfo.cs"
if ($LASTEXITCODE -ne 0) { throw '应用编译失败' }
& $compiler /nologo /target:exe /platform:x64 /optimize+ /codepage:65001 "/out:$out\FpsGuard.Tests.exe" /r:System.Core.dll /r:System.Xml.dll "$src\Core.cs" "$src\Native.cs" "$src\GameDiscovery.cs" "$src\FrameAnalysis.cs" "$src\Tests.cs"
if ($LASTEXITCODE -ne 0) { throw '测试编译失败' }
& "$out\FpsGuard.Tests.exe"
if ($LASTEXITCODE -ne 0) { throw '测试失败' }
Get-Item -LiteralPath "$out\FpsGuard.exe" | Select-Object Name,Length,LastWriteTime
