; ============================================================ 占用检测与结束进程(安装/卸载共用)
;
; 用法:
;   !insertmacro CHECK_FILE_IN_USE "<完整路径>" "<进程名>"
;   !insertmacro CLOSE_APP_IF_IN_USE "<完整路径>" "<进程名>" "<显示名>" <标签前缀>
;
; $RES 取值:
;   FREE    —— 可以被替换
;   INUSE   —— 被占用,通常是那个程序正在运行(结束它就能解决)
;   LOCKED  —— 不可写但没人占着(多半是权限或只读属性;结束进程没用)
;   MISSING —— 文件不存在(首次安装/已卸载)
;
; 注意:CHECK_FILE_IN_USE 会用到 $1~$4,CLOSE_APP_IF_IN_USE 会用到 $0。
;
; ------------------------------------------------------------ 检测做法
; 以 **GENERIC_WRITE + dwShareMode=0 + OPEN_EXISTING** 打开一次。
; 要判断"能不能覆盖它",就得真的按写入去要权限。
;
; 历史坑(2026-10-01 实测,血案):
;   之前用的是 GENERIC_READ + share=0,以为"独占读"能撞上占用。实际上
;   **对正在运行的 exe 也能打开成功** —— 映像的读权限本来就是共享的。
;   后果是恒返回 FREE,占用检测从写出来那天起就没生效过,安装程序会直接
;   覆盖正在运行的程序,留下半新半旧的文件。写方式才会触发共享冲突。
;
; 为什么不用 GetLastError 区分 INUSE 与 LOCKED:
;   System::Call 经由内部消息实现,CreateFileW 与 GetLastError 两次调用之间
;   最后错误已经被冲掉(实测:Python 直接测得 err=32,NSIS 里取回来却是 0)。
;   所以用"该进程是否在跑"来区分 —— 这也正是调用方真正关心的:
;   结束进程到底能不能解决问题。
;
; 也不要用:
;   * FileOpen "a" 之类 —— 跑在 SetOutPath 之前时会因目录不存在而失败,被误判成
;     "正在运行"(2026-09-30 踩过:全新机器上装不上);
;   * Rename 试探 —— Windows 允许重命名正在运行的 exe(映像以 FILE_SHARE_DELETE
;     打开),那个判断会漏报。

Var /GLOBAL RES

!macro CHECK_FILE_IN_USE PATH PROCEXE
  StrCpy $RES "MISSING"
  ${If} ${FileExists} "${PATH}"
    ; GENERIC_WRITE=0x40000000, dwShareMode=0, OPEN_EXISTING=3
    System::Call 'kernel32::CreateFileW(w "${PATH}", i 0x40000000, i 0, i 0, i 3, i 0, i 0) i .r1'
    ${If} $1 == -1
      ; 打不开:被占用,还是没权限?看进程在不在跑。
      nsExec::ExecToStack '"$SYSDIR\tasklist.exe" /FI "IMAGENAME eq ${PROCEXE}" /NH /FO CSV'
      Pop $2
      Pop $3
      ; 有匹配进程时 CSV 第一行以引号开头;没有匹配时是一行**本地化**的提示语。
      ; 判首字符而不是搜字符串,免得换个系统语言就失效。
      StrCpy $4 $3 1
      ${If} $4 == '"'
        StrCpy $RES "INUSE"
      ${Else}
        StrCpy $RES "LOCKED"
      ${EndIf}
    ${Else}
      StrCpy $RES "FREE"
      System::Call 'kernel32::CloseHandle(i r1)'
    ${EndIf}
  ${EndIf}
!macroend

; ------------------------------------------------------------ 结束占用进程
; 必须在 CHECK_FILE_IN_USE 之后调用,并且只在 $RES == "INUSE" 时才有意义。
; 结束后会把 $RES 重新检测:非 INUSE 才能继续。
;
; 标签前缀是必需的:NSIS 的标签是**全文件作用域**,而这段逻辑要在安装段和卸载段
; 各展开一次,不区分就会撞名编译失败。宏参数是纯文本替换,所以
; IDYES ${PREFIX}_yes 会展开成唯一标签。
;
; 先礼后兵:taskkill 不带 /F 会向进程的顶层窗口发 WM_CLOSE,让程序有机会正常退出
; (EarTrumpet 借此把设置写完);只有它没退时才 /F 强杀 —— 强杀会丢掉音量记忆那条
; 2 秒防抖队列里最后的内容,值得先试一次温和的。
;
; 测试探针定义 ET_SILENT_CLOSE 即可跳过询问,直接走"结束"分支,
; 这样自动化测试覆盖到的就是真正会跑的那段逻辑。
!macro CLOSE_APP_IF_IN_USE PATH PROCEXE DISPLAYNAME PREFIX
  ${If} $RES == "INUSE"
!ifndef ET_SILENT_CLOSE
    MessageBox MB_YESNO|MB_ICONQUESTION "${DISPLAYNAME} 正在运行,安装/卸载需要替换或删除它的文件。$\r$\n$\r$\n是否现在结束 ${DISPLAYNAME} 并继续?" /SD IDYES IDYES ${PREFIX}_yes IDNO ${PREFIX}_no
!endif

    ${PREFIX}_yes:
      nsExec::ExecToLog '"$SYSDIR\taskkill.exe" /IM "${PROCEXE}" /T'
      Pop $0
      Sleep 1500
      !insertmacro CHECK_FILE_IN_USE "${PATH}" "${PROCEXE}"

      ${If} $RES == "INUSE"
        ; 没退(托盘程序往往没有可见窗口可收 WM_CLOSE)—— 强杀。
        nsExec::ExecToLog '"$SYSDIR\taskkill.exe" /F /IM "${PROCEXE}" /T'
        Pop $0
        Sleep 1500
        !insertmacro CHECK_FILE_IN_USE "${PATH}" "${PROCEXE}"
      ${EndIf}
      Goto ${PREFIX}_done

    ${PREFIX}_no:
      ; 保持 INUSE,让调用方去 Abort 并给出提示。
      StrCpy $RES "INUSE"

    ${PREFIX}_done:
  ${EndIf}
!macroend
