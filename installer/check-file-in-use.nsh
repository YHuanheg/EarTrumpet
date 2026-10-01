; ============================================================ 占用检测(安装/卸载共用)
;
; 用法:
;   !insertmacro CHECK_FILE_IN_USE "<完整路径>"
;   ${If} $RES == "INUSE" ... ${EndIf}
; 输出:
;   $RES ∈ { INUSE, FREE, MISSING }
;
; 做法:CreateFileW + dwShareMode=0 + OPEN_EXISTING 做一次真正的"独占打开"。
;   * OPEN_EXISTING —— 既不创建也不截断文件,纯探测;
;   * 正在运行的 exe 被 loader 映射着(share 含 FILE_SHARE_READ|FILE_SHARE_DELETE),
;     我们要的 share=0 与它冲突,必然拿到 INVALID_HANDLE_VALUE(-1);
;   * 文件不存在时同样返回 -1,所以**必须先判存在**,否则首次安装会误报"正在运行"。
;
; 历史坑(2026-09-30):早期版本写的是 FileOpen "$INSTDIR\EarTrumpet.exe" "a",
; 它跑在 SetOutPath 之前 —— 目录都还没建,FileOpen 必然失败,被当成"正在运行",
; 结果全新机器上装不上。两个错误叠在一起:检测时机不对 + 失败语义被当成占用。
;
; 也不要用 Rename 试探:Windows 允许重命名正在运行的 exe
; (映像以 FILE_SHARE_DELETE 打开),那个判断会漏报。

Var /GLOBAL RES

!macro CHECK_FILE_IN_USE PATH
  StrCpy $RES "MISSING"
  ${If} ${FileExists} "${PATH}"
    StrCpy $RES "FREE"
    ; GENERIC_READ=0x80000000, dwShareMode=0, OPEN_EXISTING=3
    System::Call 'kernel32::CreateFileW(w "${PATH}", i 0x80000000, i 0, i 0, i 3, i 0, i 0) i .r1'
    ${If} $1 == -1
      StrCpy $RES "INUSE"
    ${Else}
      System::Call 'kernel32::CloseHandle(i r1)'
    ${EndIf}
  ${EndIf}
!macroend


; ============================================================ 结束占用进程(安装/卸载共用)
;
; 用法(必须在 CHECK_FILE_IN_USE 之后调用,$RES 是它的结果):
;   !insertmacro CLOSE_APP_IF_IN_USE "<路径>" "<进程名>" "<显示名>" <标签前缀>
;   ${If} $RES == "INUSE"   ; 用户拒绝,或结束失败
;     Abort
;   ${EndIf}
;
; 结束成功会把 $RES 重新检测成 FREE/MISSING,调用方据此判断能否继续。
;
; 标签前缀是必需的:NSIS 的标签是**全文件作用域**,而这段逻辑要在安装段和卸载段各展开一次,
; 不区分就会撞名编译失败。宏参数是纯文本替换,所以 IDYES ${PREFIX}_yes 会展开成唯一标签。
;
; 先礼后兵:taskkill 不带 /F 会向进程的顶层窗口发 WM_CLOSE,让程序有机会正常退出
; (EarTrumpet 借此把设置写完);只有它没退时才 /F 强杀。强杀会丢掉音量记忆那条 2 秒防抖
; 队列里最后的内容,所以值得先试一次温和的。
!macro CLOSE_APP_IF_IN_USE PATH PROCEXE DISPLAYNAME PREFIX
  ${If} $RES == "INUSE"
    MessageBox MB_YESNO|MB_ICONQUESTION "${DISPLAYNAME} 正在运行,安装/卸载需要替换或删除它的文件。$\r$\n$\r$\n是否现在结束 ${DISPLAYNAME} 并继续?" /SD IDYES IDYES ${PREFIX}_yes IDNO ${PREFIX}_no

    ${PREFIX}_yes:
      nsExec::ExecToLog '"$SYSDIR\taskkill.exe" /IM "${PROCEXE}" /T'
      Pop $0
      Sleep 1500
      !insertmacro CHECK_FILE_IN_USE "${PATH}"

      ${If} $RES == "INUSE"
        ; 没退(托盘程序没有可见窗口可收 WM_CLOSE 是常见情况)—— 强杀。
        nsExec::ExecToLog '"$SYSDIR\taskkill.exe" /F /IM "${PROCEXE}" /T'
        Pop $0
        Sleep 1500
        !insertmacro CHECK_FILE_IN_USE "${PATH}"
      ${EndIf}
      Goto ${PREFIX}_done

    ${PREFIX}_no:
      ; 保持 INUSE,让调用方去 Abort 并给出提示。
      StrCpy $RES "INUSE"

    ${PREFIX}_done:
  ${EndIf}
!macroend
