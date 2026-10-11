// Tests for the limiter DSP and the VST 2 entry point. Runs on any platform:
// on Linux the plugin source is compiled straight into this executable.

#include <cmath>
#include <cstdio>
#include <cstring>
#include <random>
#include <string>
#include <vector>

#include "../src/Limiter.h"
#include "../src/Parameters.h"
#include "../src/VstPlugin.h"

using namespace loudify;

static int failures = 0;
static constexpr double kPi = 3.14159265358979323846;

#define CHECK(cond)                                                                    \
    do                                                                                 \
    {                                                                                  \
        if (!(cond))                                                                   \
        {                                                                              \
            std::printf("  FAILED %s:%d: %s\n", __FILE__, __LINE__, #cond);            \
            failures++;                                                                \
        }                                                                              \
    } while (0)

struct Stereo
{
    std::vector<float> l, r;
    explicit Stereo(size_t n) : l(n), r(n) {}
    size_t size() const { return l.size(); }
};

static Stereo run(Limiter& lim, const Stereo& input, int block = 480)
{
    Stereo out(input.size());
    for (size_t offset = 0; offset < input.size(); offset += block)
    {
        int n = static_cast<int>(std::min<size_t>(block, input.size() - offset));
        const float* in[2] = { input.l.data() + offset, input.r.data() + offset };
        float* o[2] = { out.l.data() + offset, out.r.data() + offset };
        lim.process(in, o, 2, n);
    }
    return out;
}

static double peak(const Stereo& s, size_t from = 0)
{
    double p = 0.0;
    for (size_t i = from; i < s.size(); i++)
        p = std::max({ p, (double)std::fabs(s.l[i]), (double)std::fabs(s.r[i]) });
    return p;
}

static double rms(const Stereo& s, size_t from, size_t to)
{
    double sum = 0.0;
    for (size_t i = from; i < to; i++)
        sum += s.l[i] * (double)s.l[i] + s.r[i] * (double)s.r[i];
    return std::sqrt(sum / (2.0 * (to - from)));
}

static Stereo sine(double sr, double freq, double amplitude, size_t n)
{
    Stereo s(n);
    for (size_t i = 0; i < n; i++)
    {
        float v = static_cast<float>(amplitude * std::sin(2.0 * kPi * freq * i / sr));
        s.l[i] = v;
        s.r[i] = v;
    }
    return s;
}

static void testSilence()
{
    std::printf("silence stays silent\n");
    Limiter lim;
    lim.setGainDb(18.0);
    Stereo out = run(lim, Stereo(48000));
    CHECK(peak(out) == 0.0);
}

static void testTransparentBelowCeiling()
{
    std::printf("unity gain below the ceiling is a pure delay\n");
    Limiter lim;
    lim.setSampleRate(48000.0);
    lim.setGainDb(0.0);
    Stereo in = sine(48000.0, 1000.0, 0.1, 48000);
    Stereo out = run(lim, in);
    int lat = lim.latencySamples();
    CHECK(lat == 71);
    double maxErr = 0.0;
    for (size_t i = lat; i < in.size(); i++)
        maxErr = std::max(maxErr, (double)std::fabs(out.l[i] - in.l[i - lat]));
    CHECK(maxErr < 1e-6);
}

static void testNoOversWithBoost()
{
    std::printf("boosted signals never exceed the ceiling\n");
    const double rates[] = { 44100.0, 48000.0, 96000.0, 192000.0 };
    for (double sr : rates)
    {
        for (double gainDb : { 6.0, 12.0, 24.0, 36.0 })
        {
            Limiter lim;
            lim.setSampleRate(sr);
            lim.setGainDb(gainDb);
            lim.setCeilingDb(-1.0);

            std::mt19937 rng(42);
            std::normal_distribution<float> noise(0.0f, 0.3f);
            size_t n = static_cast<size_t>(sr * 2);
            Stereo in(n);
            for (size_t i = 0; i < n; i++)
            {
                // Noise, a silent gap, then sudden full-scale bursts.
                bool burst = (i / 1000) % 7 == 3;
                bool gap = (i / 1000) % 7 == 2;
                in.l[i] = gap ? 0.0f : burst ? 1.0f : noise(rng);
                in.r[i] = gap ? 0.0f : burst ? -1.0f : noise(rng) * 0.5f;
            }
            Stereo out = run(lim, in, 441);
            CHECK(peak(out) <= Limiter::dbToLinear(-1.0) + 1e-6);
        }
    }
}

static void testLouder()
{
    std::printf("boost makes quiet audio louder\n");
    Limiter lim;
    lim.setGainDb(12.0);
    Stereo in = sine(48000.0, 440.0, 0.05, 48000);
    Stereo out = run(lim, in);
    double gain = rms(out, 24000, 48000) / rms(in, 24000, 48000);
    CHECK(std::fabs(20.0 * std::log10(gain) - 12.0) < 0.1);
}

static void testLimitsLoudSine()
{
    std::printf("loud sine is limited close to the ceiling without hard clipping\n");
    Limiter lim;
    lim.setGainDb(12.0);
    lim.setCeilingDb(-1.0);
    Stereo out = run(lim, sine(48000.0, 100.0, 0.9, 96000));
    double ceiling = Limiter::dbToLinear(-1.0);
    double p = peak(out, 48000);
    CHECK(p <= ceiling + 1e-6);
    CHECK(p > ceiling * 0.9);
}

static void testReleaseRecovers()
{
    std::printf("gain recovers after a loud burst\n");
    Limiter lim;
    lim.setGainDb(6.0);
    lim.setReleaseMs(100.0);
    Stereo in = sine(48000.0, 440.0, 0.05, 96000);
    for (size_t i = 4800; i < 9600; i++)
        in.l[i] = in.r[i] = 1.0f;
    Stereo out = run(lim, in);
    double gain = rms(out, 72000, 96000) / rms(in, 72000, 96000);
    CHECK(std::fabs(20.0 * std::log10(gain) - 6.0) < 0.1);
}

static void testNonFiniteInput()
{
    std::printf("NaN and infinity do not poison the output\n");
    Limiter lim;
    lim.setGainDb(12.0);
    Stereo in = sine(48000.0, 440.0, 0.5, 4800);
    in.l[100] = NAN;
    in.r[200] = INFINITY;
    Stereo out = run(lim, in);
    bool finite = true;
    for (size_t i = 0; i < out.size(); i++)
        finite = finite && std::isfinite(out.l[i]) && std::isfinite(out.r[i]);
    CHECK(finite);
    CHECK(peak(out, 1000) > 0.1);
}

static void testInPlace()
{
    std::printf("in-place processing matches separate buffers\n");
    Stereo in = sine(48000.0, 440.0, 0.8, 9600);
    Limiter a, b;
    a.setGainDb(10.0);
    b.setGainDb(10.0);
    Stereo expected = run(a, in);
    Stereo buf = in;
    for (size_t offset = 0; offset < buf.size(); offset += 480)
    {
        float* io[2] = { buf.l.data() + offset, buf.r.data() + offset };
        b.process(io, io, 2, 480);
    }
    CHECK(std::memcmp(buf.l.data(), expected.l.data(), buf.size() * sizeof(float)) == 0);
}

static void testParameterMapping()
{
    std::printf("parameter mappings round-trip\n");
    CHECK(std::fabs(gainDbFromNormalized(gainDbToNormalized(9.0)) - 9.0) < 1e-9);
    CHECK(std::fabs(ceilingDbFromNormalized(ceilingDbToNormalized(-1.0)) + 1.0) < 1e-9);
    CHECK(std::fabs(releaseMsFromNormalized(releaseMsToNormalized(150.0)) - 150.0) < 1e-9);
    CHECK(gainDbFromNormalized(2.0) == kMaxGainDb);
    CHECK(ceilingDbFromNormalized(1.0) == 0.0);
}

static intptr_t VSTCALL fakeHost(AEffect*, int32_t opcode, int32_t, intptr_t, void*, float)
{
    return opcode == 1 ? 2400 : 0;
}

static void testVstEntryPoint()
{
    std::printf("VST entry point behaves like Equalizer APO expects\n");
    AEffect* e = VSTPluginMain(fakeHost);
    CHECK(e != nullptr);
    if (e == nullptr)
        return;
    CHECK(e->magic == kVstMagic);
    CHECK(e->numInputs == 2 && e->numOutputs == 2);
    CHECK((e->flags & kEffFlagsCanReplacing) != 0);
    CHECK((e->flags & (1 << 5)) == 0); // no chunks: parameters are set by name
    CHECK(e->numParams == 3);

    // Equalizer APO matches parameter names from the config file.
    std::string names[3];
    for (int i = 0; i < e->numParams; i++)
    {
        char buf[256];
        std::memset(buf, 'x', sizeof(buf));
        e->dispatcher(e, 8, i, 0, buf, 0.0f);
        names[i] = buf;
    }
    CHECK(names[0] == "Gain");
    CHECK(names[1] == "Ceiling");
    CHECK(names[2] == "Release");

    e->dispatcher(e, 10, 0, 0, nullptr, 48000.0f);
    e->dispatcher(e, 11, 0, 480, nullptr, 0.0f);
    e->setParameter(e, 0, static_cast<float>(gainDbToNormalized(12.0)));
    e->setParameter(e, 1, static_cast<float>(ceilingDbToNormalized(-1.0)));
    CHECK(std::fabs(e->getParameter(e, 0) - static_cast<float>(gainDbToNormalized(12.0))) < 1e-6);
    e->dispatcher(e, 12, 0, 1, nullptr, 0.0f);

    char display[256] = {};
    e->dispatcher(e, 7, 0, 0, display, 0.0f);
    CHECK(std::string(display) == "12.0");

    Stereo in = sine(48000.0, 440.0, 0.9, 48000);
    Stereo out(in.size());
    for (size_t offset = 0; offset < in.size(); offset += 480)
    {
        float* i2[2] = { in.l.data() + offset, in.r.data() + offset };
        float* o2[2] = { out.l.data() + offset, out.r.data() + offset };
        e->processReplacing(e, i2, o2, 480);
    }
    CHECK(peak(out) <= Limiter::dbToLinear(-1.0) + 1e-6);
    CHECK(peak(out, 24000) > 0.8);

    // Legacy accumulating process adds to the output.
    std::vector<float> l(1000, 0.25f), r(1000, 0.25f), zl(1000, 0.0f), zr(1000, 0.0f);
    float* acc[2] = { l.data(), r.data() };
    float* zero[2] = { zl.data(), zr.data() };
    e->process(e, zero, acc, 1000);
    CHECK(l[999] == 0.25f);

    CHECK(e->dispatcher(e, 1, 0, 0, nullptr, 0.0f) == 1);
}

int main()
{
    testSilence();
    testTransparentBelowCeiling();
    testNoOversWithBoost();
    testLouder();
    testLimitsLoudSine();
    testReleaseRecovers();
    testNonFiniteInput();
    testInPlace();
    testParameterMapping();
    testVstEntryPoint();

    if (failures > 0)
    {
        std::printf("%d check(s) failed\n", failures);
        return 1;
    }
    std::printf("all tests passed\n");
    return 0;
}
