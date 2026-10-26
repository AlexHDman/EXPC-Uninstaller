#include "../../src/Expc.PortableLauncher/common.h"
#include "../../src/Expc.PortableLauncher/instance.h"
int wmain(int argc,wchar_t** argv){
 if(argc==2 && wcscmp(argv[1],L"ack")==0)return 0;
 wchar_t event[256];
 if(GetEnvironmentVariableW(L"EXPC_UPDATE_READY",event,256)){
   HANDLE ready=OpenEventW(EVENT_MODIFY_STATE,FALSE,event);if(!ready)return 4;SetEvent(ready);CloseHandle(ready);return 0;
 }
 if(argc<3)return 3;
 SessionInstance instance(argv[1]);if(!instance.primary)return 10;
 std::ofstream ready(fs::path(argv[2]).wstring()+L".ready");ready<<GetCurrentProcessId();ready.close();
 for(int i=0;i<600&&!fs::exists(fs::path(argv[2]).wstring()+L".stop");++i)Sleep(20);
 return 0;
}
