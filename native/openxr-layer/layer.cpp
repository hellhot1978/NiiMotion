#define XR_NO_PROTOTYPES
#include "openxr/openxr.h"
#include "openxr/openxr_loader_negotiation.h"

extern "C" __declspec(dllimport) void* __stdcall OpenFileMappingA(unsigned long, int, const char*);
extern "C" __declspec(dllimport) void* __stdcall MapViewOfFile(void*, unsigned long, unsigned long, unsigned long, size_t);
extern "C" __declspec(dllimport) unsigned long __stdcall GetModuleFileNameA(void*, char*, unsigned long);
extern "C" __declspec(dllimport) void __stdcall OutputDebugStringA(const char*);
extern "C" __declspec(dllimport) unsigned long long __stdcall GetTickCount64();
extern "C" int _fltused = 0;
extern "C" void* memset(void* destination, int value, size_t count) {
    auto* bytes = static_cast<volatile unsigned char*>(destination); while (count--) *bytes++ = static_cast<unsigned char>(value); return destination;
}
extern "C" void* memcpy(void* destination, const void* source, size_t count) {
    auto* out = static_cast<volatile unsigned char*>(destination); const auto* in = static_cast<const volatile unsigned char*>(source); while (count--) *out++ = *in++; return destination;
}

namespace {
constexpr unsigned int kMagic = 0x3158524E; // NXR1
constexpr char kMappingName[] = "Local\\NiiMotion.OpenXR.v1";
constexpr char kLayerName[] = "XR_APILAYER_NIIRMOTION_locomotion";

#pragma pack(push, 1)
struct SharedMotion {
    unsigned int magic;
    unsigned int version;
    unsigned long long sequence;
    float x;
    float y;
    unsigned int enabled;
    unsigned int processHash1;
    unsigned int processHash2;
    unsigned int reserved;
    unsigned long long heartbeatMs;
};
#pragma pack(pop)

struct ActionInfo { XrAction handle; char name[XR_MAX_ACTION_NAME_SIZE]; XrActionType type; int direction; };
ActionInfo g_actions[128]{};
unsigned int g_actionCount = 0;
PFN_xrGetInstanceProcAddr g_nextGipa = nullptr;
PFN_xrCreateAction g_nextCreateAction = nullptr;
PFN_xrDestroyAction g_nextDestroyAction = nullptr;
PFN_xrGetActionStateVector2f g_nextGetVector2 = nullptr;
PFN_xrGetActionStateFloat g_nextGetFloat = nullptr;
void* g_mapping = nullptr;
SharedMotion* g_shared = nullptr;

bool Equal(const char* a, const char* b) {
    if (!a || !b) return false;
    while (*a && *b && *a == *b) { ++a; ++b; }
    return *a == *b;
}
char Lower(char c) { return c >= 'A' && c <= 'Z' ? static_cast<char>(c + ('a' - 'A')) : c; }
bool ContainsInsensitive(const char* value, const char* needle) {
    if (!value || !needle || !*needle) return false;
    for (; *value; ++value) {
        const char* a = value; const char* b = needle;
        while (*a && *b && Lower(*a) == Lower(*b)) { ++a; ++b; }
        if (!*b) return true;
    }
    return false;
}
unsigned int ProcessHash() {
    char path[260]{}; GetModuleFileNameA(nullptr, path, 260);
    const char* base = path; for (const char* p = path; *p; ++p) if (*p == '\\' || *p == '/') base = p + 1;
    unsigned int hash = 2166136261u; for (const char* p = base; *p; ++p) { hash ^= static_cast<unsigned char>(Lower(*p)); hash *= 16777619u; }
    return hash;
}
bool ReadMotion(float& x, float& y) {
    if (!g_shared) {
        g_mapping = OpenFileMappingA(4, 0, kMappingName);
        if (g_mapping) g_shared = static_cast<SharedMotion*>(MapViewOfFile(g_mapping, 4, 0, 0, sizeof(SharedMotion)));
    }
    if (!g_shared || g_shared->magic != kMagic || g_shared->version != 1 || !g_shared->enabled) return false;
    const auto process = ProcessHash();
    if (g_shared->processHash1 && g_shared->processHash1 != process && g_shared->processHash2 != process) return false;
    const auto before = g_shared->sequence; if (before & 1u) return false;
    const auto heartbeat = g_shared->heartbeatMs; x = g_shared->x; y = g_shared->y;
    const auto after = g_shared->sequence;
    if (before != after || (after & 1u) || GetTickCount64() - heartbeat > 250) return false;
    if (x < -1) x = -1; if (x > 1) x = 1; if (y < -1) y = -1; if (y > 1) y = 1;
    return true;
}
ActionInfo* FindAction(XrAction action) {
    for (unsigned int i = 0; i < g_actionCount; ++i) if (g_actions[i].handle == action) return &g_actions[i];
    return nullptr;
}
}

