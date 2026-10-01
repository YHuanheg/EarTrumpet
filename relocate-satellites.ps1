# 把编译输出里"一个语言一个文件夹"的附属程序集收进 Language\ 子目录。
#
# 为什么需要这一步:.NET 只在 <程序目录>\<语言>\ 下查找附属程序集,而且这条路径**不经过**
# AppDomain.AssemblyResolve —— 实测过:在解析器里返回搬走后的程序集会被直接忽略,界面回落到英文。
# 所以搬完之后必须由 LanguageFolderResourceManager 接管资源加载(见该类的注释)。
#
# 幂等:已经搬过的不会再动;源目录为空才会被删掉。
# 用法:relocate-satellites.ps1 [-OutputPath <编译输出目录>]

param([string]$OutputPath = "")

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $PSScriptRoot "Build\Release"
}
$OutputPath = $OutputPath.TrimEnd('\', '/')

if (-not (Test-Path $OutputPath)) {
    Write-Host "  (跳过) 输出目录不存在: $OutputPath"
    exit 0
}

$languageDir = Join-Path $OutputPath "Language"
$moved = 0

foreach ($dir in @(Get-ChildItem $OutputPath -Directory)) {
    if ($dir.Name -eq "Language") { continue }
    # 语言目录的名字形如 zh-CN / bs-latn-ba;再加一道"里面确实是 EarTrumpet 的卫星"的校验,
    # 免得误伤 Assets 之类的目录。
    if ($dir.Name -notmatch '^[a-z]{2,3}(-[A-Za-z]{2,4})*$') { continue }
    if (-not (Test-Path (Join-Path $dir.FullName "EarTrumpet.resources.dll"))) { continue }

    if (-not (Test-Path $languageDir)) {
        New-Item -ItemType Directory -Path $languageDir -Force | Out-Null
    }

    $target = Join-Path $languageDir $dir.Name
    if (Test-Path $target) {
        # 目标已经在了(重复构建会重新生成源目录),合并内容后删掉源目录
        Copy-Item (Join-Path $dir.FullName "*") $target -Recurse -Force
        Remove-Item $dir.FullName -Recurse -Force
    }
    else {
        Move-Item $dir.FullName $target
    }
    $moved++
}

$total = @(Get-ChildItem $languageDir -Directory -ErrorAction SilentlyContinue).Count
if ($moved -gt 0) {
    Write-Host "  语言文件夹已归集到 Language\ (本次处理 $moved 个, 现有 $total 个)"
}
else {
    Write-Host "  语言文件夹已在 Language\ 下 (共 $total 个)"
}
