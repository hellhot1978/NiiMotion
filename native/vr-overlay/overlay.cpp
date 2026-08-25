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
#include "openvr.h"

namespace {
constexpr int kWidth = 1024;
constexpr int kHeight = 640;
constexpr uint32_t kStateMagic = 0x3150564E;
constexpr wchar_t kStateMap[] = L"NiiMotion.VrPanel.v1";
constexpr wchar_t kCommandMap[] = L"NiiMotion.VrPanel.Commands.v1";
constexpr wchar_t kShowEvent[] = L"Local\\NiiMotion.VrOverlay.Show";
constexpr char kOverlayKey[] = "niirmotion.dashboard";

struct PanelState { std::string profile = "—", game = "—", locomotion = "—", devices = "—", message; float speed = 0; };

std::wstring Wide(const std::string& value) {
    if (value.empty()) return {};
    const int count = MultiByteToWideChar(CP_UTF8, 0, value.data(), static_cast<int>(value.size()), nullptr, 0);
    std::wstring output(count, L' '); MultiByteToWideChar(CP_UTF8, 0, value.data(), static_cast<int>(value.size()), output.data(), count); return output;
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
        Fill({28,430,485,535}, RGB(18,54,78)); Text(L"↻  CHECK DEVICES", {28,430,485,535}, 23, RGB(239,247,251), true, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
        Fill({539,430,996,535}, RGB(83,28,48)); Text(L"■  STOP MOVEMENT", {539,430,996,535}, 23, RGB(255,225,233), true, DT_CENTER | DT_VCENTER | DT_SINGLELINE);
        Text(L"Open from the SteamVR dashboard. Movement stops safely if a required sensor disconnects.", {35,555,989,612}, 16, RGB(126,150,164), false, DT_CENTER | DT_VCENTER | DT_WORDBREAK);
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
    vr::EVRInitError error = vr::VRInitError_None; vr::VR_Init(&error, vr::VRApplication_Overlay); if (error != vr::VRInitError_None) { CloseHandle(mutex); return 2; }
    auto* overlays = vr::VROverlay(); if (!overlays) { vr::VR_Shutdown(); CloseHandle(mutex); return 3; }
    vr::VROverlayHandle_t mainHandle = vr::k_ulOverlayHandleInvalid, thumbnailHandle = vr::k_ulOverlayHandleInvalid;
    if (overlays->CreateDashboardOverlay(kOverlayKey, "NiiMotion", &mainHandle, &thumbnailHandle) != vr::VROverlayError_None) { vr::VR_Shutdown(); CloseHandle(mutex); return 4; }
    overlays->SetOverlayInputMethod(mainHandle, vr::VROverlayInputMethod_Mouse); vr::HmdVector2_t mouseScale{{static_cast<float>(kWidth), static_cast<float>(kHeight)}}; overlays->SetOverlayMouseScale(mainHandle, &mouseScale); overlays->SetOverlayWidthInMeters(mainHandle, 2.4f);
    TextureSurface surface; Canvas canvas; SharedPanel shared; if (!surface.Initialize()) { overlays->DestroyOverlay(mainHandle); overlays->DestroyOverlay(thumbnailHandle); vr::VR_Shutdown(); CloseHandle(mutex); return 5; }
    bool running = true; auto nextFrame = std::chrono::steady_clock::now();
    while (running) {
        if (WaitForSingleObject(showEvent, 0) == WAIT_OBJECT_0) overlays->ShowDashboard(kOverlayKey);
        const PanelState state = shared.Read(); canvas.Render(state); surface.Upload(canvas.Pixels()); vr::Texture_t texture{surface.Handle(), vr::TextureType_DirectX, vr::ColorSpace_Auto}; overlays->SetOverlayTexture(mainHandle, &texture); overlays->SetOverlayTexture(thumbnailHandle, &texture);
        vr::VREvent_t event{}; while (overlays->PollNextOverlayEvent(mainHandle, &event, sizeof(event))) {
            if (event.eventType == vr::VREvent_Quit) running = false;
            if (event.eventType == vr::VREvent_MouseButtonDown) { const float x = event.data.mouse.x; const float y = kHeight - event.data.mouse.y; if (y >= 430 && y <= 535) { if (x >= 28 && x <= 485) shared.Send(2); else if (x >= 539 && x <= 996) shared.Send(1); } }
        }
        if (!vr::VR_IsRuntimeInstalled()) running = false;
        nextFrame += std::chrono::milliseconds(100); std::this_thread::sleep_until(nextFrame);
    }
    overlays->ClearOverlayTexture(mainHandle); overlays->ClearOverlayTexture(thumbnailHandle); overlays->DestroyOverlay(mainHandle); overlays->DestroyOverlay(thumbnailHandle); vr::VR_Shutdown(); CloseHandle(showEvent); ReleaseMutex(mutex); CloseHandle(mutex); return 0;
}
