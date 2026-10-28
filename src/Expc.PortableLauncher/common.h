#pragma once
#include <windows.h>
#include <bcrypt.h>
#include <filesystem>
#include <fstream>
#include <string>
#include <vector>
#include <stdexcept>
#include <regex>
namespace fs=std::filesystem;
using Bytes=std::vector<unsigned char>;
inline void Check(bool ok,const char* message) {if(!ok)throw std::runtime_error(message);}
inline Bytes Hash(const void* data,size_t size) {Bytes hash(32);Check(size<=ULONG_MAX,"Payload too large");Check(BCryptHash(BCRYPT_SHA256_ALG_HANDLE,nullptr,0,(PUCHAR)data,(ULONG)size,hash.data(),32)==0,"SHA256 failed");return hash;}
inline std::string Hex(const Bytes& bytes) {static const char chars[]="0123456789abcdef";std::string s;for(auto b:bytes){s+=chars[b>>4];s+=chars[b&15];}return s;}
inline Bytes Read(const fs::path& path) {std::ifstream in(path,std::ios::binary);Check(in.good(),"Cannot read file");return Bytes(std::istreambuf_iterator<char>(in),{});}
inline std::wstring Wide(const std::string& text) {int n=MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,text.data(),(int)text.size(),nullptr,0);Check(n>0,"Invalid UTF8");std::wstring s(n,0);MultiByteToWideChar(CP_UTF8,MB_ERR_INVALID_CHARS,text.data(),(int)text.size(),s.data(),n);return s;}
inline void NoLinks(fs::path path) {for(;!path.empty();path=path.parent_path()){auto attrs=GetFileAttributesW(path.c_str());Check(attrs==INVALID_FILE_ATTRIBUTES || !(attrs&FILE_ATTRIBUTE_REPARSE_POINT),"Reparse path blocked");if(path==path.parent_path())break;}}
inline bool StableRuntime(const std::wstring& version,int minimum) {std::wsmatch m;if(!std::regex_match(version,m,std::wregex(L"10\\.0\\.([0-9]{1,6})")))return false;return std::stoi(m[1])>=minimum;}
inline bool X64(const fs::path& file) {auto b=Read(file);if(b.size()<64||b[0]!='M'||b[1]!='Z')return false;unsigned int off;memcpy(&off,b.data()+60,4);return off<b.size()-6&&memcmp(b.data()+off,"PE\0\0",4)==0&&b[off+4]==0x64&&b[off+5]==0x86;}
inline bool Framework(const fs::path& root,const wchar_t* name,int patch,const wchar_t* dll) {auto shared=root/L"shared"/name;std::error_code ec;if(!fs::is_directory(shared,ec))return false;for(auto& d:fs::directory_iterator(shared)){if(d.is_directory()&&StableRuntime(d.path().filename().wstring(),patch)&&fs::exists(d.path()/dll))return true;}return false;}
inline bool Runtime(const fs::path& root,int patch) {try{return fs::exists(root/L"dotnet.exe")&&X64(root/L"dotnet.exe")&&Framework(root,L"Microsoft.NETCore.App",patch,L"coreclr.dll")&&Framework(root,L"Microsoft.WindowsDesktop.App",patch,L"System.Windows.Forms.dll");}catch(...){return false;}}
inline bool RelativeName(const std::string& name) {if(name.empty()||name.front()=='/'||name.find('\\')!=std::string::npos||name.find(':')!=std::string::npos||name.find('\0')!=std::string::npos)return false;for(auto& p:fs::path(name)){auto s=p.string();if(s==".."||s=="."||s.empty()||s.back()=='.'||s.back()==' ')return false;}return true;}

inline fs::path CacheRoot(const fs::path& localAppData) {return localAppData/L"EXPC/Uninstaller/Cache";}
