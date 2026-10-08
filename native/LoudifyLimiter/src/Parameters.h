#pragma once

#include <algorithm>

namespace loudify {

// VST 2 parameters are normalized to 0..1. These mappings must stay in sync with
// LimiterParameters in src/Loudify.Core/LimiterParameters.cs, which writes the
// normalized values into the Equalizer APO configuration.
enum ParameterIndex
{
    kParamGain = 0,
    kParamCeiling,
    kParamRelease,
    kNumParams
};

constexpr double kMaxGainDb = 24.0;
constexpr double kMinCeilingDb = -6.0;
constexpr double kMinReleaseMs = 10.0;
constexpr double kMaxReleaseMs = 1000.0;

inline double clamp01(double v) { return std::clamp(v, 0.0, 1.0); }

inline double gainDbFromNormalized(double v) { return clamp01(v) * kMaxGainDb; }
inline double ceilingDbFromNormalized(double v) { return kMinCeilingDb * (1.0 - clamp01(v)); }
inline double releaseMsFromNormalized(double v) { return kMinReleaseMs + clamp01(v) * (kMaxReleaseMs - kMinReleaseMs); }

inline double gainDbToNormalized(double db) { return clamp01(db / kMaxGainDb); }
inline double ceilingDbToNormalized(double db) { return clamp01(1.0 - db / kMinCeilingDb); }
inline double releaseMsToNormalized(double ms) { return clamp01((ms - kMinReleaseMs) / (kMaxReleaseMs - kMinReleaseMs)); }

} // namespace loudify
