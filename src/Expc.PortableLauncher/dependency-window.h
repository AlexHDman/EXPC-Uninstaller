#pragma once
#include <commctrl.h>
#include "dependency-policy.h"

// Native controls and a message-loop timer: no .NET and no blocking Wait on the UI thread.
struct DependencyWindow {
    HWND window{}, microsoft{}, winget{}, ok{}, cancel{}, verify{}, progress{}, status{};
    HFONT normal{}, bold{}; HANDLE process{}; ULONGLONG started{};
    DependencyPolicy policy; fs::path runtime; bool finished=false, launch=false; int dpi=96;
    static constexpr UINT Timer=1;
    int S(int value) const {return MulDiv(value,dpi,96);}
    const wchar_t* Text(const wchar_t* ru,const wchar_t* en) {return T(ru,en);}
    void Status(const std::wstring& text){SetWindowTextW(status,text.c_str());}
    void State(){
        bool working=policy.phase==InstallPhase::Waiting, ready=policy.phase==InstallPhase::Ready;
        for(auto control:{microsoft,winget})EnableWindow(control,!working&&!ready&&policy.phase!=InstallPhase::Unobserved);
        SendMessageW(microsoft,BM_SETCHECK,policy.choice==InstallChoice::Microsoft?BST_CHECKED:BST_UNCHECKED,0);
        SendMessageW(winget,BM_SETCHECK,policy.choice==InstallChoice::WinGet?BST_CHECKED:BST_UNCHECKED,0);
        EnableWindow(ok,!working&&policy.phase!=InstallPhase::Unobserved);EnableWindow(verify,!working);
        SetWindowTextW(cancel,working?Text(L"Прекратить ожидание",L"Stop waiting"):Text(L"Отмена",L"Cancel"));
        SendMessageW(progress,PBM_SETMARQUEE,working,40);ShowWindow(progress,working?SW_SHOW:SW_HIDE);InvalidateRect(window,nullptr,TRUE);
    }
    void Fonts(){if(normal)DeleteObject(normal);if(bold)DeleteObject(bold);normal=CreateFontW(-MulDiv(11,dpi,72),0,0,0,FW_NORMAL,FALSE,FALSE,FALSE,DEFAULT_CHARSET,0,0,CLEARTYPE_QUALITY,0,L"Segoe UI");bold=CreateFontW(-MulDiv(11,dpi,72),0,0,0,FW_BOLD,FALSE,FALSE,FALSE,DEFAULT_CHARSET,0,0,CLEARTYPE_QUALITY,0,L"Segoe UI");for(auto control:{microsoft,winget,ok,cancel,verify,status})SendMessageW(control,WM_SETFONT,(WPARAM)normal,TRUE);}
    void Layout(){
        RECT work{};SystemParametersInfoW(SPI_GETWORKAREA,0,&work,0);RECT r{0,0,S(600),S(325)};AdjustWindowRectExForDpi(&r,WS_CAPTION|WS_SYSMENU|WS_MINIMIZEBOX,FALSE,0,dpi);
        SetWindowPos(window,nullptr,0,0,r.right-r.left,r.bottom-r.top,SWP_NOMOVE|SWP_NOZORDER);
        MoveWindow(microsoft,S(548),S(108),S(24),S(26),TRUE);MoveWindow(winget,S(548),S(150),S(24),S(26),TRUE);
        MoveWindow(status,S(24),S(200),S(550),S(55),TRUE);MoveWindow(progress,S(24),S(183),S(550),S(10),TRUE);
        MoveWindow(verify,S(24),S(275),S(190),S(30),TRUE);MoveWindow(ok,S(278),S(275),S(90),S(30),TRUE);MoveWindow(cancel,S(378),S(275),S(198),S(30),TRUE);
    }
    void Paint(){PAINTSTRUCT ps;auto dc=BeginPaint(window,&ps);SetBkMode(dc,TRANSPARENT);
        auto line=[&](int x,int y,const wchar_t* text,bool accent){SelectObject(dc,accent?bold:normal);SetTextColor(dc,accent?RGB(51,156,255):GetSysColor(COLOR_WINDOWTEXT));TextOutW(dc,S(x),S(y),text,(int)wcslen(text));};
        line(24,20,Text(L"Для запуска EXPC Uninstaller требуется",L"EXPC Uninstaller requires"),false);
        line(24,44,L".NET Desktop Runtime 10 x64",true);
        line(24,72,Text(L"Выберите способ установки.",L"Choose an installation method."),false);
        line(24,110,Text(L"Открыть страницу загрузки Microsoft",L"Open the Microsoft download page"),true);
        const wchar_t* prefix=Text(L"Установить через ",L"Install using ");line(24,152,prefix,false);SIZE size;GetTextExtentPoint32W(dc,prefix,(int)wcslen(prefix),&size);
        SelectObject(dc,bold);SetTextColor(dc,RGB(51,156,255));TextOutW(dc,S(24)+size.cx,S(152),L"WinGet",6);
        if(policy.phase==InstallPhase::Ready){HPEN pen=CreatePen(PS_SOLID,S(3),RGB(38,160,75));auto old=SelectObject(dc,pen);MoveToEx(dc,S(245),S(284),nullptr);LineTo(dc,S(251),S(291));LineTo(dc,S(265),S(276));SelectObject(dc,old);DeleteObject(pen);}
        EndPaint(window,&ps);
    }
    void CheckInstalled(bool afterInstall=false,DWORD code=0){
        if(process && WaitForSingleObject(process,0)==WAIT_TIMEOUT){Status(Text(L"Установщик ещё работает. Дождитесь завершения.",L"Installer is still running. Wait for completion."));return;}
        if(process){CloseHandle(process);process=nullptr;}
        runtime=RuntimeRoot();policy.Complete(!runtime.empty());
        if(!runtime.empty())Status(code==3010||code==1641?Text(L"Готово. Требуется перезагрузка Windows; автоматически она не выполняется. OK — запуск EXPC.",L"Ready. Windows restart requested; no automatic restart. OK launches EXPC."):Text(L"Готово. Нажмите OK для запуска EXPC.",L"Ready. Press OK to launch EXPC."));
        else if(code==3010||code==1641)Status(Text(L"Требуется перезагрузка. Runtime пока не подтверждён. Перезагрузите Windows самостоятельно, затем проверьте установку.",L"Restart required. Runtime is not confirmed. Restart Windows yourself, then check installation."));
        else if(afterInstall)Status(std::wstring(Text(L"Установка не подтверждена. Код WinGet: ",L"Installation not verified. WinGet code: "))+std::to_wstring(code)+Text(L". Можно выбрать Microsoft.",L". You can choose Microsoft."));
        else Status(Text(L"Совместимый runtime не обнаружен. Завершите установку и проверьте снова.",L"Compatible runtime was not found. Finish installation and check again."));
        State();
    }
    void StartInstall(){
        if(!policy.Begin())return;State();Status(Text(L"WinGet: загрузка / установка. Может потребоваться UAC. Ожидание до 15 минут…",L"WinGet: downloading / installing. UAC may be required. Waiting up to 15 minutes…"));
        auto exe=Known(FOLDERID_LocalAppData)/L"Microsoft/WindowsApps/winget.exe";
        if(!fs::exists(exe)){policy.Complete(false);Status(Text(L"WinGet не найден. Выберите страницу Microsoft.",L"WinGet not found. Choose the Microsoft page."));State();return;}
        std::wstring cmd=L"\""+exe.wstring()+L"\" install --id Microsoft.DotNet.DesktopRuntime.10 --exact --source winget --architecture x64 --accept-source-agreements --accept-package-agreements --disable-interactivity";
        STARTUPINFOW si{sizeof si};PROCESS_INFORMATION pi{};
        if(!CreateProcessW(exe.c_str(),cmd.data(),nullptr,nullptr,FALSE,CREATE_NO_WINDOW,nullptr,nullptr,&si,&pi)){auto code=GetLastError();policy.Complete(false);Status(std::wstring(Text(L"Не удалось запустить WinGet. Ошибка Windows: ",L"Cannot start WinGet. Windows error: "))+std::to_wstring(code));State();return;}
        CloseHandle(pi.hThread);process=pi.hProcess;started=GetTickCount64();SetTimer(window,Timer,300,nullptr);
    }
    void Command(int id){
        if(id==IDCANCEL){launch=false;DestroyWindow(window);return;}
        if(id==10||id==11){policy.Select(id==10?InstallChoice::Microsoft:InstallChoice::WinGet);State();return;}
        if(id==12){CheckInstalled();return;}
        if(id!=IDOK||policy.phase==InstallPhase::Waiting||policy.phase==InstallPhase::Unobserved)return;
        if(policy.Launch()){launch=true;DestroyWindow(window);return;}
        if(policy.choice==InstallChoice::None){MessageBoxW(window,Text(L"EXPC Uninstaller не может быть запущен: необходимый .NET Desktop Runtime 10 x64 не установлен",L"EXPC Uninstaller cannot start: the required .NET Desktop Runtime 10 x64 is not installed"),L"EXPC Uninstaller",MB_OK|MB_ICONWARNING);DestroyWindow(window);return;}
        if(policy.choice==InstallChoice::WinGet){StartInstall();return;}
        auto url=std::wstring(L"https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-")+RUNTIME_VERSION+L"-windows-x64-installer";
        auto result=(INT_PTR)ShellExecuteW(window,L"open",url.c_str(),nullptr,nullptr,SW_SHOWNORMAL);
        Status(result>32?Text(L"Установите Desktop Runtime со страницы Microsoft, затем нажмите «Проверить установку».",L"Install Desktop Runtime from Microsoft, then click Check installation."):Text(L"Не удалось открыть браузер. Выберите WinGet или откройте страницу Microsoft вручную.",L"Could not open browser. Choose WinGet or open Microsoft manually."));
    }
    static LRESULT CALLBACK Proc(HWND h,UINT msg,WPARAM w,LPARAM l){auto self=(DependencyWindow*)GetWindowLongPtrW(h,GWLP_USERDATA);if(msg==WM_NCCREATE){self=(DependencyWindow*)((CREATESTRUCTW*)l)->lpCreateParams;self->window=h;SetWindowLongPtrW(h,GWLP_USERDATA,(LONG_PTR)self);}if(!self)return DefWindowProcW(h,msg,w,l);
        switch(msg){case WM_PAINT:self->Paint();return 0;case WM_COMMAND:self->Command(LOWORD(w));return 0;
        case WM_TIMER:if(self->process){auto state=WaitForSingleObject(self->process,0);if(state==WAIT_OBJECT_0){DWORD code;GetExitCodeProcess(self->process,&code);CloseHandle(self->process);self->process=nullptr;KillTimer(h,Timer);self->CheckInstalled(true,code);}else if(GetTickCount64()-self->started>15*60*1000){KillTimer(h,Timer);self->policy.phase=InstallPhase::Unobserved;self->Status(T(L"Время ожидания истекло. Установщик не остановлен. Не запускайте повторную установку; проверьте её завершение.",L"Wait timed out. Installer was not stopped. Do not reinstall; check its completion."));self->State();}}return 0;
        case WM_DPICHANGED:self->dpi=HIWORD(w);self->Fonts();self->Layout();{auto r=(RECT*)l;SetWindowPos(h,nullptr,r->left,r->top,0,0,SWP_NOSIZE|SWP_NOZORDER);}return 0;
        case WM_CLOSE:self->Command(IDCANCEL);return 0;case WM_DESTROY:KillTimer(h,Timer);self->finished=true;return 0;
        }return DefWindowProcW(h,msg,w,l);
    }
    fs::path Show(){
        INITCOMMONCONTROLSEX controls{sizeof controls,ICC_PROGRESS_CLASS};InitCommonControlsEx(&controls);
        WNDCLASSW cls{};cls.lpfnWndProc=Proc;cls.hInstance=GetModuleHandleW(nullptr);cls.lpszClassName=L"EXPCDependency";cls.hCursor=LoadCursorW(nullptr,IDC_ARROW);cls.hbrBackground=(HBRUSH)(COLOR_WINDOW+1);RegisterClassW(&cls);
        window=CreateWindowExW(0,cls.lpszClassName,L"EXPC Uninstaller",WS_CAPTION|WS_SYSMENU|WS_MINIMIZEBOX,CW_USEDEFAULT,CW_USEDEFAULT,620,370,nullptr,nullptr,cls.hInstance,this);Check(window!=nullptr,"Cannot create runtime dialog");dpi=GetDpiForWindow(window);
        auto control=[&](const wchar_t* kind,const wchar_t* label,DWORD style,int id){return CreateWindowExW(0,kind,label,WS_CHILD|WS_VISIBLE|style,0,0,10,10,window,(HMENU)(INT_PTR)id,cls.hInstance,nullptr);};
        microsoft=control(L"BUTTON",L"",BS_CHECKBOX|WS_TABSTOP,10);winget=control(L"BUTTON",L"",BS_CHECKBOX|WS_TABSTOP,11);
        ok=control(L"BUTTON",L"OK",BS_DEFPUSHBUTTON|WS_TABSTOP,IDOK);cancel=control(L"BUTTON",Text(L"Отмена",L"Cancel"),BS_PUSHBUTTON|WS_TABSTOP,IDCANCEL);
        verify=control(L"BUTTON",Text(L"Проверить установку",L"Check installation"),BS_PUSHBUTTON|WS_TABSTOP,12);status=control(L"STATIC",L"",SS_LEFT,13);progress=control(PROGRESS_CLASSW,L"",PBS_MARQUEE,14);
        Fonts();Layout();State();RECT r{},work{};GetWindowRect(window,&r);SystemParametersInfoW(SPI_GETWORKAREA,0,&work,0);SetWindowPos(window,nullptr,work.left+(work.right-work.left-(r.right-r.left))/2,work.top+(work.bottom-work.top-(r.bottom-r.top))/2,0,0,SWP_NOSIZE|SWP_NOZORDER);ShowWindow(window,SW_SHOW);SetFocus(cancel);
        MSG msg;while(!finished&&GetMessageW(&msg,nullptr,0,0)>0){if(!IsDialogMessageW(window,&msg)){TranslateMessage(&msg);DispatchMessageW(&msg);}}
        if(process)CloseHandle(process);if(normal)DeleteObject(normal);if(bold)DeleteObject(bold);return launch?runtime:fs::path();
    }
};
