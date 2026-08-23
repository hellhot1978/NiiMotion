#define XR_NO_PROTOTYPES
#include "openxr/openxr.h"

extern "C" __declspec(dllimport) void* __stdcall LoadLibraryA(const char*);
extern "C" __declspec(dllimport) void* __stdcall GetProcAddress(void*, const char*);
extern "C" __declspec(dllimport) void __stdcall ExitProcess(unsigned int);
extern "C" int _fltused = 0;
extern "C" void* memset(void* destination, int value, size_t count) { auto* bytes = static_cast<volatile unsigned char*>(destination); while (count--) *bytes++ = static_cast<unsigned char>(value); return destination; }

bool Equal(const char* a, const char* b) { while (*a && *b && *a == *b) { ++a; ++b; } return *a == *b; }
XrApiLayerProperties g_properties[32]{};

extern "C" void __stdcall ProbeStart() {
    auto library = LoadLibraryA("C:\\Program Files (x86)\\Steam\\steamapps\\common\\SteamVR\\bin\\win64\\openxr_loader.dll");
    if (!library) ExitProcess(10);
    auto enumerate = reinterpret_cast<PFN_xrEnumerateApiLayerProperties>(GetProcAddress(library, "xrEnumerateApiLayerProperties"));
    if (!enumerate) ExitProcess(11);
    unsigned int count = 0; if (XR_FAILED(enumerate(0, &count, nullptr)) || count == 0 || count > 32) ExitProcess(12);
    for (unsigned int i = 0; i < count; ++i) g_properties[i].type = XR_TYPE_API_LAYER_PROPERTIES;
    if (XR_FAILED(enumerate(count, &count, g_properties))) ExitProcess(13);
    for (unsigned int i = 0; i < count; ++i) if (Equal(g_properties[i].layerName, "XR_APILAYER_NIIRMOTION_locomotion")) ExitProcess(0);
    ExitProcess(14);
}
