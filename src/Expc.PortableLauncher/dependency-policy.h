#pragma once
enum class InstallChoice { None, Microsoft, WinGet };
enum class InstallPhase { Choice, Waiting, Ready, Failed, Unobserved };
struct DependencyPolicy {
    InstallChoice choice = InstallChoice::None;
    InstallPhase phase = InstallPhase::Choice;
    bool consumed = false;
    void Select(InstallChoice next) { if(phase!=InstallPhase::Waiting && phase!=InstallPhase::Ready && phase!=InstallPhase::Unobserved) choice=choice==next?InstallChoice::None:next; }
    bool Begin() { if(phase==InstallPhase::Waiting||phase==InstallPhase::Ready||phase==InstallPhase::Unobserved||choice!=InstallChoice::WinGet)return false;phase=InstallPhase::Waiting;return true; }
    void Complete(bool compatible) { phase=compatible?InstallPhase::Ready:InstallPhase::Failed; }
    bool Launch() { if(phase!=InstallPhase::Ready||consumed)return false;consumed=true;return true; }
};
