#include "common.h"
#include <iostream>
#include "dependency-policy.h"
int wmain(){try{
 Check(StableRuntime(L"10.0.12",12),"Minimum patch rejected");Check(StableRuntime(L"10.0.99",12),"Later patch rejected");
 for(auto value:{L"10.0.11",L"10.0.12-preview.1",L"11.0.0",L"9.0.99",L"10.1.0",L"10.0.12-beta"})Check(!StableRuntime(value,12),"Wrong runtime accepted");
 for(auto value:{"../outside","/outside","C:/outside","folder\\outside","folder/../outside","name.","name "})Check(!RelativeName(value),"Unsafe name accepted");
 Check(RelativeName("package-probe/Expc.PackageProbe.exe"),"Helper path rejected");
 Check(Hex(Hash("abc",3))=="ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad","SHA mismatch");
 wchar_t exe[32768];GetModuleFileNameW(nullptr,exe,32768);Check(X64(exe),"Test binary is not x64");
 DependencyPolicy p;Check(p.choice==InstallChoice::None && !p.Launch(),"No choice must not launch");
 p.Select(InstallChoice::Microsoft);p.Select(InstallChoice::WinGet);Check(p.choice==InstallChoice::WinGet,"Choices must be exclusive");
 p.Select(InstallChoice::WinGet);Check(p.choice==InstallChoice::None && !p.Begin(),"Uncheck must clear choice");
 p.Select(InstallChoice::WinGet);Check(p.Begin() && !p.Begin(),"Duplicate installation");p.Select(InstallChoice::Microsoft);Check(p.choice==InstallChoice::WinGet,"Choice changed while running");
 p.Complete(false);Check(!p.Launch(),"Failed verification launched");p.Select(InstallChoice::Microsoft);Check(!p.Begin(),"Microsoft starts installer");
 p.Complete(true);Check(p.Launch()&&!p.Launch(),"Ready must launch exactly once");
 DependencyPolicy timeout;timeout.phase=InstallPhase::Unobserved;timeout.choice=InstallChoice::WinGet;Check(!timeout.Begin()&&!timeout.Launch(),"Unobserved allows retry or launch");
 std::cout<<"native policy: assertions passed; no GUI, runtime installation or app launch\n";return 0;
 }catch(const std::exception& e){std::cerr<<e.what()<<"\n";return 1;}}
