#pragma once

#include <algorithm>
#include <cmath>
#include <cstdint>

#ifdef _WIN32
#ifndef NOMINMAX
#define NOMINMAX
#endif
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <windows.h>
#include <sddl.h>
#endif

namespace loudify {

// Shared memory written by Loudify.exe (the user's session) and read by the
// plugin inside audiodg.exe (a service). It carries the "duck while talking"
// attenuation, which must change in real time without Equalizer APO reloading
// its configuration. Must stay in sync with src/Loudify.Core/ControlProtocol.cs.
struct ControlBlock
{
    uint32_t magic;
    uint32_t version;
    float duckDb;          // Attenuation applied on top of the gain, >= 0.
    uint32_t reserved;
    uint64_t heartbeatMs;  // GetTickCount64() of the last write by the app.
};
static_assert(sizeof(ControlBlock) == 24, "layout shared with C#");

constexpr uint32_t kControlMagic = 0x3159444C; // "LDY1"
constexpr uint32_t kControlVersion = 1;
constexpr uint64_t kControlStaleMs = 500;
constexpr double kMaxDuckDb = 40.0;

// The attenuation to apply, or 0 when the app is not running (stale heartbeat)
// or the block does not hold valid data.
inline double duckFromBlock(const ControlBlock& block, uint64_t nowMs)
{
    if (block.magic != kControlMagic || block.version != kControlVersion)
        return 0.0;
    uint64_t hb = block.heartbeatMs;
    bool fresh = hb <= nowMs ? nowMs - hb <= kControlStaleMs : hb - nowMs <= kControlStaleMs;
    double duck = block.duckDb;
    if (!fresh || !std::isfinite(duck))
        return 0.0;
    return std::clamp(duck, 0.0, kMaxDuckDb);
}

#ifdef _WIN32

class ControlChannel
{
public:
    ControlChannel()
    {
        // audiodg.exe runs as LOCAL SERVICE, which may create Global\ objects; the
        // app (a normal user, medium integrity) only opens it, so it needs read and
        // write access and a low mandatory label to pass the no-write-up check.
        SECURITY_ATTRIBUTES sa = { sizeof(sa), nullptr, FALSE };
        PSECURITY_DESCRIPTOR sd = nullptr;
        if (ConvertStringSecurityDescriptorToSecurityDescriptorW(
                L"D:(A;;GA;;;SY)(A;;GA;;;LS)(A;;GRGW;;;AU)S:(ML;;NW;;;LW)", SDDL_REVISION_1, &sd, nullptr))
            sa.lpSecurityDescriptor = sd;

        mapping_ = CreateFileMappingW(INVALID_HANDLE_VALUE, sd ? &sa : nullptr, PAGE_READWRITE,
            0, sizeof(ControlBlock), L"Global\\LoudifyControl");
        bool created = mapping_ != nullptr && GetLastError() != ERROR_ALREADY_EXISTS;
        if (sd != nullptr)
            LocalFree(sd);
        if (mapping_ == nullptr)
            return;

        block_ = static_cast<volatile ControlBlock*>(MapViewOfFile(mapping_, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(ControlBlock)));
        if (block_ != nullptr && created)
        {
            block_->duckDb = 0.0f;
            block_->heartbeatMs = 0;
            block_->version = kControlVersion;
            block_->magic = kControlMagic;
        }
    }

    ~ControlChannel()
    {
        if (block_ != nullptr)
            UnmapViewOfFile(const_cast<ControlBlock*>(block_));
        if (mapping_ != nullptr)
            CloseHandle(mapping_);
    }

    ControlChannel(const ControlChannel&) = delete;
    ControlChannel& operator=(const ControlChannel&) = delete;

    double duckDb() const
    {
        if (block_ == nullptr)
            return 0.0;
        ControlBlock copy;
        copy.magic = block_->magic;
        copy.version = block_->version;
        copy.duckDb = block_->duckDb;
        copy.reserved = 0;
        copy.heartbeatMs = block_->heartbeatMs;
        return duckFromBlock(copy, GetTickCount64());
    }

private:
    HANDLE mapping_ = nullptr;
    volatile ControlBlock* block_ = nullptr;
};

#else

// Other platforms only build the tests; there is no app to talk to.
class ControlChannel
{
public:
    double duckDb() const { return 0.0; }
};

#endif

} // namespace loudify