extern "C" XRAPI_ATTR XrResult XRAPI_CALL Nii_xrCreateAction(XrActionSet set, const XrActionCreateInfo* info, XrAction* action) {
    const auto result = g_nextCreateAction(set, info, action);
    if (XR_SUCCEEDED(result) && info && action && g_actionCount < 128) {
        auto& entry = g_actions[g_actionCount++]; entry.handle = *action;
        unsigned int i = 0; for (; i + 1 < XR_MAX_ACTION_NAME_SIZE && info->actionName[i]; ++i) entry.name[i] = info->actionName[i]; entry.name[i] = 0;
        entry.type = info->actionType; const bool namedMovement = ContainsInsensitive(info->actionName, "move") || ContainsInsensitive(info->actionName, "locomotion") || ContainsInsensitive(info->actionName, "walk");
        entry.direction = namedMovement ? (ContainsInsensitive(info->actionName, "back") ? -1 : 1) : 0;
        if (entry.direction && (entry.type == XR_ACTION_TYPE_VECTOR2F_INPUT || entry.type == XR_ACTION_TYPE_FLOAT_INPUT)) OutputDebugStringA("NiiMotion OpenXR: movement action matched.\n");
    }
    return result;
}

extern "C" XRAPI_ATTR XrResult XRAPI_CALL Nii_xrDestroyAction(XrAction action) {
    for (unsigned int i = 0; i < g_actionCount; ++i) if (g_actions[i].handle == action) { g_actions[i] = g_actions[g_actionCount - 1]; --g_actionCount; break; }
    return g_nextDestroyAction(action);
}

extern "C" XRAPI_ATTR XrResult XRAPI_CALL Nii_xrGetActionStateVector2f(XrSession session, const XrActionStateGetInfo* info, XrActionStateVector2f* state) {
    const auto result = g_nextGetVector2(session, info, state);
    auto* action = info ? FindAction(info->action) : nullptr;
    if (XR_SUCCEEDED(result) && state && action && action->direction && action->type == XR_ACTION_TYPE_VECTOR2F_INPUT) {
        float x = 0, y = 0;
        if (ReadMotion(x, y)) { state->currentState.x = x; state->currentState.y = y; state->isActive = XR_TRUE; state->changedSinceLastSync = XR_TRUE; }
        else { state->currentState.x = 0; state->currentState.y = 0; }
    }
    return result;
}

extern "C" XRAPI_ATTR XrResult XRAPI_CALL Nii_xrGetActionStateFloat(XrSession session, const XrActionStateGetInfo* info, XrActionStateFloat* state) {
    const auto result = g_nextGetFloat(session, info, state); auto* action = info ? FindAction(info->action) : nullptr;
    if (XR_SUCCEEDED(result) && state && action && action->direction && action->type == XR_ACTION_TYPE_FLOAT_INPUT) {
        float x = 0, y = 0;
        if (ReadMotion(x, y)) { const auto value = action->direction < 0 ? (y < 0 ? -y : 0) : (y > 0 ? y : 0); state->currentState = value; state->isActive = XR_TRUE; state->changedSinceLastSync = XR_TRUE; }
        else state->currentState = 0;
    }
    return result;
}

