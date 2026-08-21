#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <sddl.h>
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <cstring>
#include "openvr_driver.h"

namespace {
constexpr unsigned int kMagic = 0x31524D4E; // NMR1
constexpr char kPipeName[] = R"(\\.\pipe\NiiRMotion.VrOutput.v1)";

#pragma pack(push, 1)
struct MotionPacket { unsigned int magic; float x; float y; };
#pragma pack(pop)

vr::HmdQuaternion_t QuaternionFromMatrix(const vr::HmdMatrix34_t& m) {
    vr::HmdQuaternion_t q{};
    const double trace = m.m[0][0] + m.m[1][1] + m.m[2][2];
    if (trace > 0) {
        const double s = std::sqrt(trace + 1.0) * 2; q.w = 0.25 * s;
        q.x = (m.m[2][1] - m.m[1][2]) / s; q.y = (m.m[0][2] - m.m[2][0]) / s; q.z = (m.m[1][0] - m.m[0][1]) / s;
    } else if (m.m[0][0] > m.m[1][1] && m.m[0][0] > m.m[2][2]) {
        const double s = std::sqrt(1.0 + m.m[0][0] - m.m[1][1] - m.m[2][2]) * 2; q.w = (m.m[2][1] - m.m[1][2]) / s;
        q.x = 0.25 * s; q.y = (m.m[0][1] + m.m[1][0]) / s; q.z = (m.m[0][2] + m.m[2][0]) / s;
    } else if (m.m[1][1] > m.m[2][2]) {
        const double s = std::sqrt(1.0 + m.m[1][1] - m.m[0][0] - m.m[2][2]) * 2; q.w = (m.m[0][2] - m.m[2][0]) / s;
        q.x = (m.m[0][1] + m.m[1][0]) / s; q.y = 0.25 * s; q.z = (m.m[1][2] + m.m[2][1]) / s;
    } else {
        const double s = std::sqrt(1.0 + m.m[2][2] - m.m[0][0] - m.m[1][1]) * 2; q.w = (m.m[1][0] - m.m[0][1]) / s;
        q.x = (m.m[0][2] + m.m[2][0]) / s; q.y = (m.m[1][2] + m.m[2][1]) / s; q.z = 0.25 * s;
    }
    return q;
}

class LocomotionDevice final : public vr::ITrackedDeviceServerDriver {
public:
    vr::EVRInitError Activate(uint32_t objectId) override {
        objectId_ = objectId;
        const auto container = vr::VRProperties()->TrackedDeviceToPropertyContainer(objectId);
        vr::VRProperties()->SetStringProperty(container, vr::Prop_ModelNumber_String, "NiiRMotion Analog Locomotion");
        vr::VRProperties()->SetStringProperty(container, vr::Prop_ManufacturerName_String, "NiiRMotion");
        vr::VRProperties()->SetStringProperty(container, vr::Prop_ControllerType_String, "niirmotion_locomotion");
        vr::VRProperties()->SetStringProperty(container, vr::Prop_InputProfilePath_String, "{niirmotion}/input/niirmotion_profile.json");
        vr::VRProperties()->SetInt32Property(container, vr::Prop_ControllerRoleHint_Int32, vr::TrackedControllerRole_Treadmill);
        vr::VRProperties()->SetBoolProperty(container, vr::Prop_NeverTracked_Bool, false);
        vr::VRProperties()->SetBoolProperty(container, vr::Prop_IsOnDesktop_Bool, true);
        vr::VRProperties()->SetBoolProperty(container, vr::Prop_Identifiable_Bool, false);
        auto error = vr::VRDriverInput()->CreateScalarComponent(container, "/input/joystick/x", &xHandle_, vr::VRScalarType_Absolute, vr::VRScalarUnits_NormalizedTwoSided);
        if (error != vr::VRInputError_None) return vr::VRInitError_Driver_Failed;
        error = vr::VRDriverInput()->CreateScalarComponent(container, "/input/joystick/y", &yHandle_, vr::VRScalarType_Absolute, vr::VRScalarUnits_NormalizedTwoSided);
        if (error != vr::VRInputError_None) return vr::VRInitError_Driver_Failed;
        error = vr::VRDriverInput()->CreateBooleanComponent(container, "/input/joystick/click", &clickHandle_);
        if (error != vr::VRInputError_None) return vr::VRInitError_Driver_Failed;
        error = vr::VRDriverInput()->CreateBooleanComponent(container, "/input/joystick/touch", &touchHandle_);
        if (error != vr::VRInputError_None) return vr::VRInitError_Driver_Failed;
        error = vr::VRDriverInput()->CreateScalarComponent(container, "/input/turnstick/x", &turnXHandle_, vr::VRScalarType_Absolute, vr::VRScalarUnits_NormalizedTwoSided);
        if (error != vr::VRInputError_None) return vr::VRInitError_Driver_Failed;
        error = vr::VRDriverInput()->CreateScalarComponent(container, "/input/turnstick/y", &turnYHandle_, vr::VRScalarType_Absolute, vr::VRScalarUnits_NormalizedTwoSided);
        if (error != vr::VRInputError_None) return vr::VRInitError_Driver_Failed;
        error = vr::VRDriverInput()->CreateBooleanComponent(container, "/input/turnstick/click", &turnClickHandle_);
        if (error != vr::VRInputError_None) return vr::VRInitError_Driver_Failed;
        error = vr::VRDriverInput()->CreateBooleanComponent(container, "/input/turnstick/touch", &turnTouchHandle_);
        if (error != vr::VRInputError_None) return vr::VRInitError_Driver_Failed;
        char message[192];
        std::snprintf(message, sizeof(message), "NiiRMotion components created: x=%llu y=%llu click=%llu touch=%llu.",
            static_cast<unsigned long long>(xHandle_), static_cast<unsigned long long>(yHandle_),
            static_cast<unsigned long long>(clickHandle_), static_cast<unsigned long long>(touchHandle_));
        vr::VRDriverLog()->Log(message);
        Update(0, 0); return vr::VRInitError_None;
    }
    void Deactivate() override { Update(0, 0); objectId_ = vr::k_unTrackedDeviceIndexInvalid; }
    void EnterStandby() override { Update(0, 0); }
    void* GetComponent(const char*) override { return nullptr; }
    void DebugRequest(const char*, char* response, uint32_t size) override { if (size) response[0] = 0; }
    vr::DriverPose_t GetPose() override {
        vr::DriverPose_t pose{}; pose.deviceIsConnected = true; pose.poseIsValid = true;
        pose.result = vr::TrackingResult_Running_OK; pose.qWorldFromDriverRotation.w = 1; pose.qDriverFromHeadRotation.w = 1; pose.qRotation.w = 1;
        vr::TrackedDevicePose_t tracked[vr::k_unMaxTrackedDeviceCount]{};
        vr::VRServerDriverHost()->GetRawTrackedDevicePoses(0, tracked, vr::k_unMaxTrackedDeviceCount);
        const auto& hmd = tracked[vr::k_unTrackedDeviceIndex_Hmd];
        if (hmd.bDeviceIsConnected && hmd.bPoseIsValid) {
            const auto now = GetTickCount64();
            const double yaw = std::atan2(hmd.mDeviceToAbsoluteTracking.m[0][2], hmd.mDeviceToAbsoluteTracking.m[2][2]);
            if (lastHmdPoseTick_ != 0 && now > lastHmdPoseTick_) {
                double delta = yaw - lastHmdYaw_;
                while (delta > 3.141592653589793) delta -= 6.283185307179586;
                while (delta < -3.141592653589793) delta += 6.283185307179586;
                const double yawRate = std::abs(delta) * 1000.0 / static_cast<double>(now - lastHmdPoseTick_);
                if (yawRate >= 1.60) turnSuppressUntil_ = now + 120;
            }
            lastHmdYaw_ = yaw; lastHmdPoseTick_ = now;
            pose.qRotation = QuaternionFromMatrix(hmd.mDeviceToAbsoluteTracking);
            pose.vecPosition[0] = hmd.mDeviceToAbsoluteTracking.m[0][3];
            pose.vecPosition[1] = hmd.mDeviceToAbsoluteTracking.m[1][3];
            pose.vecPosition[2] = hmd.mDeviceToAbsoluteTracking.m[2][3];
        }
        return pose;
    }
    void PublishPose() { if (objectId_ != vr::k_unTrackedDeviceIndexInvalid) vr::VRServerDriverHost()->TrackedDevicePoseUpdated(objectId_, GetPose(), sizeof(vr::DriverPose_t)); }
    void Update(float x, float y) {
        if (!xHandle_ || !yHandle_) return;
        const float safeTurn = std::clamp(x, -1.0f, 1.0f);
        const float safeY = GetTickCount64() < turnSuppressUntil_ ? 0.0f : std::clamp(y, -1.0f, 1.0f);
        const bool active = (safeY * safeY) > 0.0001f;
        const auto xError = vr::VRDriverInput()->UpdateScalarComponent(xHandle_, 0, 0);
        const auto yError = vr::VRDriverInput()->UpdateScalarComponent(yHandle_, safeY, 0);
        vr::VRDriverInput()->UpdateScalarComponent(turnXHandle_, safeTurn, 0);
        vr::VRDriverInput()->UpdateScalarComponent(turnYHandle_, 0, 0);
        const bool turning = std::abs(safeTurn) > 0.01f;
        vr::VRDriverInput()->UpdateBooleanComponent(turnClickHandle_, turning, 0);
        vr::VRDriverInput()->UpdateBooleanComponent(turnTouchHandle_, turning, 0);
        const auto clickError = vr::VRDriverInput()->UpdateBooleanComponent(clickHandle_, active, 0);
        const auto touchError = vr::VRDriverInput()->UpdateBooleanComponent(touchHandle_, active, 0);
        if (active != lastActive_) {
            char message[224];
            std::snprintf(message, sizeof(message),
                "NiiRMotion input %s x=%.3f y=%.3f updateErrors=%d/%d/%d/%d.",
                active ? "ACTIVE" : "ZERO", safeTurn, safeY,
                static_cast<int>(xError), static_cast<int>(yError), static_cast<int>(clickError),
                static_cast<int>(touchError));
            vr::VRDriverLog()->Log(message);
            lastActive_ = active;
        }
    }
private:
    vr::TrackedDeviceIndex_t objectId_ = vr::k_unTrackedDeviceIndexInvalid;
    vr::VRInputComponentHandle_t xHandle_ = 0, yHandle_ = 0, clickHandle_ = 0, touchHandle_ = 0, turnXHandle_ = 0, turnYHandle_ = 0, turnClickHandle_ = 0, turnTouchHandle_ = 0;
    bool lastActive_ = false;
    double lastHmdYaw_ = 0;
    ULONGLONG lastHmdPoseTick_ = 0, turnSuppressUntil_ = 0;
};

class Provider final : public vr::IServerTrackedDeviceProvider {
public:
    vr::EVRInitError Init(vr::IVRDriverContext* context) override {
        VR_INIT_SERVER_DRIVER_CONTEXT(context);
        PSECURITY_DESCRIPTOR descriptor = nullptr;
        SECURITY_ATTRIBUTES security{sizeof(SECURITY_ATTRIBUTES), nullptr, FALSE};
        // SteamVR may run vrserver at high integrity while the desktop app remains
        // medium integrity.  The explicit low mandatory label keeps this local-only
        // pipe writable by the authenticated interactive user without elevation.
        if (ConvertStringSecurityDescriptorToSecurityDescriptorA("D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GRGW;;;AU)S:(ML;;NW;;;LW)", SDDL_REVISION_1, &descriptor, nullptr)) security.lpSecurityDescriptor = descriptor;
        pipe_ = CreateNamedPipeA(kPipeName, PIPE_ACCESS_INBOUND,
            PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_NOWAIT | PIPE_REJECT_REMOTE_CLIENTS, 1, sizeof(MotionPacket), sizeof(MotionPacket), 0, security.lpSecurityDescriptor ? &security : nullptr);
        if (descriptor) LocalFree(descriptor);
        if (pipe_ == INVALID_HANDLE_VALUE) return vr::VRInitError_Driver_Failed;
        vr::VRServerDriverHost()->TrackedDeviceAdded("NIIRMOTION_LOCOMOTION_V1", vr::TrackedDeviceClass_Controller, &device_);
        lastPacket_ = GetTickCount64(); vr::VRDriverLog()->Log("NiiRMotion analog output initialized (safe zero)."); return vr::VRInitError_None;
    }
    void Cleanup() override { device_.Update(0, 0); if (pipe_ != INVALID_HANDLE_VALUE) CloseHandle(pipe_); pipe_ = INVALID_HANDLE_VALUE; VR_CLEANUP_SERVER_DRIVER_CONTEXT(); }
    const char* const* GetInterfaceVersions() override { return vr::k_InterfaceVersions; }
    void RunFrame() override {
        device_.PublishPose();
        if (pipe_ == INVALID_HANDLE_VALUE) return;
        if (!connected_) {
            const BOOL ok = ConnectNamedPipe(pipe_, nullptr);
            connected_ = ok || GetLastError() == ERROR_PIPE_CONNECTED;
            if (connected_) vr::VRDriverLog()->Log("NiiRMotion pipe client connected.");
        }
        if (connected_) {
            DWORD available = 0;
            if (!PeekNamedPipe(pipe_, nullptr, 0, nullptr, &available, nullptr)) {
                device_.Update(0, 0); DisconnectNamedPipe(pipe_); connected_ = false;
                vr::VRDriverLog()->Log("NiiRMotion pipe client disconnected; safe zero applied.");
            } else while (available >= sizeof(MotionPacket)) {
                MotionPacket packet{}; DWORD read = 0;
                if (!ReadFile(pipe_, &packet, sizeof(packet), &read, nullptr) || read != sizeof(packet)) break;
                if (packet.magic == kMagic) { device_.Update(packet.x, packet.y); lastPacket_ = GetTickCount64(); }
                if (!PeekNamedPipe(pipe_, nullptr, 0, nullptr, &available, nullptr)) break;
            }
        }
        if (GetTickCount64() - lastPacket_ > 250) device_.Update(0, 0);
    }
    bool ShouldBlockStandbyMode() override { return false; }
    void EnterStandby() override { device_.Update(0, 0); }
    void LeaveStandby() override { }
private:
    LocomotionDevice device_; HANDLE pipe_ = INVALID_HANDLE_VALUE; bool connected_ = false; ULONGLONG lastPacket_ = 0;
};

Provider provider;
}

extern "C" __declspec(dllexport) void* HmdDriverFactory(const char* interfaceName, int* returnCode) {
    if (std::strcmp(vr::IServerTrackedDeviceProvider_Version, interfaceName) == 0) return &provider;
    if (returnCode) *returnCode = vr::VRInitError_Init_InterfaceNotFound; return nullptr;
}
