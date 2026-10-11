// Minimal VST 2.4 effect exposing the Loudify limiter to Equalizer APO.
//
// Only the parts of the VST 2 ABI that Equalizer APO uses are declared here.
// The plugin has no editor and no chunks: Equalizer APO sets the parameters by
// name ("Gain", "Ceiling", "Release") from the VSTPlugin line in the config.

#include "VstPlugin.h"

#include <cstdio>
#include <cstring>
#include <new>

#include "ControlChannel.h"
#include "Limiter.h"
#include "Parameters.h"

namespace {

using namespace loudify;

constexpr int32_t kVstVersion = 2400;
constexpr int32_t kVendorVersion = 1000;
constexpr int32_t kPlugCategoryEffect = 1;
constexpr int32_t kMaxParamStrLen = 8;
constexpr int32_t kMaxNameLen = 32;
constexpr int32_t kMaxVendorStrLen = 64;

enum Opcode : int32_t
{
    effOpen = 0,
    effClose = 1,
    effGetParamLabel = 6,
    effGetParamDisplay = 7,
    effGetParamName = 8,
    effSetSampleRate = 10,
    effSetBlockSize = 11,
    effMainsChanged = 12,
    effGetPlugCategory = 35,
    effGetEffectName = 45,
    effGetVendorString = 47,
    effGetProductString = 48,
    effGetVendorVersion = 49,
    effCanDo = 51,
    effGetVstVersion = 58,
};

void copyString(void* dest, const char* src, size_t maxLen)
{
    if (dest == nullptr)
        return;
    char* d = static_cast<char*>(dest);
    std::strncpy(d, src, maxLen - 1);
    d[maxLen - 1] = '\0';
}

class Plugin
{
public:
    explicit Plugin(VstHostCallback host)
        : host_(host)
    {
        std::memset(&effect_, 0, sizeof(effect_));
        effect_.magic = kVstMagic;
        effect_.dispatcher = &Plugin::dispatcher;
        effect_.process = &Plugin::processAccumulating;
        effect_.setParameter = &Plugin::setParameter;
        effect_.getParameter = &Plugin::getParameter;
        effect_.numPrograms = 0;
        effect_.numParams = kNumParams;
        effect_.numInputs = Limiter::kMaxChannels;
        effect_.numOutputs = Limiter::kMaxChannels;
        effect_.flags = kEffFlagsCanReplacing;
        // Lookahead latency is not reported: it is ~1.5 ms and reporting it would
        // make some hosts add the same delay again for compensation.
        effect_.initialDelay = 0;
        effect_.ioRatio = 1.0f;
        effect_.object = this;
        effect_.uniqueID = kVstUniqueId;
        effect_.version = kVendorVersion;
        effect_.processReplacing = &Plugin::processReplacing;
        effect_.processDoubleReplacing = nullptr;

        params_[kParamGain] = 0.0f;
        params_[kParamCeiling] = static_cast<float>(ceilingDbToNormalized(-1.0));
        params_[kParamRelease] = static_cast<float>(releaseMsToNormalized(150.0));
        for (int i = 0; i < kNumParams; i++)
            apply(i);
    }

    AEffect* effect() { return &effect_; }

private:
    static Plugin* self(AEffect* e) { return static_cast<Plugin*>(e->object); }

    static intptr_t VSTCALL dispatcher(AEffect* e, int32_t opcode, int32_t index, intptr_t value, void* ptr, float opt)
    {
        Plugin* p = self(e);
        switch (opcode)
        {
        case effOpen:
            return 0;
        case effClose:
            delete p;
            return 1;
        case effGetParamLabel:
            copyString(ptr, p->paramLabel(index), kMaxParamStrLen);
            return 0;
        case effGetParamDisplay:
            p->paramDisplay(index, ptr);
            return 0;
        case effGetParamName:
            copyString(ptr, paramName(index), kMaxParamStrLen);
            return 0;
        case effSetSampleRate:
            p->limiter_.setSampleRate(opt);
            return 0;
        case effSetBlockSize:
            return 0;
        case effMainsChanged:
            if (value != 0)
                p->limiter_.reset();
            return 0;
        case effGetPlugCategory:
            return kPlugCategoryEffect;
        case effGetEffectName:
            copyString(ptr, "Loudify Limiter", kMaxNameLen);
            return 1;
        case effGetVendorString:
            copyString(ptr, "Loudify", kMaxVendorStrLen);
            return 1;
        case effGetProductString:
            copyString(ptr, "Loudify Limiter", kMaxVendorStrLen);
            return 1;
        case effGetVendorVersion:
            return kVendorVersion;
        case effCanDo:
            return 0;
        case effGetVstVersion:
            return kVstVersion;
        default:
            return 0;
        }
    }

