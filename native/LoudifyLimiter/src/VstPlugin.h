#pragma once

#include <cstdint>

// The subset of the VST 2.4 binary interface needed by an effect plugin.
// Layout matches the AEffect structure that VST 2 hosts expect.

#if defined(_WIN32)
#define VSTCALL __cdecl
#define LOUDIFY_EXPORT __declspec(dllexport)
#else
#define VSTCALL
#define LOUDIFY_EXPORT __attribute__((visibility("default")))
#endif

constexpr int32_t kVstMagic = ('V' << 24) | ('s' << 16) | ('t' << 8) | 'P';
constexpr int32_t kVstUniqueId = ('L' << 24) | ('d' << 16) | ('f' << 8) | 'L';
constexpr int32_t kEffFlagsCanReplacing = 1 << 4;

#pragma pack(push, 8)

struct AEffect;

typedef intptr_t (VSTCALL* VstHostCallback)(AEffect* effect, int32_t opcode, int32_t index, intptr_t value, void* ptr, float opt);
typedef intptr_t (VSTCALL* VstDispatcherProc)(AEffect* effect, int32_t opcode, int32_t index, intptr_t value, void* ptr, float opt);
typedef void (VSTCALL* VstProcessProc)(AEffect* effect, float** inputs, float** outputs, int32_t sampleFrames);
typedef void (VSTCALL* VstProcessDoubleProc)(AEffect* effect, double** inputs, double** outputs, int32_t sampleFrames);
typedef void (VSTCALL* VstSetParameterProc)(AEffect* effect, int32_t index, float parameter);
typedef float (VSTCALL* VstGetParameterProc)(AEffect* effect, int32_t index);

struct AEffect
{
    int32_t magic;
    VstDispatcherProc dispatcher;
    VstProcessProc process;
    VstSetParameterProc setParameter;
    VstGetParameterProc getParameter;
    int32_t numPrograms;
    int32_t numParams;
    int32_t numInputs;
    int32_t numOutputs;
    int32_t flags;
    intptr_t reserved1;
    intptr_t reserved2;
    int32_t initialDelay;
    int32_t realQualities;
    int32_t offQualities;
    float ioRatio;
    void* object;
    void* user;
    int32_t uniqueID;
    int32_t version;
    VstProcessProc processReplacing;
    VstProcessDoubleProc processDoubleReplacing;
    char future[56];
};

#pragma pack(pop)

extern "C" LOUDIFY_EXPORT AEffect* VSTCALL VSTPluginMain(VstHostCallback host);
