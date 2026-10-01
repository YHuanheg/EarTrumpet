#Requires -Version 5.1
<#
.SYNOPSIS
    把 Build\Release 里的 C# EarTrumpet 打成 MSIX 安装包和便携版 zip。

.DESCRIPTION
    先用 build-release.bat 编译,再跑这个脚本。

    * MSIX:手工组装布局(目录清单 + 图标 + 程序),用 Windows SDK 的 makeappx 打包,
      然后用自签名证书签名。清单里的身份/发布者是**本分支自己的**(YHuanheg.EarTrumpet /
      CN=YHuanheg),所以装上去和官方版是**并列**关系,不会覆盖官方版 ——
      机器上若还装着官方版或旧身份的本分支包,需要先卸载。
    * 便携版 zip:把同一份产物压成免安装包,附一份中文说明。

.PARAMETER CertThumbprint
    指定用于签名的证书指纹(在 Cert:\CurrentUser\My 里找)。不给就按清单里的发布者名
    自动查找或新建一张自签证书。

.PARAMETER SkipSign
    只打包不签名(签名需要用 SDK 的 signtool)。

.EXAMPLE
    .\make-package.ps1
#>
[CmdletBinding()]
param(
    [string]$CertThumbprint = "",
    [switch]$SkipSign
)

$ErrorActionPreference = "Stop"
# Compress-Archive 会往宿主控制台狂刷进度条(每次几百行),这里直接关掉
$ProgressPreference = "SilentlyContinue"
$repo = $PSScriptRoot
$release = Join-Path $repo "Build\Release"
$packageProject = Join-Path $repo "EarTrumpet.Package"
$outDir = Join-Path $repo "Build\Package"
$layout = Join-Path $repo "Build\MsixLayout"

if (-not (Test-Path (Join-Path $release "EarTrumpet.exe"))) {
    throw "找不到 $release\EarTrumpet.exe —— 请先运行 build-release.bat 编译。"
}

# 语言文件夹(必须在打包前归集好,否则三种包都会带着 32 个顶层目录)
Write-Host "== 0) 归集语言文件夹到 Language\ ==" -ForegroundColor Cyan
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo "relocate-satellites.ps1") -OutputPath $release
if ($LASTEXITCODE -ne 0) { throw "relocate-satellites.ps1 失败" }

# 硬校验:非英文语言必须真的能取到译文。Resources.Designer.cs 一旦被 Visual Studio
# 重新生成(改 .resx 时会触发),它就退回标准 ResourceManager,界面会**静默**变英文 ——
# 这个检查把那种回归变成打包失败。
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $repo "verify-localization.ps1") -AppDirectory $release -RequireRelocated
if ($LASTEXITCODE -ne 0) { throw "本地化校验未通过 —— 拒绝打包(界面会退化成纯英文)" }

# 版本号以编译产物为准(GitVersion 从 git 历史推出来),再回写清单,不靠人记。
# 两个理由:
#   ① 包名、exe 属性、「设置 → 应用」里显示的版本号必须是同一个,写死清单就会各说各话;
#   ② **MSIX 升级要求新包版本号更大**,清单写死的话每版都是同一个号,新版根本装不上。
Write-Host "== 0.5) 同步版本号 ==" -ForegroundColor Cyan
# 注意读的是 ProductVersion,不是 FileVersion:GitVersion 的 assembly-versioning-scheme
# 是 MajorMinorPatchTag,FileVersion 恒为 x.y.z.0,提交数只出现在 ProductVersion 里
# (形如 2.3.0-ci.137+Branch.master.Sha.…)。读 FileVersion 会得到 2.3.0.0 ——
# 比已安装的版本还小,MSIX 会直接拒绝升级。
$productVersion = (Get-Item (Join-Path $release "EarTrumpet.exe")).VersionInfo.ProductVersion
$head = ($productVersion -split '\+')[0]
$nums = @([regex]::Matches($head, '\d+') | ForEach-Object { [int]$_.Value })
if ($nums.Count -lt 3) {
    throw "从 ProductVersion 解析不出版本号:'$productVersion'"
}
$build = if ($nums.Count -ge 4) { $nums[3] } else { 0 }
$version = "$($nums[0]).$($nums[1]).$($nums[2]).$build"
$manifestPath = Join-Path $packageProject "Package.appxmanifest"
$manifest = Get-Content $manifestPath -Raw -Encoding UTF8
$manifestVersion = [regex]::Match($manifest, '<Identity[^>]*Version="([^"]+)"').Groups[1].Value
if ($manifestVersion -ne $version) {
    $manifest = $manifest -replace '(<Identity[^>]*Version=")[^"]+(")', ('${1}' + $version + '${2}')
    Set-Content $manifestPath -Value $manifest -Encoding UTF8 -NoNewline
    Write-Host "  编译产物 $version;清单 $manifestVersion -> 已回写"
    if ($manifestVersion -and ([version]$version -le [version]$manifestVersion)) {
        Write-Host "  [i] 版本号没有变大。MSIX 升级需要更大的版本号 —— 提交一次改动(GitVersion 的末位是距上个 tag 的提交数)再打包。" -ForegroundColor Yellow
    }
}
else {
    Write-Host "  版本号 $version(清单已一致)"
}

