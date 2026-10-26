// Console-only harness. Read the distributed PE as data; never execute its entry point.
#include <windows.h>
#include <iostream>
static HMODULE resourceModule;
#define FindResourceW(module, name, type) ::FindResourceW(resourceModule, name, type)
#define SizeofResource(module, resource) ::SizeofResource(resourceModule, resource)
#define LoadResource(module, resource) ::LoadResource(resourceModule, resource)
#define wWinMain UnusedGuiEntry
#include "../../src/Expc.PortableLauncher/launcher.cpp"
#undef wWinMain
int wmain(int argc, wchar_t** argv) {
    try {
        if (argc != 3) return 2;
        resourceModule = LoadLibraryExW(argv[1], nullptr, LOAD_LIBRARY_AS_DATAFILE_EXCLUSIVE | LOAD_LIBRARY_AS_IMAGE_RESOURCE);
        Check(resourceModule != nullptr, "Cannot read PE as data");
        Extract(fs::path(argv[2]));
        FreeLibrary(resourceModule);
        return 0;
    } catch(const std::exception& e) { std::cerr << e.what() << std::endl; return 1; }
}
