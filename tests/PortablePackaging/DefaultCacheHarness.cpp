// Headless extraction test: fake only the LocalAppData known folder, not Extract's path logic.
#include <windows.h>
#include <shlobj.h>
#include <string>
#include <iostream>
static HMODULE resourceModule;
static std::wstring fixtureLocal;
static HRESULT FixtureKnownFolder(REFKNOWNFOLDERID id,DWORD flags,HANDLE token,PWSTR* output){
 if(!IsEqualGUID(id,FOLDERID_LocalAppData))return SHGetKnownFolderPath(id,flags,token,output);
 *output=(PWSTR)CoTaskMemAlloc((fixtureLocal.size()+1)*sizeof(wchar_t));
 if(!*output)return E_OUTOFMEMORY;
 memcpy(*output,fixtureLocal.c_str(),(fixtureLocal.size()+1)*sizeof(wchar_t));return S_OK;
}
#define SHGetKnownFolderPath FixtureKnownFolder
#define FindResourceW(module, name, type) ::FindResourceW(resourceModule, name, type)
#define SizeofResource(module, resource) ::SizeofResource(resourceModule, resource)
#define LoadResource(module, resource) ::LoadResource(resourceModule, resource)
#define wWinMain UnusedGuiEntry
#include "../../src/Expc.PortableLauncher/launcher.cpp"
#undef wWinMain
int wmain(int argc,wchar_t** argv){try{
 if(argc!=3)return 2;fixtureLocal=argv[2];
 resourceModule=LoadLibraryExW(argv[1],nullptr,LOAD_LIBRARY_AS_DATAFILE_EXCLUSIVE|LOAD_LIBRARY_AS_IMAGE_RESOURCE);
 Check(resourceModule!=nullptr,"Read candidate as data failed");
 auto result=Extract(); // Production default, not the explicit testRoot overload.
 Check(result==fs::path(fixtureLocal)/L"EXPC/Uninstaller/Cache"/Wide(PAYLOAD_SHA),"Incorrect extraction destination");
 Check(fs::exists(result/L"EXPC-Uninstaller.dll"),"No extracted application");
 Check(Extract()==result,"Warm cache destination changed");
 FreeLibrary(resourceModule);std::cout<<"default cache + cold/warm extraction PASS; no GUI or candidate execution\n";return 0;
}catch(const std::exception& e){std::cerr<<e.what()<<"\n";return 1;}}
