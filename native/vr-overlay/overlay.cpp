#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <d3d11.h>
#include <algorithm>
#include <chrono>
#include <cmath>
#include <cstdint>
#include <string>
#include <thread>
#include <vector>
#include <fstream>
#include <filesystem>
#include "openvr.h"

namespace {
constexpr int kWidth = 1024;
constexpr int kHeight = 640;
constexpr uint32_t kStateMagic = 0x3150564E;
constexpr wchar_t kStateMap[] = L"NiiMotion.VrPanel.v1";
constexpr wchar_t kCommandMap[] = L"NiiMotion.VrPanel.Commands.v1";
constexpr wchar_t kHmdPoseMap[] = L"Local\\NiiMotion.HmdPose.v1";
constexpr uint32_t kHmdPoseMagic = 0x31444D48;
constexpr wchar_t kShowEvent[] = L"Local\\NiiMotion.VrOverlay.Show";
constexpr char kOverlayKey[] = "niirmotion.dashboard";

struct PanelState { std::string profile = "—", game = "—", locomotion = "—", devices = "—", message; float speed = 0; };
#pragma pack(push, 1)
struct SharedHmdPose { uint32_t magic = kHmdPoseMagic; uint32_t version = 1; int64_t sequence = 0; int64_t qpcTicks = 0; uint32_t tracked = 0; float position[3]{}; float orientation[4]{0,0,0,1}; };
#pragma pack(pop)

class HmdPosePublisher {
    HANDLE mapping_ = nullptr; SharedHmdPose* pose_ = nullptr; int64_t sequence_ = 0;
    static void Quaternion(const vr::HmdMatrix34_t& m, float* q) {
        const float trace = m.m[0][0] + m.m[1][1] + m.m[2][2];
        if (trace > 0) { const float s = std::sqrt(trace + 1.0f) * 2; q[3] = .25f*s; q[0]=(m.m[2][1]-m.m[1][2])/s; q[1]=(m.m[0][2]-m.m[2][0])/s; q[2]=(m.m[1][0]-m.m[0][1])/s; }
        else if (m.m[0][0] > m.m[1][1] && m.m[0][0] > m.m[2][2]) { const float s=std::sqrt(1+m.m[0][0]-m.m[1][1]-m.m[2][2])*2; q[3]=(m.m[2][1]-m.m[1][2])/s; q[0]=.25f*s; q[1]=(m.m[0][1]+m.m[1][0])/s; q[2]=(m.m[0][2]+m.m[2][0])/s; }
        else if (m.m[1][1] > m.m[2][2]) { const float s=std::sqrt(1+m.m[1][1]-m.m[0][0]-m.m[2][2])*2; q[3]=(m.m[0][2]-m.m[2][0])/s; q[0]=(m.m[0][1]+m.m[1][0])/s; q[1]=.25f*s; q[2]=(m.m[1][2]+m.m[2][1])/s; }
        else { const float s=std::sqrt(1+m.m[2][2]-m.m[0][0]-m.m[1][1])*2; q[3]=(m.m[1][0]-m.m[0][1])/s; q[0]=(m.m[0][2]+m.m[2][0])/s; q[1]=(m.m[1][2]+m.m[2][1])/s; q[2]=.25f*s; }
    }
public:
    HmdPosePublisher() { mapping_ = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, 0, sizeof(SharedHmdPose), kHmdPoseMap); if (mapping_) pose_ = static_cast<SharedHmdPose*>(MapViewOfFile(mapping_, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(SharedHmdPose))); }
    ~HmdPosePublisher() { if (pose_) { pose_->tracked = 0; FlushViewOfFile(pose_, sizeof(SharedHmdPose)); UnmapViewOfFile(pose_); } if (mapping_) CloseHandle(mapping_); }
    void Publish(vr::IVRSystem* system) { if (!pose_ || !system) return; vr::TrackedDevicePose_t poses[vr::k_unMaxTrackedDeviceCount]{}; system->GetDeviceToAbsoluteTrackingPose(vr::TrackingUniverseStanding, 0, poses, vr::k_unMaxTrackedDeviceCount); const auto& hmd=poses[vr::k_unTrackedDeviceIndex_Hmd]; SharedHmdPose next{}; next.sequence=++sequence_; LARGE_INTEGER tick{}; QueryPerformanceCounter(&tick); next.qpcTicks=tick.QuadPart; next.tracked=hmd.bPoseIsValid && hmd.bDeviceIsConnected; if (next.tracked) { const auto& m=hmd.mDeviceToAbsoluteTracking; next.position[0]=m.m[0][3]; next.position[1]=m.m[1][3]; next.position[2]=m.m[2][3]; Quaternion(m,next.orientation); } *pose_=next; FlushViewOfFile(pose_, sizeof(SharedHmdPose)); }
};

