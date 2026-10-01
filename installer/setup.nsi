; EarTrumpet 传统向导式安装脚本(NSIS 3.x,Unicode 中文界面)
;
; 用法(由 make-package.ps1 调用):
;   makensis.exe /DPAYLOAD=<Build\Release 的绝对路径> /DAPPVERSION=2.3.0.136 ^
;               /DOUTFILE=<输出的 setup.exe 路径> installer\setup.nsi
;
; 设计要点:
;   * RequestExecutionLevel user —— 装到 %LOCALAPPDATA%\Programs\EarTrumpet,
;     全程不需要管理员权限;
;   * 五步经典向导:欢迎 -> 组件 -> 选择目录 -> 安装中 -> 完成;
;   * 卸载器 + 在「设置 → 应用」里登记(含图标、版本、发布者、占用空间);
;   * 安装前用"独占写测试"检查程序是否正在运行,避免覆盖失败后留下半新半旧的文件;
;   * 卸载时顺带清掉应用可能写入的开机自启动项(Run 键里的 EarTrumpet)。

Unicode true

!include "MUI2.nsh"
!include "FileFunc.nsh"
!include "LogicLib.nsh"

; ---- 由命令行传入的参数(给默认值以便单独调试) ----
; 默认值一律基于 ${__FILEDIR__}(本脚本所在目录),这样在任何工作目录下都能直接
; 跑 `makensis installer\setup.nsi`,不依赖调用方的 cwd。
!ifndef PAYLOAD
  !define PAYLOAD "${__FILEDIR__}\..\Build\Release"
!endif
!ifndef APPVERSION
  !define APPVERSION "2.3.0.136"
!endif
!ifndef OUTFILE
  !define OUTFILE "${__FILEDIR__}\..\Build\Package\EarTrumpet-setup.exe"
!endif
!ifndef APPICON
  !define APPICON "${__FILEDIR__}\..\EarTrumpet\Assets\Icon-Light.ico"
!endif
; 向导配图(由 installer\make-art.py 用仓库自带的官方 logo 生成,产物已入库)
!ifndef ARTDIR
  !define ARTDIR "${__FILEDIR__}\art"
!endif

!define APPNAME "EarTrumpet"
!define APPPUBLISHER "自行编译版本(基于官方 2.3.0.0)"
!define UNINSTKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\EarTrumpet"

Name "${APPNAME}"
OutFile "${OUTFILE}"
InstallDir "$LOCALAPPDATA\Programs\EarTrumpet"
InstallDirRegKey HKCU "Software\EarTrumpet" "InstallDir"
RequestExecutionLevel user
SetCompressor /SOLID lzma
SetCompressorDictSize 32

VIProductVersion "${APPVERSION}"
VIAddVersionKey /LANG=2052 "ProductName" "${APPNAME}"
VIAddVersionKey /LANG=2052 "FileDescription" "${APPNAME} 安装程序"
VIAddVersionKey /LANG=2052 "FileVersion" "${APPVERSION}"
VIAddVersionKey /LANG=2052 "ProductVersion" "${APPVERSION}"
VIAddVersionKey /LANG=2052 "CompanyName" "${APPPUBLISHER}"
VIAddVersionKey /LANG=2052 "LegalCopyright" "MIT License"

; ---- 占用检测宏(安装段和卸载段共用;单独成文件是为了让测试探针能 !include 同一份源码) ----
!include "${__FILEDIR__}\check-file-in-use.nsh"

; ---- 界面 ----
!define MUI_ABORTWARNING
!define MUI_ICON "${APPICON}"
!define MUI_UNICON "${APPICON}"
; 品牌化配图 —— 替换掉 NSIS 自带那张(带 NSIS 自家 logo,会让人以为是别人的安装包)
; 图片尺寸必须是 MUI 规定的 150x57 与 164x314,否则会被 FitControl 拉伸变形
!define MUI_HEADERIMAGE
!define MUI_HEADERIMAGE_BITMAP   "${ARTDIR}\header.bmp"
!define MUI_UNHEADERIMAGE_BITMAP "${ARTDIR}\header.bmp"
!define MUI_WELCOMEPAGE_BITMAP   "${ARTDIR}\welcome.bmp"
!define MUI_FINISHPAGE_BITMAP    "${ARTDIR}\welcome.bmp"