    static void VSTCALL setParameter(AEffect* e, int32_t index, float value)
    {
        Plugin* p = self(e);
        if (index < 0 || index >= kNumParams)
            return;
        p->params_[index] = static_cast<float>(clamp01(value));
        p->apply(index);
    }

    static float VSTCALL getParameter(AEffect* e, int32_t index)
    {
        Plugin* p = self(e);
        if (index < 0 || index >= kNumParams)
            return 0.0f;
        return p->params_[index];
    }

    static void VSTCALL processReplacing(AEffect* e, float** inputs, float** outputs, int32_t frames)
    {
        self(e)->updateDuck();
        self(e)->limiter_.process(inputs, outputs, Limiter::kMaxChannels, frames);
    }

    // Legacy VST 2 process(): adds the result to the output buffers.
    static void VSTCALL processAccumulating(AEffect* e, float** inputs, float** outputs, int32_t frames)
    {
        constexpr int kChunk = 256;
        self(e)->updateDuck();
        float temp[Limiter::kMaxChannels][kChunk];
        float* tempPtrs[Limiter::kMaxChannels] = { temp[0], temp[1] };
        for (int32_t offset = 0; offset < frames; offset += kChunk)
        {
            int n = frames - offset < kChunk ? frames - offset : kChunk;
            const float* in[Limiter::kMaxChannels] = { inputs[0] + offset, inputs[1] + offset };
            self(e)->limiter_.process(in, tempPtrs, Limiter::kMaxChannels, n);
            for (int c = 0; c < Limiter::kMaxChannels; c++)
                for (int i = 0; i < n; i++)
                    outputs[c][offset + i] += temp[c][i];
        }
    }

    static const char* paramName(int32_t index)
    {
        switch (index)
        {
        case kParamGain: return "Gain";
        case kParamCeiling: return "Ceiling";
        case kParamRelease: return "Release";
        default: return "";
        }
    }

    const char* paramLabel(int32_t index) const
    {
        return index == kParamRelease ? "ms" : "dB";
    }

    void paramDisplay(int32_t index, void* ptr) const
    {
        if (ptr == nullptr || index < 0 || index >= kNumParams)
            return;
        double v = params_[index];
        double shown = index == kParamGain ? gainDbFromNormalized(v)
            : index == kParamCeiling ? ceilingDbFromNormalized(v)
            : releaseMsFromNormalized(v);
        std::snprintf(static_cast<char*>(ptr), kMaxParamStrLen, "%.1f", shown);
    }

    // Once per audio block: picks up the attenuation written by Loudify.exe.
    void updateDuck()
    {
        double duck = control_.duckDb();
        if (duck != limiter_.duckDb())
            limiter_.setDuckDb(duck);
    }

    void apply(int index)
    {
        double v = params_[index];
        switch (index)
        {
        case kParamGain: limiter_.setGainDb(gainDbFromNormalized(v)); break;
        case kParamCeiling: limiter_.setCeilingDb(ceilingDbFromNormalized(v)); break;
        case kParamRelease: limiter_.setReleaseMs(releaseMsFromNormalized(v)); break;
        default: break;
        }
    }

    AEffect effect_;
    VstHostCallback host_;
    float params_[kNumParams] = {};
    Limiter limiter_;
    ControlChannel control_;
};

} // namespace

extern "C" LOUDIFY_EXPORT AEffect* VSTCALL VSTPluginMain(VstHostCallback host)
{
    if (host == nullptr)
        return nullptr;
    Plugin* plugin = new (std::nothrow) Plugin(host);
    return plugin != nullptr ? plugin->effect() : nullptr;
}