std::wstring Wide(const std::string& value) {
    if (value.empty()) return {};
    const int count = MultiByteToWideChar(CP_UTF8, 0, value.data(), static_cast<int>(value.size()), nullptr, 0);
    std::wstring output(count, L' '); MultiByteToWideChar(CP_UTF8, 0, value.data(), static_cast<int>(value.size()), output.data(), count); return output;
}

std::string Utf8(const std::wstring& value) {
    if (value.empty()) return {};
    const int count = WideCharToMultiByte(CP_UTF8, 0, value.data(), static_cast<int>(value.size()), nullptr, 0, nullptr, nullptr);
    std::string output(count, ' '); WideCharToMultiByte(CP_UTF8, 0, value.data(), static_cast<int>(value.size()), output.data(), count, nullptr, nullptr); return output;
}

std::wstring SiblingPath(const wchar_t* name) {
    wchar_t executable[MAX_PATH]{}; GetModuleFileNameW(nullptr, executable, MAX_PATH); std::wstring path(executable); const auto slash = path.find_last_of(L"\\/"); return path.substr(0, slash + 1) + name;
}

void Trace(const std::string& message) {
    wchar_t temp[MAX_PATH]{}; GetTempPathW(MAX_PATH, temp); std::ofstream log(std::filesystem::path(temp) / L"NiiMotion.VrOverlay.log", std::ios::app); if (log) log << message << '\n';
}

void ShowSteamVrDesktop(vr::IVROverlay* overlays) {
    const char* keys[] = { "valve.steam.desktop", "system.desktop.1", "system.desktop" };
    for (const char* key : keys) {
        vr::VROverlayHandle_t handle = vr::k_ulOverlayHandleInvalid; const auto found = overlays->FindOverlay(key, &handle); Trace(std::string("desktop key=") + key + " result=" + std::to_string(found));
        if (found == vr::VROverlayError_None) { overlays->ShowDashboard(key); overlays->ShowOverlay(handle); Trace(std::string("desktop shown=") + key); return; }
    }
    overlays->ShowDashboard("valve.steam.desktop"); Trace("desktop fallback=valve.steam.desktop");
}

std::string JsonPath(const std::wstring& value) { std::string utf8 = Utf8(value), result; for (const char c : utf8) { if (c == '\\' || c == '"') result.push_back('\\'); result.push_back(c); } return result; }

std::string WriteRuntimeManifest() {
    wchar_t executable[MAX_PATH]{}, temp[MAX_PATH]{}; GetModuleFileNameW(nullptr, executable, MAX_PATH); GetTempPathW(MAX_PATH, temp); const std::filesystem::path path = std::filesystem::path(temp) / L"NiiMotion.VrOverlay.vrmanifest";
    std::ofstream file(path, std::ios::trunc); file << "{\"source\":\"builtin\",\"applications\":[{\"app_key\":\"com.niirmotion.dashboard\",\"launch_type\":\"binary\",\"binary_path_windows\":\"" << JsonPath(executable) << "\",\"is_dashboard_overlay\":true,\"image_path\":\"" << JsonPath(SiblingPath(L"dashboard-icon.png")) << "\",\"strings\":{\"en_us\":{\"name\":\"NiiMotion\"},\"tr_tr\":{\"name\":\"NiiMotion\"}}}]}"; file.close(); return Utf8(path.wstring());
}

