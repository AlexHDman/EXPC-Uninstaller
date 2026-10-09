#include <cwchar>
#include <windows.h>
#include <shobjidl.h>
#include <shlobj.h>
#include <atomic>
class Sink final : public IFileOperationProgressSink {
    std::atomic<ULONG> refs{1};
public:
    HRESULT result = E_FAIL;
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID id, void** p) override { if (id==IID_IUnknown || id==IID_IFileOperationProgressSink) {*p=this;AddRef();return S_OK;} *p=nullptr;return E_NOINTERFACE; }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++refs; }
    ULONG STDMETHODCALLTYPE Release() override { auto n=--refs;if(!n)delete this;return n; }
    HRESULT STDMETHODCALLTYPE StartOperations() override {return S_OK;}
    HRESULT STDMETHODCALLTYPE FinishOperations(HRESULT) override {return S_OK;}
    HRESULT STDMETHODCALLTYPE PreRenameItem(DWORD,IShellItem*,LPCWSTR) override {return E_ABORT;}
    HRESULT STDMETHODCALLTYPE PostRenameItem(DWORD,IShellItem*,LPCWSTR,HRESULT,IShellItem*) override {return S_OK;}
    HRESULT STDMETHODCALLTYPE PreMoveItem(DWORD,IShellItem*,IShellItem*,LPCWSTR) override {return E_ABORT;}
    HRESULT STDMETHODCALLTYPE PostMoveItem(DWORD,IShellItem*,IShellItem*,LPCWSTR,HRESULT,IShellItem*) override {return S_OK;}
    HRESULT STDMETHODCALLTYPE PreCopyItem(DWORD,IShellItem*,IShellItem*,LPCWSTR) override {return E_ABORT;}
    HRESULT STDMETHODCALLTYPE PostCopyItem(DWORD,IShellItem*,IShellItem*,LPCWSTR,HRESULT,IShellItem*) override {return S_OK;}
    HRESULT STDMETHODCALLTYPE PreDeleteItem(DWORD flags,IShellItem* item) override {
        if (!(flags & TSF_DELETE_RECYCLE_IF_POSSIBLE)) return E_ABORT;
        PWSTR path=nullptr; auto hr=item->GetDisplayName(SIGDN_FILESYSPATH,&path);if(FAILED(hr))return hr;
        auto attributes=GetFileAttributesW(path); if(attributes==INVALID_FILE_ATTRIBUTES || attributes&FILE_ATTRIBUTE_REPARSE_POINT) hr=E_ABORT;
        if(SUCCEEDED(hr) && attributes&FILE_ATTRIBUTE_DIRECTORY) { wchar_t query[32768];swprintf_s(query,L"%s\\*",path); WIN32_FIND_DATAW d;HANDLE h=FindFirstFileW(query,&d);if(h!=INVALID_HANDLE_VALUE){do{if(wcscmp(d.cFileName,L".") && wcscmp(d.cFileName,L"..")){hr=E_ABORT;break;}}while(FindNextFileW(h,&d));FindClose(h);} }
        CoTaskMemFree(path);return hr;
    }
    HRESULT STDMETHODCALLTYPE PostDeleteItem(DWORD,IShellItem*,HRESULT hr,IShellItem*) override {result=hr;return S_OK;}
    HRESULT STDMETHODCALLTYPE PreNewItem(DWORD,IShellItem*,LPCWSTR) override {return E_ABORT;}
    HRESULT STDMETHODCALLTYPE PostNewItem(DWORD,IShellItem*,LPCWSTR,LPCWSTR,DWORD,HRESULT,IShellItem*) override {return S_OK;}
    HRESULT STDMETHODCALLTYPE UpdateProgress(UINT,UINT) override {return S_OK;}
    HRESULT STDMETHODCALLTYPE ResetTimer() override {return S_OK;}
    HRESULT STDMETHODCALLTYPE PauseTimer() override {return S_OK;}
    HRESULT STDMETHODCALLTYPE ResumeTimer() override {return S_OK;}
};
extern "C" __declspec(dllexport) int __cdecl RecycleOnly(const wchar_t* path) {
    HRESULT initialized=CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED);if(FAILED(initialized))return initialized;
    IFileOperation* op=nullptr; IShellItem* item=nullptr; auto sink=new Sink();
    HRESULT hr=CoCreateInstance(CLSID_FileOperation,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&op));
    if(SUCCEEDED(hr))hr=op->SetOperationFlags(FOFX_RECYCLEONDELETE|FOFX_EARLYFAILURE|FOF_NOERRORUI|FOF_SILENT|FOF_NOCONFIRMATION|FOF_NO_CONNECTED_ELEMENTS|FOF_NORECURSION);
    if(SUCCEEDED(hr))hr=SHCreateItemFromParsingName(path,nullptr,IID_PPV_ARGS(&item));
    if(SUCCEEDED(hr))hr=op->DeleteItem(item,sink);
    if(SUCCEEDED(hr))hr=op->PerformOperations();
    BOOL aborted=TRUE;if(SUCCEEDED(hr))hr=op->GetAnyOperationsAborted(&aborted);
    if(SUCCEEDED(hr))hr=aborted?E_ABORT:sink->result;
    if(SUCCEEDED(hr)) { auto attr=GetFileAttributesW(path);auto error=GetLastError();hr=(attr==INVALID_FILE_ATTRIBUTES && (error==ERROR_FILE_NOT_FOUND || error==ERROR_PATH_NOT_FOUND))?S_OK:E_FAIL; }
    if(item)item->Release();if(op)op->Release();sink->Release();CoUninitialize();return hr;
}
