#pragma once

#include <algorithm>
#include <cmath>

namespace loudify {

// Gain stage followed by a stereo-linked lookahead peak limiter.
//
// The gain is applied first, then the limiter guarantees that no output sample
// exceeds the ceiling. Gain reduction is computed as the minimum required gain
// over a lookahead window, smoothed by a release filter and a box filter of the
// same length as the window. Because every value averaged by the box filter is
// already <= the gain required by a peak, the gain reaches that value exactly
// when the peak leaves the delay line, so peaks are caught without hard clipping.
class Limiter
{
public:
    static constexpr int kMaxChannels = 2;
    static constexpr int kMaxWindow = 2048;
    static constexpr double kLookaheadMs = 1.5;
    static constexpr double kGainSmoothingMs = 20.0;

    Limiter() { setSampleRate(48000.0); }

    void setSampleRate(double sampleRate)
    {
        if (!(sampleRate > 0.0))
            sampleRate = 48000.0;
        sampleRate_ = sampleRate;
        window_ = std::clamp(static_cast<int>(std::lround(kLookaheadMs * sampleRate / 1000.0)), 1, kMaxWindow);
        gainCoef_ = 1.0 - std::exp(-1.0 / (kGainSmoothingMs / 1000.0 * sampleRate));
        updateReleaseCoef();
        reset();
    }

    void setGainDb(double db) { targetGain_ = dbToLinear(db); }
    void setCeilingDb(double db) { ceiling_ = dbToLinear(std::min(db, 0.0)); }
    void setReleaseMs(double ms)
    {
        releaseMs_ = std::max(ms, 1.0);
        updateReleaseCoef();
    }

    // Clears the delay line and the gain-reduction state. The input gain restarts
    // at unity and ramps to the target, so enabling the boost never pops.
    void reset()
    {
        for (int c = 0; c < kMaxChannels; c++)
            std::fill(delay_[c], delay_[c] + kMaxWindow, 0.0f);
        std::fill(required_, required_ + kMaxWindow, 1.0);
        std::fill(smoothed_, smoothed_ + kMaxWindow, 1.0);
        boxSum_ = window_;
        release_ = 1.0;
        currentGain_ = 1.0;
        pos_ = 0;
    }

    int latencySamples() const { return window_ - 1; }
    double sampleRate() const { return sampleRate_; }

    // in and out may point to the same buffers (in-place processing).
    void process(const float* const* in, float* const* out, int channels, int frames)
    {
        channels = std::clamp(channels, 0, kMaxChannels);
        for (int i = 0; i < frames; i++)
        {
            currentGain_ += (targetGain_ - currentGain_) * gainCoef_;

            double peak = 0.0;
            for (int c = 0; c < channels; c++)
            {
                double x = in[c][i];
                if (!std::isfinite(x))
                    x = 0.0;
                x *= currentGain_;
                delay_[c][pos_] = static_cast<float>(x);
                peak = std::max(peak, std::fabs(x));
            }

            required_[pos_] = peak > ceiling_ ? ceiling_ / peak : 1.0;

            double windowMin = 1.0;
            for (int k = 0; k < window_; k++)
                windowMin = std::min(windowMin, required_[k]);

            // Instant attack, exponential release. Never rises above windowMin.
            if (windowMin < release_)
                release_ = windowMin;
            else
                release_ += (windowMin - release_) * releaseCoefInv_;

            boxSum_ += release_ - smoothed_[pos_];
            smoothed_[pos_] = release_;

            int readPos = pos_ + 1 == window_ ? 0 : pos_ + 1;
            if (readPos == 0)
            {
                // Recompute periodically so floating-point drift cannot accumulate.
                boxSum_ = 0.0;
                for (int k = 0; k < window_; k++)
                    boxSum_ += smoothed_[k];
            }
            double gain = boxSum_ / window_;

            for (int c = 0; c < channels; c++)
            {
                double y = delay_[c][readPos] * gain;
                out[c][i] = static_cast<float>(std::clamp(y, -ceiling_, ceiling_));
            }
            pos_ = readPos;
        }
    }

    static double dbToLinear(double db) { return std::pow(10.0, db / 20.0); }

private:
    void updateReleaseCoef()
    {
        releaseCoefInv_ = 1.0 - std::exp(-1.0 / (releaseMs_ / 1000.0 * sampleRate_));
    }

    double sampleRate_ = 48000.0;
    int window_ = 1;
    double targetGain_ = 1.0;
    double currentGain_ = 1.0;
    double gainCoef_ = 1.0;
    double ceiling_ = dbToLinear(-1.0);
    double releaseMs_ = 150.0;
    double releaseCoefInv_ = 1.0;
    double release_ = 1.0;
    double boxSum_ = 1.0;
    int pos_ = 0;

    float delay_[kMaxChannels][kMaxWindow] = {};
    double required_[kMaxWindow] = {};
    double smoothed_[kMaxWindow] = {};
};

} // namespace loudify