Write-Host "== 1) 定位 Windows SDK 工具 ==" -ForegroundColor Cyan
$sdkRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
$makeappx = Get-ChildItem $sdkRoot -Directory -ErrorAction SilentlyContinue |
    Sort-Object Name -Descending |
    ForEach-Object { Join-Path $_.FullName "x64\makeappx.exe" } |
    Where-Object { Test-Path $_ } | Select-Object -First 1
$signtool = Get-ChildItem $sdkRoot -Directory -ErrorAction SilentlyContinue |
    Sort-Object Name -Descending |
    ForEach-Object { Join-Path $_.FullName "x64\signtool.exe" } |
    Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $makeappx) { throw "找不到 makeappx.exe —— 需要安装 Windows SDK(本机可从 VS Installer 补装)。" }
Write-Host "  makeappx : $makeappx"
Write-Host "  signtool : $signtool"

Write-Host "== 2) 组装 MSIX 布局 ==" -ForegroundColor Cyan
# 清空旧布局;若删除被安全策略拦住(受限会话会把删除重定向到回收站并失败),就退化成
# 就地覆盖 —— 布局内容每次都由同一份产物生成,覆盖即可,最坏情况只是残留一个旧文件。
if (Test-Path $layout) {
    try { Remove-Item $layout -Recurse -Force -ErrorAction Stop }
    catch { Write-Warning "无法清理旧布局目录,改为就地覆盖: $($_.Exception.Message)" }
}
New-Item -ItemType Directory -Path (Join-Path $layout "Assets") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $layout "EarTrumpet") -Force | Out-Null
Copy-Item (Join-Path $release "*") (Join-Path $layout "EarTrumpet") -Recurse -Force

$manifestPath = Join-Path $packageProject "Package.appxmanifest"
$manifest = Get-Content $manifestPath -Raw -Encoding UTF8
# $version 已经在步骤 0.5 从编译产物同步过来了,这里不再从清单里读。
$publisher = [regex]::Match($manifest, 'Publisher="([^"]+)"').Groups[1].Value
Write-Host "  清单版本 $version / 发布者 $publisher"

# 打包时 VS 会替换的标记,手工补齐
$manifest = $manifest -replace 'Executable="\$targetnametoken\$\.exe"', 'Executable="EarTrumpet\EarTrumpet.exe"'
$manifest = $manifest -replace 'EntryPoint="\$targetentrypoint\$"', 'EntryPoint="Windows.FullTrustApplication"'
Set-Content (Join-Path $layout "AppxManifest.xml") -Value $manifest -Encoding UTF8

# 图标:清单里以属性(Logo="")或元素(<Logo>)形式出现的都要,否则 makeappx 校验会失败
$assetDir = Join-Path $packageProject "Assets"
$refs = [regex]::Matches($manifest, 'Assets\\[A-Za-z0-9_.\-]+\.png') |
    ForEach-Object { $_.Value } | Sort-Object -Unique
foreach ($ref in $refs) {
    $name = Split-Path $ref -Leaf
    $source = Join-Path $assetDir $name
    if (-not (Test-Path $source)) {
        $source = Join-Path $assetDir ($name -replace '\.png$', '.scale-100.png')
    }
    if (Test-Path $source) {
        Copy-Item $source (Join-Path $layout "Assets\$name") -Force
    }
    else {
        Write-Warning "图标 $name 在仓库里找不到(清单引用了它)"
    }
}
Write-Host "  图标 $($refs.Count) 个"

