#include "common.h"
#include <compressapi.h>
#include <shlobj.h>
#include <shellapi.h>
#include "payload.h"
#include <unordered_map>
static bool Russian() {return PRIMARYLANGID(GetUserDefaultUILanguage())==LANG_RUSSIAN;}
static const wchar_t* T(const wchar_t* ru,const wchar_t* en){return Russian()?ru:en;}
static fs::path Known(REFKNOWNFOLDERID id){PWSTR p=nullptr;Check(SUCCEEDED(SHGetKnownFolderPath(id,0,nullptr,&p)),"Profile path unavailable");fs::path r=p;CoTaskMemFree(p);return r;}
static fs::path RuntimeRoot(){for(auto view:{KEY_WOW64_64KEY,KEY_WOW64_32KEY}){wchar_t b[32768];DWORD n=sizeof b;if(RegGetValueW(HKEY_LOCAL_MACHINE,L"SOFTWARE\\dotnet\\Setup\\InstalledVersions\\x64",L"InstallLocation",RRF_RT_REG_SZ|(view==KEY_WOW64_64KEY?RRF_SUBKEY_WOW6464KEY:RRF_SUBKEY_WOW6432KEY),nullptr,b,&n)==ERROR_SUCCESS&&Runtime(b,MIN_PATCH))return b;}auto p=Known(FOLDERID_ProgramFiles)/L"dotnet";return Runtime(p,MIN_PATCH)?p:fs::path();}
static DWORD Run(const fs::path& exe,const std::wstring& args,DWORD timeout,bool wait){std::wstring command=L"\""+exe.wstring()+L"\" "+args;STARTUPINFOW si{sizeof si};PROCESS_INFORMATION pi{};Check(CreateProcessW(exe.c_str(),command.data(),nullptr,nullptr,FALSE,CREATE_NO_WINDOW,nullptr,exe.parent_path().c_str(),&si,&pi)!=0,"Cannot start child process");CloseHandle(pi.hThread);DWORD code=0;if(wait){auto state=WaitForSingleObject(pi.hProcess,timeout);if(state!=WAIT_OBJECT_0){CloseHandle(pi.hProcess);throw std::runtime_error("Timed out; installer was not terminated");}GetExitCodeProcess(pi.hProcess,&code);}CloseHandle(pi.hProcess);return code;}
#include "dependency-window.h"
static fs::path Dependency(){auto root=RuntimeRoot();if(!root.empty())return root;return DependencyWindow{}.Show();}
struct Cursor{const unsigned char* p;size_t left;const unsigned char* Take(size_t n){Check(n<=left,"Truncated payload");auto r=p;p+=n;left-=n;return r;}template<class T>T Number(){T n;memcpy(&n,Take(sizeof n),sizeof n);return n;}};
static fs::path Extract(const fs::path& testRoot = {}){auto resource=FindResourceW(nullptr,MAKEINTRESOURCEW(101),RT_RCDATA);Check(resource!=nullptr,"Missing payload");auto n=SizeofResource(nullptr,resource);auto bytes=(unsigned char*)LockResource(LoadResource(nullptr,resource));Check(Hex(Hash(bytes,n))==PAYLOAD_SHA,"Payload integrity failure");Cursor packed{bytes,n};auto length=packed.Number<uint64_t>();Check(length<1500ULL*1024*1024,"Invalid payload size");Bytes raw((size_t)length);DECOMPRESSOR_HANDLE handle;Check(CreateDecompressor(COMPRESS_ALGORITHM_LZMS,nullptr,&handle),"Decompressor unavailable");SIZE_T written;bool ok=Decompress(handle,packed.p,packed.left,raw.data(),raw.size(),&written)!=0;CloseDecompressor(handle);Check(ok&&written==raw.size(),"Payload decompression failed");
    auto root=(testRoot.empty()?Known(FOLDERID_LocalAppData)/L"EXPC/PortableCache":testRoot)/Wide(PAYLOAD_SHA);NoLinks(root);fs::create_directories(root);NoLinks(root);
    auto mutexName=L"Local\\EXPCExtract-"+Wide(PAYLOAD_SHA);HANDLE mutex=CreateMutexW(nullptr,FALSE,mutexName.c_str());Check(mutex!=nullptr,"Extraction lock failed");auto wait=WaitForSingleObject(mutex,120000);if(wait!=WAIT_OBJECT_0&&wait!=WAIT_ABANDONED){CloseHandle(mutex);throw std::runtime_error("Extraction lock timeout");}
    try{std::unordered_map<std::string,std::pair<const unsigned char*,size_t>> contents; Cursor c{raw.data(),raw.size()};auto count=c.Number<uint32_t>();Check(count>0&&count<10000,"Invalid file count");for(uint32_t i=0;i<count;++i){auto size=c.Number<uint32_t>();Check(size<32768,"Invalid path");auto name=std::string((const char*)c.Take(size),size);Check(RelativeName(name),"Unsafe payload path");auto fileLength=c.Number<uint64_t>();auto hash=c.Take(32);auto unique=c.Number<unsigned char>(); auto key=Hex(Bytes(hash,hash+32)); const unsigned char* content; if(unique){content=c.Take((size_t)fileLength);contents.emplace(key,std::make_pair(content,(size_t)fileLength));}else{auto found=contents.find(key);Check(found!=contents.end()&&found->second.second==fileLength,"Invalid content reference");content=found->second.first;}Check(memcmp(Hash(content,(size_t)fileLength).data(),hash,32)==0,"File integrity failure");auto file=root/Wide(name);NoLinks(file);fs::create_directories(file.parent_path());NoLinks(file.parent_path());if(fs::exists(file)){auto current=Read(file);Check(memcmp(Hash(current.data(),current.size()).data(),hash,32)==0,"Extracted file was modified; use a clean cache directory");continue;}
      // CREATE_NEW never overwrites another file. Held lock serializes parallel extractions.
      HANDLE output=CreateFileW(file.c_str(),GENERIC_WRITE,0,nullptr,CREATE_NEW,FILE_ATTRIBUTE_NORMAL,nullptr);Check(output!=INVALID_HANDLE_VALUE,"Cannot create payload file");DWORD done=0;auto saved=WriteFile(output,content,(DWORD)fileLength,&done,nullptr);FlushFileBuffers(output);CloseHandle(output);Check(saved&&done==fileLength,"Cannot write payload file");}
      Check(c.left==0,"Trailing payload bytes");ReleaseMutex(mutex);CloseHandle(mutex);return root;
    }catch(...){ReleaseMutex(mutex);CloseHandle(mutex);throw;}
}
int WINAPI wWinMain(HINSTANCE,HINSTANCE,LPWSTR,int){try{SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);wchar_t name[32768];Check(GetModuleFileNameW(nullptr,name,32768)>0,"Executable path unavailable");fs::path origin=name;
    // Test mode verifies/extracts the actual embedded payload, with no GUI, runtime install or app launch.
    int argc=0;auto args=CommandLineToArgvW(GetCommandLineW(),&argc);bool verify=(argc==2||argc==3)&&wcscmp(args[1],L"--verify-payload")==0;fs::path verifyRoot=verify&&argc==3?args[2]:L"";LocalFree(args);
    if(verify){Extract(verifyRoot);return 0;}
    auto probe=origin.parent_path()/(L".expc-write-"+std::to_wstring(GetCurrentProcessId()));HANDLE check=CreateFileW(probe.c_str(),GENERIC_WRITE,0,nullptr,CREATE_NEW,FILE_ATTRIBUTE_TEMPORARY|FILE_FLAG_DELETE_ON_CLOSE,nullptr);Check(check!=INVALID_HANDLE_VALUE,"Portable folder is read-only; no settings or secrets were moved");CloseHandle(check);
    fs::path runtime;
#ifdef FRAMEWORK_DEPENDENT
    runtime=Dependency();if(runtime.empty())return 2;
#endif
    auto root=Extract();SetEnvironmentVariableW(L"EXPC_PORTABLE_ORIGIN",origin.c_str());
#ifdef FRAMEWORK_DEPENDENT
    SetEnvironmentVariableW(L"DOTNET_ROOT_X64",runtime.c_str());SetEnvironmentVariableW(L"DOTNET_ROOT",runtime.c_str());SetEnvironmentVariableW(L"DOTNET_ROLL_FORWARD",L"LatestPatch");Run(runtime/L"dotnet.exe",L"\""+(root/L"EXPC-Uninstaller.dll").wstring()+L"\"",0,false);
#else
    Run(root/L"EXPC-Uninstaller.exe",L"",0,false);
#endif
    return 0;
}catch(const std::exception& e){int argc=0;auto a=CommandLineToArgvW(GetCommandLineW(),&argc);bool verify=(argc==2||argc==3)&&wcscmp(a[1],L"--verify-payload")==0;LocalFree(a);if(!verify)MessageBoxW(nullptr,Wide(e.what()).c_str(),L"EXPC Uninstaller",MB_OK|MB_ICONERROR);return 1;}}
