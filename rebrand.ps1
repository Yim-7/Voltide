# rebrand.ps1 - 把本项目改造成你自己的品牌（基于原项目 MIT 许可证）。
#
# 用法：
#   .\rebrand.ps1 -NewName MyTool -Author "Your Name"
#   或不带参数运行，会提示你输入。
#
# 合规：本脚本不会删除或修改 LICENSE，原作者 Joko 的 MIT 版权声明会保留。
param(
    [string]$NewName = "",
    [string]$Author  = ""
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

if ([string]::IsNullOrWhiteSpace($NewName)) { $NewName = Read-Host "请输入新软件名称（英文/拼音，如 MyTool）" }
if ([string]::IsNullOrWhiteSpace($Author))  { $Author  = Read-Host "请输入你的署名（用于版权，可填英文名）" }
$NewName = $NewName.Trim()
$Author  = $Author.Trim()

if ($NewName -notmatch '^[A-Za-z0-9 _-]+$') { throw "名称只能含字母、数字、空格、下划线或连字符。" }
if ($NewName -ieq 'Voltide') { Write-Host "名称与原名相同，无需改名。" -ForegroundColor Yellow; return }

function Replace-InFile([string]$rel, [hashtable]$map) {
    $path = Join-Path $root $rel
    $text = Get-Content $path -Raw -Encoding UTF8
    foreach ($k in $map.Keys) {
        if (-not $text.Contains($k)) { throw "在 $rel 中找不到预期文本：$k" }
        $text = $text.Replace($k, $map[$k])
    }
    Set-Content $path $text -Encoding UTF8 -NoNewline
}

# 1) 集中品牌常量 src/Core/AppBrand.cs
Replace-InFile 'src/Core/AppBrand.cs' @{
    'public const string Name = "Voltide";'        = "public const string Name = `"$NewName`";"
    'public const string ExeName = "Voltide.exe";' = "public const string ExeName = `"$NewName.exe`";"
    'public const string MutexId = "Voltide-SingleInstance";' = "public const string MutexId = `"$NewName-SingleInstance`";"
    'public const string SettingsKey = @"Software\Voltide";'  = "public const string SettingsKey = @`"Software\$NewName`";"
    'public const string ErrorLog = "Voltide-error.txt";'      = "public const string ErrorLog = `"$NewName-error.txt`";"
}

# 2) 程序集信息（资源管理器右键属性可见）
Replace-InFile 'src/Properties/AssemblyInfo.cs' @{
    '[assembly: AssemblyTitle("Voltide")]'   = "[assembly: AssemblyTitle(`"$NewName`")]"
    '[assembly: AssemblyProduct("Voltide")]' = "[assembly: AssemblyProduct(`"$NewName`")]"
    '[assembly: AssemblyCompany("Joko")]'    = "[assembly: AssemblyCompany(`"$Author`")]"
    '[assembly: AssemblyCopyright("© 2026 Joko")]' = "[assembly: AssemblyCopyright(`"© 2026 $Author; based on Voltide by Joko`")]"
}

# 3) build.ps1 输出文件名
Replace-InFile 'build.ps1' @{
    "Join-Path `$root 'Voltide.exe'"      = "Join-Path `$root '$NewName.exe'"
    "Join-Path `$root 'Voltide-Test.exe'" = "Join-Path `$root '$NewName-Test.exe'"
}

# 4) 处理旧的构建产物：重命名旧 exe（若存在），交由你重新编译确认。
foreach ($pair in @(@('Voltide.exe', "$NewName.exe"), @('Voltide-Test.exe', "$NewName-Test.exe"))) {
    $old = Join-Path $root $pair[0]
    $new = Join-Path $root $pair[1]
    if (Test-Path $old) {
        if (Test-Path $new) { Remove-Item $new -Force }
        Rename-Item $old $pair[1]
    }
}

Write-Host ""
Write-Host "品牌已改为：$NewName（作者 $Author）" -ForegroundColor Green
Write-Host "下一步："
Write-Host "  1) 运行 .\build.ps1 重新编译，确认 0 错误。"
Write-Host "  2) 修改 README.md 标题与简介，并加一行：Based on Voltide by Joko (MIT)."
Write-Host "  3) 运行 .\sign.ps1 给新 exe 签名（可选，消除 SmartScreen）。"
Write-Host "  4) 按 PUBLISH.md 的步骤推送到 GitHub。"
Write-Host "LICENSE 保持原样，符合 MIT 要求。"
