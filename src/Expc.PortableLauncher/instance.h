#pragma once
#include <sddl.h>
struct SessionInstance {
    HANDLE gate=nullptr, activation=nullptr;
    bool primary=false;
    SessionInstance(const std::wstring& testPrefix = {}) {
        HANDLE token=nullptr; Check(OpenProcessToken(GetCurrentProcess(),TOKEN_QUERY,&token)!=0,"Session identity unavailable");
        DWORD n=0; GetTokenInformation(token,TokenUser,nullptr,0,&n); Bytes b(n);
        bool ok=GetTokenInformation(token,TokenUser,b.data(),n,&n)!=0;CloseHandle(token);Check(ok,"Session identity unavailable");
        LPWSTR sid=nullptr;Check(ConvertSidToStringSidW(((TOKEN_USER*)b.data())->User.Sid,&sid)!=0,"Session SID unavailable");
        std::wstring identity=sid;LocalFree(sid);auto prefix=testPrefix.empty()?L"Local\\EXPCdelete-"+identity:testPrefix;
        PSECURITY_DESCRIPTOR sd=nullptr;
        Check(ConvertStringSecurityDescriptorToSecurityDescriptorW((L"D:P(A;;GA;;;"+identity+L")S:(ML;;NW;;;LW)").c_str(),SDDL_REVISION_1,&sd,nullptr)!=0,"Session security unavailable");
        SECURITY_ATTRIBUTES sa{sizeof sa,sd,FALSE};
        activation=CreateEventW(&sa,FALSE,FALSE,(prefix+L"-activate").c_str());
        gate=CreateMutexW(&sa,FALSE,(prefix+L"-launcher").c_str());LocalFree(sd);
        Check(gate&&activation,"Session lock unavailable");
        auto result=WaitForSingleObject(gate,0);primary=result==WAIT_OBJECT_0||result==WAIT_ABANDONED;
        Check(primary||result==WAIT_TIMEOUT,"Session lock failed");
        if(primary){ // A directly started managed app also prevents runtime/bootstrap duplication.
            auto app=OpenMutexW(SYNCHRONIZE|MUTEX_MODIFY_STATE,FALSE,(prefix+L"-app").c_str());
            if(app){auto state=WaitForSingleObject(app,0);if(state==WAIT_TIMEOUT){ReleaseMutex(gate);primary=false;}
                else if(state==WAIT_OBJECT_0||state==WAIT_ABANDONED)ReleaseMutex(app);CloseHandle(app);}
        }
        if(!primary){AllowSetForegroundWindow(ASFW_ANY);SetEvent(activation);}
    }
    ~SessionInstance(){if(gate){if(primary)ReleaseMutex(gate);CloseHandle(gate);}if(activation)CloseHandle(activation);}
};