!define MUI_WELCOMEPAGE_TITLE "欢迎安装 ${APPNAME}"
!define MUI_WELCOMEPAGE_TEXT "EarTrumpet 是一个 Windows 音量合成器:把每个播放设备的音量、以及每个应用的音量放到托盘上随时可调。$\r$\n$\r$\n这一份是自行编译的版本,相比官方 2.3.0.0 多了两处改动:$\r$\n    * 可选的开机自启动(右键菜单和设置里都能开关)$\r$\n    * 记住蓝牙耳机的音量,并在该设备重新连接时自动恢复$\r$\n$\r$\n程序会装在你的用户目录下,全程不需要管理员权限。$\r$\n$\r$\n提示:如果你之前装过商店(MSIX)版本的 EarTrumpet,建议先到「设置 → 应用」里卸载它,否则会出现两个托盘图标。"

!define MUI_DIRECTORYPAGE_TEXT_TOP "请选择安装位置。默认装在当前用户目录下,不需要管理员权限。"

!define MUI_FINISHPAGE_TITLE "${APPNAME} 安装完成"
!define MUI_FINISHPAGE_TEXT "EarTrumpet 已经安装好了。$\r$\n$\r$\n启动后它不会显示主窗口,而是把图标放到任务栏右下角的通知区域。Windows 11 默认会把新图标收进折叠区,点任务栏的 ^ 箭头就能找到它。$\r$\n$\r$\n常用操作:左键单击弹出音量面板,中键单击静音,在图标上滚动滚轮调节音量,右键是菜单。"
!define MUI_FINISHPAGE_RUN "$INSTDIR\EarTrumpet.exe"
!define MUI_FINISHPAGE_RUN_TEXT "立即运行 ${APPNAME}"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "SimpChinese"

; ============================================================ 安装
Section "EarTrumpet 主程序(必需)" SEC_MAIN
  SectionIn RO

  ; 程序正在运行时覆盖它的 exe 会失败,留下半新半旧的文件。
  ; 先检测,检测到就问用户要不要结束它(而不是直接把人挡回去);用户拒绝才放弃。
  !insertmacro CHECK_FILE_IN_USE "$INSTDIR\EarTrumpet.exe" "EarTrumpet.exe"
  !insertmacro CLOSE_APP_IF_IN_USE "$INSTDIR\EarTrumpet.exe" "EarTrumpet.exe" "EarTrumpet" INS
  ${If} $RES == "INUSE"
    MessageBox MB_ICONSTOP|MB_OK "EarTrumpet 仍在运行,无法替换它的文件。$\r$\n$\r$\n请先在托盘图标上右键 → 「退出」,然后重新运行本安装程序。"
    Abort
  ${EndIf}
  ; 不可写但不是被占用(多半是权限)。跟上面分开报 —— 结束进程解决不了这种,别让人白折腾。
  ${If} $RES == "LOCKED"
    MessageBox MB_ICONSTOP|MB_OK "无法写入 $INSTDIR\EarTrumpet.exe(没有写权限,或文件被设为只读)。$\r$\n$\r$\n请换一个安装目录,或以管理员身份运行本安装程序。"
    Abort
  ${EndIf}

  SetOutPath "$INSTDIR"
  File /r "${PAYLOAD}\*.*"

  ; 安装目录里放一份说明
  FileOpen $0 "$INSTDIR\安装说明.txt" "w"
  FileWrite $0 "EarTrumpet(自行编译版)$\r$\n"
  FileWrite $0 "==========================$\r$\n$\r$\n"
  FileWrite $0 "启动:双击 EarTrumpet.exe。它没有主窗口,只在任务栏右下角的通知区域出现。$\r$\n"
  FileWrite $0 "      Windows 11 默认把新图标收进折叠区,点任务栏的 ^ 箭头可找到。$\r$\n$\r$\n"
  FileWrite $0 "操作:左键 = 音量面板;中键 = 静音;在图标上滚轮 = 调音量;右键 = 菜单。$\r$\n$\r$\n"
  FileWrite $0 "相比官方 2.3.0.0 的两处改动:可选的开机自启动;记住蓝牙耳机音量并在重连时恢复。$\r$\n$\r$\n"
  FileWrite $0 "设置保存在注册表 HKCU\Software\EarTrumpet。$\r$\n"
  FileWrite $0 "卸载:开始菜单里的「卸载 EarTrumpet」,或 设置 → 应用。$\r$\n"
  FileClose $0

  WriteUninstaller "$INSTDIR\uninstall.exe"

  CreateDirectory "$SMPROGRAMS\${APPNAME}"
  CreateShortCut "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk" "$INSTDIR\EarTrumpet.exe" "" "$INSTDIR\EarTrumpet.exe" 0
  CreateShortCut "$SMPROGRAMS\${APPNAME}\卸载 ${APPNAME}.lnk" "$INSTDIR\uninstall.exe"

  ; 让「设置 → 应用」里能看到它,并能直接卸载
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayName" "${APPNAME}"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayVersion" "${APPVERSION}"
  WriteRegStr HKCU "${UNINSTKEY}" "Publisher" "${APPPUBLISHER}"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayIcon" "$INSTDIR\EarTrumpet.exe"
  WriteRegStr HKCU "${UNINSTKEY}" "UninstallString" '"$INSTDIR\uninstall.exe"'
  WriteRegStr HKCU "${UNINSTKEY}" "QuietUninstallString" '"$INSTDIR\uninstall.exe" /S'
  WriteRegStr HKCU "${UNINSTKEY}" "InstallLocation" "$INSTDIR"
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoRepair" 1

  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD HKCU "${UNINSTKEY}" "EstimatedSize" "$0"

  WriteRegStr HKCU "Software\EarTrumpet" "InstallDir" "$INSTDIR"