Write-Host "== 3) makeappx 打包 ==" -ForegroundColor Cyan
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
$msix = Join-Path $outDir "EarTrumpet-$version-x86.msix"
if (Test-Path $msix) { try { Remove-Item $msix -Force -ErrorAction Stop } catch { Write-Warning "旧包删不掉,makeappx /o 会覆盖它" } }
& $makeappx pack /d $layout /p $msix /o | Out-Null
if (-not (Test-Path $msix)) { throw "makeappx 打包失败" }
Write-Host ("  {0}  ({1:N2} MB)" -f (Split-Path $msix -Leaf), ((Get-Item $msix).Length / 1MB))

if (-not $SkipSign) {
    Write-Host "== 4) 签名 ==" -ForegroundColor Cyan
    if (-not $signtool) { throw "找不到 signtool.exe" }

    $cert = $null
    if ($CertThumbprint) {
        $cert = Get-ChildItem "Cert:\CurrentUser\My\$CertThumbprint" -ErrorAction SilentlyContinue
    }
    else {
        $cert = Get-ChildItem "Cert:\CurrentUser\My" |
            Where-Object { $_.Subject -eq $publisher } | Select-Object -First 1
    }
    if (-not $cert) {
        Write-Host "  未找到匹配发布者的证书,新建一张自签证书(仅本机信任,不能对外代表任何身份)"
        $cert = New-SelfSignedCertificate -Type Custom -Subject $publisher `
            -KeyUsage DigitalSignature -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 `
            -KeyExportPolicy Exportable -FriendlyName "EarTrumpet Local Build Signing" `
            -CertStoreLocation "Cert:\CurrentUser\My" `
            -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3") `
            -NotAfter (Get-Date).AddYears(3)
    }
    $cerPath = Join-Path $outDir "eartrumpet-local.cer"
    Export-Certificate -Cert $cert -FilePath $cerPath -Force | Out-Null
    Write-Host "  证书指纹 $($cert.Thumbprint),公钥导出至 $(Split-Path $cerPath -Leaf)"

    & $signtool sign /fd SHA256 /s My /sha1 $cert.Thumbprint $msix | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "signtool 签名失败" }
    Write-Host "  已签名"
}

Write-Host "== 5) 便携版 zip ==" -ForegroundColor Cyan
# 暂存在临时目录里,且不再需要事后清理;顺手保留一个干净的顶层目录名给 zip 用
$zipRoot = Join-Path $env:TEMP "EarTrumpet-$version-portable"
if (Test-Path $zipRoot) {
    try { Remove-Item $zipRoot -Recurse -Force -ErrorAction Stop }
    catch { $zipRoot = "$zipRoot-$(Get-Date -Format 'HHmmss')" }
}
New-Item -ItemType Directory -Path $zipRoot -Force | Out-Null
Copy-Item (Join-Path $release "*") $zipRoot -Recurse -Force
$readme = @"
EarTrumpet 便携版(免安装)
=========================

双击 EarTrumpet.exe 即可运行,不需要安装。
程序会出现在任务栏右下角的通知区域;Windows 11 默认把新图标收进折叠区,
点任务栏的 ^ 箭头就能找到它。

常用操作:
  * 左键单击托盘图标:弹出音量面板
  * 中键单击:默认设备静音 / 取消静音
  * 在托盘图标上滚动滚轮:调节默认设备音量
  * 右键单击:切换默认设备、设置、退出

这一份是自行编译的版本,相比官方 2.3.0.0 多两个改动:
  * 可选的开机自启动(右键菜单和设置里都有关闭开关)
  * 记住蓝牙耳机的音量,并在该设备重新连接时恢复
    —— 打开「设置 → 设备」可以看到所有已记录音量的设备(含当前没连接的),
       以及各自的记忆音量、当前音量、连接状态、蓝牙类型和上次见到的时间。

设置存放在哪:
  * 这个包里带了 portable.txt —— 设置会保存在**程序目录下的 settings.json**,
    把整个文件夹拷到别的机器或 U 盘,设置一起带走。
  * 删掉 portable.txt 和 settings.json,就回到常规方式(设置存在注册表
    HKCU\Software\EarTrumpet)。第一次回到常规方式时会尝试把注册表里的旧设置搬过来;
    反过来,第一次启用便携模式时也会把注册表里的现有设置搬进 settings.json。

卸载 = 删掉整个文件夹
(但记得先在右键菜单里关掉"开机自启动",否则会留下一条无效启动项)。
"@
Set-Content (Join-Path $zipRoot "使用说明.txt") -Value $readme -Encoding UTF8

