# EarTrumpet(自行编译版)

> 这是基于官方 [File-New-Project/EarTrumpet](https://github.com/File-New-Project/EarTrumpet) **2.3.0.0**
> 自行编译的分支:保留官方全部功能,并额外加了下面几项。
>
> *A self-compiled fork of upstream EarTrumpet 2.3.0.0 with a few extra features. Upstream's
> README follows at the bottom; all credit for EarTrumpet itself goes to its authors.*

## 这个分支和官方版的区别

| 功能 | 说明 |
| --- | --- |
| **开机自启动开关** | 托盘右键菜单和设置里都能开关 |
| **记住蓝牙设备音量** | 设备重连时自动恢复。「设置 → 设备」列出所有已记录的设备(含当前未连接的),显示记忆音量、当前音量、连接状态、蓝牙类型、上次见到的时间,并可逐条「忘记此设备」 |
| **重连时的音量浮层** | 恢复音量时显示与系统音量键外观一致的提示浮层。样式/位置/时机是按原生浮层截图逐项量出来后复刻的(原生浮层归 ShellExperienceHost 私有,第三方无法唤出)。可用 `EarTrumpet.exe --volume-osd-preview 50` 预览,加 `--light` / `--dark` 指定配色 |
| **安装目录更整洁** | 32 个语言文件夹收进 `Language\` 子目录(运行时由自定义 ResourceManager 接管加载) |
| **便携模式** | 程序目录里有 `portable.txt` 时,设置写在同目录的 `settings.json`,整个文件夹可以拷到别的机器;删掉该文件即回到注册表,注册表里的原设置不会被删 |
| **不上报任何数据** | 官方版会经 Bugsnag 把崩溃报告发送到上游账号。本分支把上报客户端、配置节、依赖包与相关开关**整条链路移除**,只保留本机诊断(「Troubleshoot」按钮读的是内存里的日志),不会向任何地方发送数据 |
| **安装器更省事** | 覆盖安装或卸载时如果 EarTrumpet 正在运行,会询问是否结束它并继续,而不是直接拒绝 |

## 下载 / 安装

到 **[Releases](https://github.com/YHuanheg/EarTrumpet/releases)** 下载,三种形态任选其一:

- `EarTrumpet-<版本>-setup.exe` —— 向导式安装,装到 `%LOCALAPPDATA%\Programs\EarTrumpet`,全程不需要管理员权限。**推荐**
- `EarTrumpet-<版本>-portable-x86.zip` —— 解压即用,包内已带 `portable.txt`(设置随文件夹走)
- `EarTrumpet-<版本>-x86.msix` —— 应用包安装,**需要先把 `eartrumpet-local.cer` 装进「受信任的根证书颁发机构」**,否则 Windows 会拒绝安装

> 如果之前装过商店(MSIX)版或官方版,建议先卸载再装本分支,否则会出现两个托盘图标。

## 自己构建

```bat
build-release.bat                     :: 编译(x86 Release)
powershell -File make-package.ps1     :: 打包出 setup.exe / 便携版 zip / MSIX
```

需要 Visual Studio Build Tools(MSBuild)、.NET Framework 4.6.2 Developer Pack、
Windows SDK(提供 makeappx / signtool)。完整说明见 [BUILD-CSHARP.md](./BUILD-CSHARP.md)。

版本号由 GitVersion 从 git 历史推出(末位是"距上个 tag 的提交数"),打包脚本会自动把它同步进 MSIX 清单 ——
**所以每交付一版都要提交**,否则版本号不会变,而 MSIX 升级要求版本号递增。

## 许可与致谢

MIT,与上游一致。全部功劳属于 [EarTrumpet 项目](https://github.com/File-New-Project/EarTrumpet)
及其贡献者,本分支只做了上表那些改动。

上游的隐私政策描述的是**上游**的遥测行为,不适用于本分支 —— 这里不上报任何数据。

上游项目本身的完整说明(功能、奖项、各语言说明等)请见 [File-New-Project/EarTrumpet](https://github.com/File-New-Project/EarTrumpet)。