SectionEnd

Section "创建桌面快捷方式" SEC_DESKTOP
  CreateShortCut "$DESKTOP\${APPNAME}.lnk" "$INSTDIR\EarTrumpet.exe" "" "$INSTDIR\EarTrumpet.exe" 0
SectionEnd

; ============================================================ 卸载
Function un.onInit
  ; /SD IDNO —— 静默卸载(QuietUninstallString、无人值守、脚本批量卸载)时不能弹窗,
  ; NSIS 在没有 /SD 时会取**第一个**按钮当默认,也就是 IDYES,等于静默删掉用户设置。
  ; 默认必须是"保留",删数据这种事只能由用户显式点。
  MessageBox MB_YESNO|MB_ICONQUESTION "是否同时删除 EarTrumpet 的设置?$\r$\n$\r$\n包括:开机自启动开关、蓝牙耳机音量记忆、窗口位置等(都在 HKCU\Software\EarTrumpet)。$\r$\n选择「否」则保留,方便你以后重装时接着用。" /SD IDNO IDYES delete_settings IDNO keep_settings
  delete_settings:
    StrCpy $0 "1"
    Goto done
  keep_settings:
    StrCpy $0 "0"
  done:
FunctionEnd

Section "Uninstall"
  ; 占用检测:程序在跑的话 exe 删不掉,会留下装不干净的局面 —— 一样先问再结束它。
  ; (EarTrumpet 没有命令行退出参数,所以不能靠 ExecWait 让它自己退。)
  ; 不用 Rename 试探:Windows 允许重命名正在运行的 exe(映像以 FILE_SHARE_DELETE 打开),
  ; 那个判断会漏报。
  !insertmacro CHECK_FILE_IN_USE "$INSTDIR\EarTrumpet.exe" "EarTrumpet.exe"
  !insertmacro CLOSE_APP_IF_IN_USE "$INSTDIR\EarTrumpet.exe" "EarTrumpet.exe" "EarTrumpet" UN
  ${If} $RES == "INUSE"
    MessageBox MB_ICONSTOP|MB_OK "EarTrumpet 仍在运行,无法删除它的文件。$\r$\n$\r$\n请先在托盘图标上右键 → 「退出」,然后重新运行卸载程序。"
    Abort
  ${EndIf}
  ${If} $RES == "LOCKED"
    MessageBox MB_ICONSTOP|MB_OK "无法删除 $INSTDIR\EarTrumpet.exe(没有写权限,或文件被设为只读)。$\r$\n$\r$\n请以管理员身份运行卸载程序,或手动删除该目录。"
    Abort
  ${EndIf}

  Delete "$SMPROGRAMS\${APPNAME}\${APPNAME}.lnk"
  Delete "$SMPROGRAMS\${APPNAME}\卸载 ${APPNAME}.lnk"
  RMDir "$SMPROGRAMS\${APPNAME}"
  Delete "$DESKTOP\${APPNAME}.lnk"

  ; 应用自己写的开机自启动项(Value 名就是 EarTrumpet)
  DeleteRegValue HKCU "Software\Microsoft\Windows\CurrentVersion\Run" "EarTrumpet"

  RMDir /r "$INSTDIR"

  DeleteRegKey HKCU "${UNINSTKEY}"
  DeleteRegValue HKCU "Software\EarTrumpet" "InstallDir"

  ${If} $0 == "1"
    DeleteRegKey HKCU "Software\EarTrumpet"
  ${EndIf}
SectionEnd

; ---- 组件说明(显示在组件页下方) ----
!insertmacro MUI_FUNCTION_DESCRIPTION_BEGIN
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_MAIN} "EarTrumpet 主程序与 32 种语言的界面资源(必需)。"
  !insertmacro MUI_DESCRIPTION_TEXT ${SEC_DESKTOP} "在桌面上放一个 EarTrumpet 的快捷方式。"
!insertmacro MUI_FUNCTION_DESCRIPTION_END