# 便携模式的开关:存在这个文件就用程序目录下的 settings.json 存设置
$marker = @"
这个文件让 EarTrumpet 把设置存在同目录的 settings.json 里,而不是注册表。
作用:整个文件夹可以拷到别的机器 / U 盘,设置一起带走。

不要这个行为?删掉本文件(和 settings.json)即可,设置会回到注册表。
"@
Set-Content (Join-Path $zipRoot "portable.txt") -Value $marker -Encoding UTF8

$zip = Join-Path $outDir "EarTrumpet-$version-portable-x86.zip"
if (Test-Path $zip) { try { Remove-Item $zip -Force -ErrorAction Stop } catch { Write-Warning "旧 zip 删不掉,压缩时覆盖它" } }
Compress-Archive -Path $zipRoot -DestinationPath $zip -CompressionLevel Optimal -Force
Write-Host ("  {0}  ({1:N2} MB)" -f (Split-Path $zip -Leaf), ((Get-Item $zip).Length / 1MB))

Write-Host "== 6) 传统向导式安装包(NSIS) ==" -ForegroundColor Cyan
$makensis = @(
    (Join-Path $env:USERPROFILE ".workbuddy\tools\nsis\nsis-3.10\makensis.exe"),
    (Join-Path ${env:ProgramFiles(x86)} "NSIS\makensis.exe"),
    (Join-Path $env:ProgramFiles "NSIS\makensis.exe")
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1

if (-not $makensis) {
    Write-Warning "没找到 NSIS 编译器,跳过 setup.exe。免安装获取方式(约 2 MB,解压即用):"
    Write-Host "  Invoke-WebRequest 'https://downloads.sourceforge.net/project/nsis/NSIS%203/3.10/nsis-3.10.zip' -OutFile `"$env:TEMP\nsis.zip`""
    Write-Host "  Expand-Archive `"$env:TEMP\nsis.zip`" `"$env:USERPROFILE\.workbuddy\tools\nsis`" -Force"
    Write-Host "  然后重新运行本脚本即可。"
}
else {
    $setup = Join-Path $outDir "EarTrumpet-$version-setup.exe"
    $icon = Join-Path $repo "EarTrumpet\Assets\Icon-Light.ico"
    & $makensis "/DPAYLOAD=$release" "/DAPPVERSION=$version" "/DOUTFILE=$setup" "/DAPPICON=$icon" (Join-Path $repo "installer\setup.nsi") | Out-String -Stream | Where-Object { $_ -match "Total size|warning|error" } | ForEach-Object { Write-Host "  $_" }
    if (Test-Path $setup) {
        Write-Host ("  {0}  ({1:N2} MB)" -f (Split-Path $setup -Leaf), ((Get-Item $setup).Length / 1MB))
    }
    else {
        Write-Warning "makensis 编译失败"
    }
}

Write-Host ""
Write-Host "== 完成,产物在 $outDir ==" -ForegroundColor Green
Get-ChildItem $outDir -File | Where-Object { $_.Extension -in ".msix", ".zip", ".cer", ".exe" } |
    ForEach-Object { Write-Host ("  {0,-42} {1,8:N2} MB" -f $_.Name, ($_.Length / 1MB)) }
Write-Host ""
Write-Host "三种包怎么选:" -ForegroundColor Yellow
Write-Host @"
  *.setup.exe  —— 传统向导式安装(推荐):双击 -> 下一步 -> 选择目录 -> 完成
                  装在 %LOCALAPPDATA%\Programs\EarTrumpet,不需要管理员;
                  会在开始菜单和「设置 → 应用」里登记,带卸载器。
  *.msix       —— 系统原生安装界面,能直接更新已装的商店版(需先信任证书)
  *-portable-* —— 免安装,解压即用
"@
Write-Host ""
Write-Host "安装 MSIX 之前(只需做一次):" -ForegroundColor Yellow
Write-Host "  1. 先从托盘右键退出正在运行的 EarTrumpet(更新时程序占着文件会失败)"
Write-Host "  2. 信任随包导出的证书(不需要管理员):"
Write-Host "     Import-Certificate -FilePath `"$outDir\eartrumpet-local.cer`" -CertStoreLocation Cert:\CurrentUser\TrustedPeople"
Write-Host "  3. 双击 .msix —— 会弹出 Windows 的原生安装界面"
Write-Host "  回滚:Get-AppxPackage *EarTrumpet* | Remove-AppxPackage 之后重新安装官方版"