extern "C" XRAPI_ATTR XrResult XRAPI_CALL Nii_xrGetInstanceProcAddr(XrInstance instance, const char* name, PFN_xrVoidFunction* function) {
    if (!function) return XR_ERROR_VALIDATION_FAILURE;
    if (Equal(name, "xrGetInstanceProcAddr")) { *function = reinterpret_cast<PFN_xrVoidFunction>(Nii_xrGetInstanceProcAddr); return XR_SUCCESS; }
    if (Equal(name, "xrCreateAction")) { *function = reinterpret_cast<PFN_xrVoidFunction>(Nii_xrCreateAction); return XR_SUCCESS; }
    if (Equal(name, "xrDestroyAction")) { *function = reinterpret_cast<PFN_xrVoidFunction>(Nii_xrDestroyAction); return XR_SUCCESS; }
    if (Equal(name, "xrGetActionStateVector2f")) { *function = reinterpret_cast<PFN_xrVoidFunction>(Nii_xrGetActionStateVector2f); return XR_SUCCESS; }
    if (Equal(name, "xrGetActionStateFloat")) { *function = reinterpret_cast<PFN_xrVoidFunction>(Nii_xrGetActionStateFloat); return XR_SUCCESS; }
    return g_nextGipa(instance, name, function);
}

extern "C" XRAPI_ATTR XrResult XRAPI_CALL Nii_xrCreateApiLayerInstance(const XrInstanceCreateInfo* info, const XrApiLayerCreateInfo* layerInfo, XrInstance* instance) {
    if (!layerInfo || !layerInfo->nextInfo || !instance) return XR_ERROR_INITIALIZATION_FAILED;
    auto* next = layerInfo->nextInfo; g_nextGipa = next->nextGetInstanceProcAddr;
    XrApiLayerCreateInfo forwarded = *layerInfo; forwarded.nextInfo = next->next;
    const auto result = next->nextCreateApiLayerInstance(info, &forwarded, instance);
    if (XR_FAILED(result)) return result;
    g_nextGipa(*instance, "xrCreateAction", reinterpret_cast<PFN_xrVoidFunction*>(&g_nextCreateAction));
    g_nextGipa(*instance, "xrDestroyAction", reinterpret_cast<PFN_xrVoidFunction*>(&g_nextDestroyAction));
    g_nextGipa(*instance, "xrGetActionStateVector2f", reinterpret_cast<PFN_xrVoidFunction*>(&g_nextGetVector2));
    g_nextGipa(*instance, "xrGetActionStateFloat", reinterpret_cast<PFN_xrVoidFunction*>(&g_nextGetFloat));
    return g_nextCreateAction && g_nextDestroyAction && g_nextGetVector2 && g_nextGetFloat ? XR_SUCCESS : XR_ERROR_INITIALIZATION_FAILED;
}

extern "C" __declspec(dllexport) XRAPI_ATTR XrResult XRAPI_CALL xrNegotiateLoaderApiLayerInterface(
    const XrNegotiateLoaderInfo* loaderInfo, const char* layerName, XrNegotiateApiLayerRequest* request) {
    if (!loaderInfo || !request || !Equal(layerName, kLayerName) || loaderInfo->maxInterfaceVersion < 1) return XR_ERROR_INITIALIZATION_FAILED;
    request->layerInterfaceVersion = 1;
    request->layerApiVersion = loaderInfo->maxApiVersion;
    request->getInstanceProcAddr = Nii_xrGetInstanceProcAddr;
    request->createApiLayerInstance = Nii_xrCreateApiLayerInstance;
    return XR_SUCCESS;
}