void AppendUtf8(std::string& output, uint32_t code) {
    if (code <= 0x7F) output.push_back(static_cast<char>(code));
    else if (code <= 0x7FF) { output.push_back(static_cast<char>(0xC0 | code >> 6)); output.push_back(static_cast<char>(0x80 | (code & 0x3F))); }
    else { output.push_back(static_cast<char>(0xE0 | code >> 12)); output.push_back(static_cast<char>(0x80 | ((code >> 6) & 0x3F))); output.push_back(static_cast<char>(0x80 | (code & 0x3F))); }
}

std::string JsonString(const std::string& json, const char* property) {
    const std::string needle = std::string("\"") + property + "\":"; auto pos = json.find(needle); if (pos == std::string::npos) return {};
    pos += needle.size(); while (pos < json.size() && json[pos] == ' ') ++pos; if (pos >= json.size() || json[pos++] != '"') return {};
    std::string output;
    while (pos < json.size()) {
        const char c = json[pos++]; if (c == '"') break; if (c != '\\') { output.push_back(c); continue; } if (pos >= json.size()) break;
        const char escaped = json[pos++];
        if (escaped == 'u' && pos + 4 <= json.size()) { uint32_t code = 0; for (int i = 0; i < 4; ++i) { const char h = json[pos++]; code = code * 16 + (h >= '0' && h <= '9' ? h - '0' : h >= 'a' && h <= 'f' ? h - 'a' + 10 : h - 'A' + 10); } AppendUtf8(output, code); }
        else output.push_back(escaped == 'n' ? '\n' : escaped == 'r' ? '\r' : escaped == 't' ? '\t' : escaped);
    }
    return output;
}

float JsonFloat(const std::string& json, const char* property) {
    const std::string needle = std::string("\"") + property + "\":"; auto pos = json.find(needle); if (pos == std::string::npos) return 0;
    pos += needle.size(); try { return std::stof(json.substr(pos)); } catch (...) { return 0; }
}

class SharedPanel {
    HANDLE stateMap_ = nullptr, commandMap_ = nullptr; uint8_t* state_ = nullptr; uint8_t* commands_ = nullptr;
public:
    SharedPanel() {
        stateMap_ = OpenFileMappingW(FILE_MAP_READ, FALSE, kStateMap); if (stateMap_) state_ = static_cast<uint8_t*>(MapViewOfFile(stateMap_, FILE_MAP_READ, 0, 0, 4096));
        commandMap_ = OpenFileMappingW(FILE_MAP_WRITE, FALSE, kCommandMap); if (commandMap_) commands_ = static_cast<uint8_t*>(MapViewOfFile(commandMap_, FILE_MAP_WRITE, 0, 0, 64));
    }
    ~SharedPanel() { if (state_) UnmapViewOfFile(state_); if (commands_) UnmapViewOfFile(commands_); if (stateMap_) CloseHandle(stateMap_); if (commandMap_) CloseHandle(commandMap_); }
    PanelState Read() const {
        PanelState result; if (!state_ || *reinterpret_cast<const uint32_t*>(state_) != kStateMagic) { result.message = "NiiMotion desktop app is not publishing status."; return result; }
        const int length = *reinterpret_cast<const int*>(state_ + 4); if (length <= 0 || length > 4000) return result;
        const std::string json(reinterpret_cast<const char*>(state_ + 8), length);
        result.profile = JsonString(json, "Profile"); result.game = JsonString(json, "Game"); result.locomotion = JsonString(json, "Locomotion"); result.devices = JsonString(json, "DeviceSummary"); result.message = JsonString(json, "Message"); result.speed = std::clamp(JsonFloat(json, "Speed"), 0.0f, 1.0f); return result;
    }
    void Send(int command) const { if (!commands_) return; *reinterpret_cast<int*>(commands_) = command; LARGE_INTEGER tick{}; QueryPerformanceCounter(&tick); *reinterpret_cast<int64_t*>(commands_ + 8) = tick.QuadPart; FlushViewOfFile(commands_, 16); }
};

class Canvas {
    HDC dc_ = nullptr; HBITMAP bitmap_ = nullptr; void* pixels_ = nullptr; HFONT font_ = nullptr;
public:
    Canvas() {
        dc_ = CreateCompatibleDC(nullptr); BITMAPINFO info{}; info.bmiHeader.biSize = sizeof(BITMAPINFOHEADER); info.bmiHeader.biWidth = kWidth; info.bmiHeader.biHeight = -kHeight; info.bmiHeader.biPlanes = 1; info.bmiHeader.biBitCount = 32; info.bmiHeader.biCompression = BI_RGB;
        bitmap_ = CreateDIBSection(dc_, &info, DIB_RGB_COLORS, &pixels_, nullptr, 0); SelectObject(dc_, bitmap_); SetBkMode(dc_, TRANSPARENT);
    }
    ~Canvas() { if (font_) DeleteObject(font_); if (bitmap_) DeleteObject(bitmap_); if (dc_) DeleteDC(dc_); }
    void Fill(RECT rect, COLORREF color) { HBRUSH brush = CreateSolidBrush(color); FillRect(dc_, &rect, brush); DeleteObject(brush); }
    void Text(const std::wstring& text, RECT rect, int size, COLORREF color, bool bold = false, UINT format = DT_LEFT | DT_VCENTER | DT_SINGLELINE) {
        if (font_) DeleteObject(font_); font_ = CreateFontW(-size, 0, 0, 0, bold ? FW_SEMIBOLD : FW_NORMAL, FALSE, FALSE, FALSE, DEFAULT_CHARSET, OUT_DEFAULT_PRECIS, CLIP_DEFAULT_PRECIS, CLEARTYPE_QUALITY, DEFAULT_PITCH, L"Segoe UI"); SelectObject(dc_, font_); SetTextColor(dc_, color); DrawTextW(dc_, text.c_str(), -1, &rect, format);
    }
    void Render(const PanelState& state) {
        Fill({0,0,kWidth,kHeight}, RGB(5,12,18)); Fill({28,25,996,105}, RGB(12,29,42));
        Text(L"NiiMotion", {55,32,500,70}, 34, RGB(244,248,250), true); Text(L"VR LOCOMOTION · LIVE", {57,72,500,96}, 15, RGB(62,187,244), true);
        const bool active = state.locomotion != "Kapalı" && state.locomotion != "Off" && state.locomotion != "OFF"; Fill({800,45,967,87}, active ? RGB(16,71,58) : RGB(69,34,45)); Text(active ? L"● ACTIVE" : L"● OFF", {800,45,967,87}, 18, active ? RGB(85,226,193) : RGB(255,130,157), true, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
        Fill({28,125,996,400}, RGB(11,23,33));
        Text(L"PROFILE", {55,147,220,175}, 14, RGB(73,188,244), true); Text(Wide(state.profile), {55,176,470,222}, 25, RGB(244,248,250), true);
        Text(L"GAME", {535,147,700,175}, 14, RGB(73,188,244), true); Text(Wide(state.game), {535,176,960,222}, 25, RGB(244,248,250), true);
        Text(L"LOCOMOTION", {55,245,220,273}, 14, RGB(73,188,244), true); Text(Wide(state.locomotion), {55,273,470,317}, 22, RGB(86,223,185), true);
        Text(L"DEVICES", {535,245,700,273}, 14, RGB(73,188,244), true); Text(Wide(state.devices), {535,273,960,317}, 22, RGB(224,234,240), true);
        Fill({55,342,960,358}, RGB(29,49,63)); const int speedWidth = static_cast<int>(905 * state.speed); if (speedWidth > 0) Fill({55,342,55 + speedWidth,358}, RGB(30,159,224));
        Text(Wide(state.message), {55,365,960,395}, 16, RGB(156,177,190), false, DT_LEFT | DT_VCENTER | DT_END_ELLIPSIS | DT_SINGLELINE);
        Fill({28,430,330,535}, active ? RGB(83,28,48) : RGB(16,77,103)); Text(active ? L"■  STOP" : L"▶  START", {28,430,330,535}, 23, active ? RGB(255,225,233) : RGB(228,247,255), true, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
        Fill({345,430,661,535}, RGB(18,54,78)); Text(L"↻  DEVICES", {345,430,661,535}, 22, RGB(239,247,251), true, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
        Fill({676,430,996,535}, RGB(24,43,57)); Text(L"▣  DESKTOP", {676,430,996,535}, 22, RGB(239,247,251), true, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
        Text(L"Open from the SteamVR dashboard. Movement stops safely if a required sensor disconnects.", {35,555,989,612}, 16, RGB(126,150,164), false, DT_CENTER | DT_VCENTER | DT_WORDBREAK);
        // GDI writes BGR but leaves the alpha byte at zero. OpenVR honors that
        // byte, so without normalizing it the dashboard texture is invisible.
        auto* bgra = static_cast<uint8_t*>(pixels_);
        for (int pixel = 0; pixel < kWidth * kHeight; ++pixel) bgra[pixel * 4 + 3] = 255;
    }
    void RenderIcon() {
        Fill({0,0,kWidth,kHeight}, RGB(5,12,18));
        Fill({300,55,724,585}, RGB(9,94,153));
        Text(L"N", {300,55,724,585}, 310, RGB(246,251,255), true, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
        auto* bgra = static_cast<uint8_t*>(pixels_); for (int pixel = 0; pixel < kWidth * kHeight; ++pixel) bgra[pixel * 4 + 3] = 255;
    }
    const void* Pixels() const { return pixels_; }
};

class TextureSurface {
    ID3D11Device* device_ = nullptr; ID3D11DeviceContext* context_ = nullptr; ID3D11Texture2D* texture_ = nullptr;
public:
    bool Initialize() {
        D3D_FEATURE_LEVEL level{}; if (FAILED(D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, D3D11_CREATE_DEVICE_BGRA_SUPPORT, nullptr, 0, D3D11_SDK_VERSION, &device_, &level, &context_))) return false;
        D3D11_TEXTURE2D_DESC desc{}; desc.Width = kWidth; desc.Height = kHeight; desc.MipLevels = 1; desc.ArraySize = 1; desc.Format = DXGI_FORMAT_B8G8R8A8_UNORM; desc.SampleDesc.Count = 1; desc.Usage = D3D11_USAGE_DYNAMIC; desc.BindFlags = D3D11_BIND_SHADER_RESOURCE; desc.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
        return SUCCEEDED(device_->CreateTexture2D(&desc, nullptr, &texture_));
    }
    ~TextureSurface() { if (texture_) texture_->Release(); if (context_) context_->Release(); if (device_) device_->Release(); }
    bool Upload(const void* pixels) { D3D11_MAPPED_SUBRESOURCE mapped{}; if (FAILED(context_->Map(texture_, 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped))) return false; const auto* source = static_cast<const uint8_t*>(pixels); for (int y = 0; y < kHeight; ++y) memcpy(static_cast<uint8_t*>(mapped.pData) + y * mapped.RowPitch, source + y * kWidth * 4, kWidth * 4); context_->Unmap(texture_, 0); return true; }
    void* Handle() const { return texture_; }
};
}

int WINAPI wWinMain(HINSTANCE, HINSTANCE, PWSTR, int) {
    HANDLE mutex = CreateMutexW(nullptr, TRUE, L"Local\\NiiMotion.VrOverlay.Singleton"); if (!mutex || GetLastError() == ERROR_ALREADY_EXISTS) return 0;
    HANDLE showEvent = CreateEventW(nullptr, FALSE, FALSE, kShowEvent); if (!showEvent) { ReleaseMutex(mutex); CloseHandle(mutex); return 6; }
    vr::EVRInitError error = vr::VRInitError_None; vr::VR_Init(&error, vr::VRApplication_Overlay); if (error != vr::VRInitError_None) { CloseHandle(showEvent); CloseHandle(mutex); return 2; }
    auto* overlays = vr::VROverlay(); if (!overlays) { vr::VR_Shutdown(); CloseHandle(mutex); return 3; }
    if (auto* applications = vr::VRApplications()) {
        const auto packagedManifest = Utf8(SiblingPath(L"niirmotion.vrmanifest")); applications->RemoveApplicationManifest(packagedManifest.c_str()); const auto manifest = WriteRuntimeManifest(); applications->AddApplicationManifest(manifest.c_str(), false); applications->IdentifyApplication(GetCurrentProcessId(), "com.niirmotion.dashboard");
    }
    vr::VROverlayHandle_t mainHandle = vr::k_ulOverlayHandleInvalid, thumbnailHandle = vr::k_ulOverlayHandleInvalid;
    if (overlays->CreateDashboardOverlay(kOverlayKey, "NiiMotion", &mainHandle, &thumbnailHandle) != vr::VROverlayError_None) { vr::VR_Shutdown(); CloseHandle(mutex); return 4; }
    overlays->SetOverlayInputMethod(mainHandle, vr::VROverlayInputMethod_Mouse); vr::HmdVector2_t mouseScale{{static_cast<float>(kWidth), static_cast<float>(kHeight)}}; overlays->SetOverlayMouseScale(mainHandle, &mouseScale); overlays->SetOverlayWidthInMeters(mainHandle, 2.4f);
    TextureSurface surface, thumbnailSurface; Canvas canvas, thumbnailCanvas; SharedPanel shared; HmdPosePublisher hmdPose; if (!surface.Initialize() || !thumbnailSurface.Initialize()) { overlays->DestroyOverlay(mainHandle); overlays->DestroyOverlay(thumbnailHandle); vr::VR_Shutdown(); CloseHandle(showEvent); CloseHandle(mutex); return 5; }
    const auto iconPath = Utf8(SiblingPath(L"dashboard-icon.png"));
    if (overlays->SetOverlayFromFile(thumbnailHandle, iconPath.c_str()) != vr::VROverlayError_None) { thumbnailCanvas.RenderIcon(); thumbnailSurface.Upload(thumbnailCanvas.Pixels()); vr::Texture_t thumbnailTexture{thumbnailSurface.Handle(), vr::TextureType_DirectX, vr::ColorSpace_Auto}; overlays->SetOverlayTexture(thumbnailHandle, &thumbnailTexture); }
    bool running = true; auto nextFrame = std::chrono::steady_clock::now();
    while (running) {
        hmdPose.Publish(vr::VRSystem());
        if (WaitForSingleObject(showEvent, 0) == WAIT_OBJECT_0) overlays->ShowDashboard(kOverlayKey);
        const PanelState state = shared.Read(); const bool active = state.locomotion != "Kapalı" && state.locomotion != "Off" && state.locomotion != "OFF"; canvas.Render(state); surface.Upload(canvas.Pixels()); vr::Texture_t texture{surface.Handle(), vr::TextureType_DirectX, vr::ColorSpace_Auto}; overlays->SetOverlayTexture(mainHandle, &texture);
        vr::VREvent_t event{}; while (overlays->PollNextOverlayEvent(mainHandle, &event, sizeof(event))) {
            if (event.eventType == vr::VREvent_Quit) running = false;
            if (event.eventType == vr::VREvent_MouseButtonDown || event.eventType == vr::VREvent_MouseButtonUp) {
                float x = event.data.mouse.x, rawY = event.data.mouse.y; if (x >= 0 && x <= 1.5f) x *= kWidth; if (rawY >= 0 && rawY <= 1.5f) rawY *= kHeight; const float flippedY = kHeight - rawY; const bool buttonRow = (rawY >= 410 && rawY <= 555) || (flippedY >= 410 && flippedY <= 555);
                Trace("mouse event=" + std::to_string(event.eventType) + " x=" + std::to_string(x) + " y=" + std::to_string(rawY));
                if (event.eventType == vr::VREvent_MouseButtonUp && buttonRow) { if (x >= 10 && x <= 335) { Trace("command movement"); shared.Send(active ? 1 : 3); } else if (x >= 335 && x <= 670) { Trace("command devices"); shared.Send(2); } else if (x >= 670 && x <= 1014) { Trace("command desktop"); ShowSteamVrDesktop(overlays); } }
            }
        }
        if (!vr::VR_IsRuntimeInstalled()) running = false;
        nextFrame += std::chrono::milliseconds(100); std::this_thread::sleep_until(nextFrame);
    }
    overlays->ClearOverlayTexture(mainHandle); overlays->ClearOverlayTexture(thumbnailHandle); overlays->DestroyOverlay(mainHandle); overlays->DestroyOverlay(thumbnailHandle); vr::VR_Shutdown(); CloseHandle(showEvent); ReleaseMutex(mutex); CloseHandle(mutex); return 0;
}
