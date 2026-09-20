// U01 QA: capture only an explicitly identified NeuroSaki fixture process tree.
// Uses Microsoft's documented process-loopback activation, never endpoint/system loopback.
#define NOMINMAX
#include <windows.h>
#include <audioclient.h>
#include <audioclientactivationparams.h>
#include <mmdeviceapi.h>
#include <wrl.h>
#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdint>
#include <filesystem>
#include <iomanip>
#include <iostream>
#include <limits>
#include <sstream>
#include <stdexcept>
#include <string>
#include <vector>

using Microsoft::WRL::ComPtr;
namespace fs = std::filesystem;
static std::atomic<bool> interrupted{ false };

struct Handle {
    HANDLE value = nullptr;
    explicit Handle(HANDLE v = nullptr) : value(v) {}
    ~Handle() { if (value && value != INVALID_HANDLE_VALUE) CloseHandle(value); }
    Handle(const Handle&) = delete;
    Handle& operator=(const Handle&) = delete;
};
static void Check(HRESULT result, const char* operation) {
    if (FAILED(result)) {
        std::ostringstream error;
        error << operation << " failed, HRESULT=0x" << std::hex << static_cast<unsigned long>(result);
        throw std::runtime_error(error.str());
    }
}
static void Require(bool value, const char* message) { if (!value) throw std::runtime_error(message); }
static int64_t Qpc() { LARGE_INTEGER value; QueryPerformanceCounter(&value); return value.QuadPart; }
static int64_t Frequency() { LARGE_INTEGER value; QueryPerformanceFrequency(&value); return value.QuadPart; }
static DWORD HostBuild() {
    typedef LONG(WINAPI* VersionFunction)(OSVERSIONINFOW*);
    auto function = reinterpret_cast<VersionFunction>(GetProcAddress(GetModuleHandleW(L"ntdll.dll"), "RtlGetVersion"));
    OSVERSIONINFOW info{}; info.dwOSVersionInfoSize = sizeof(info);
    Require(function && function(&info) == 0, "Cannot verify Windows build; process capture is disabled.");
    return info.dwBuildNumber;
}
static BOOL WINAPI ConsoleSignal(DWORD event) {
    if (event == CTRL_C_EVENT || event == CTRL_BREAK_EVENT || event == CTRL_CLOSE_EVENT) {
        interrupted.store(true); return TRUE;
    }
    return FALSE;
}
static unsigned long StrictNumber(const std::wstring& text, unsigned long maximum) {
    Require(!text.empty() && text.size() <= 10, "Invalid numeric argument.");
    uint64_t value = 0;
    for (wchar_t character : text) {
        Require(character >= L'0' && character <= L'9', "Numeric arguments accept decimal digits only.");
        value = value * 10 + character - L'0';
        Require(value <= maximum, "Numeric argument exceeds its limit.");
    }
    Require(value > 0, "Numeric argument must be positive.");
    return static_cast<unsigned long>(value);
}
struct Options {
    DWORD pid = 0;
    unsigned seconds = 0;
    fs::path output;
    fs::path expectedExecutable;
    bool fixtureOnly = false;
};
static Options Parse(int argc, wchar_t** argv) {
    Options options;
    for (int index = 1; index < argc; ++index) {
        std::wstring key = argv[index];
        if (key == L"--fixture-only") { Require(!options.fixtureOnly, "Duplicate fixture flag."); options.fixtureOnly = true; continue; }
        Require(index + 1 < argc, "Missing argument value.");
        std::wstring value = argv[++index];
        if (key == L"--pid") { Require(options.pid == 0, "Duplicate PID."); options.pid = StrictNumber(value, MAXDWORD); }
        else if (key == L"--seconds") { Require(options.seconds == 0, "Duplicate duration."); options.seconds = StrictNumber(value, 120); }
        else if (key == L"--output") { Require(options.output.empty(), "Duplicate output."); options.output = value; }
        else if (key == L"--expected-exe") { Require(options.expectedExecutable.empty(), "Duplicate executable."); options.expectedExecutable = value; }
        else throw std::runtime_error("Unknown argument. No system-loopback or microphone mode exists.");
    }
    Require(options.fixtureOnly && options.pid && options.seconds && !options.output.empty() && !options.expectedExecutable.empty(),
        "Explicit PID, duration, output, expected executable and --fixture-only are required.");
    Require(options.output.is_absolute() && options.expectedExecutable.is_absolute(), "Use absolute paths.");
    Require(_wcsicmp(options.expectedExecutable.filename().c_str(), L"NeuroSaki.exe") == 0, "Only the explicit NeuroSaki.exe fixture player is permitted.");
    Require(_wcsicmp(options.output.extension().c_str(), L".wav") == 0, "Output must be a WAV file.");
    return options;
}

// WRL FtmBase supplies the required free-threaded marshaler for the activation callback.
class Activation final : public Microsoft::WRL::RuntimeClass<Microsoft::WRL::RuntimeClassFlags<Microsoft::WRL::ClassicCom>,
    IActivateAudioInterfaceCompletionHandler, Microsoft::WRL::FtmBase> {
public:
    Handle ready{ CreateEventW(nullptr, TRUE, FALSE, nullptr) };
    HRESULT result = E_PENDING;
    ComPtr<IAudioClient> client;
    STDMETHOD(ActivateCompleted)(IActivateAudioInterfaceAsyncOperation* operation) override {
        HRESULT activated = E_FAIL;
        ComPtr<IUnknown> unknown;
        result = operation->GetActivateResult(&activated, &unknown);
        if (SUCCEEDED(result)) result = activated;
        if (SUCCEEDED(result)) result = unknown.As(&client);
        SetEvent(ready.value);
        return S_OK;
    }
};

#pragma pack(push, 1)
struct WavHeader {
    char riff[4] = { 'R','I','F','F' };
    uint32_t riffBytes = 50;
    char wave[4] = { 'W','A','V','E' };
    char format[4] = { 'f','m','t',' ' };
    uint32_t formatBytes = 18;
    uint16_t codec = 3;
    uint16_t channels = 2;
    uint32_t sampleRate = 48000;
    uint32_t bytesPerSecond = 384000;
    uint16_t alignment = 8;
    uint16_t bits = 32;
    uint16_t extraFormatBytes = 0;
    char fact[4] = { 'f','a','c','t' };
    uint32_t factBytes = 4;
    uint32_t factSamples = 0;
    char data[4] = { 'd','a','t','a' };
    uint32_t dataBytes = 0;
};
#pragma pack(pop)
static_assert(sizeof(WavHeader) == 58, "Float WAV includes an 18-byte format and a fact chunk.");

class NewFile {
    Handle file;
public:
    explicit NewFile(const fs::path& path) : file(CreateFileW(path.c_str(), GENERIC_WRITE, FILE_SHARE_READ, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr)) {
        Require(file.value != INVALID_HANDLE_VALUE, "Cannot create a new evidence file; existing files are never overwritten.");
    }
    void Write(const void* data, DWORD length) {
        DWORD written = 0;
        Require(WriteFile(file.value, data, length, &written, nullptr) && written == length, "Evidence write failed.");
    }
    void Text(const std::string& text) { Write(text.data(), static_cast<DWORD>(text.size())); }
    void Rewind() { LARGE_INTEGER zero{}; Require(SetFilePointerEx(file.value, zero, nullptr, FILE_BEGIN) != FALSE, "Cannot finalize WAV header."); }
    void Flush() { Require(FlushFileBuffers(file.value) != FALSE, "Cannot flush evidence."); }
};
struct PacketStats {
    double rms = 0;
    double peak = 0;
    int64_t firstNonzeroFrame = -1;
    int64_t lastNonzeroFrame = -1;
};
static PacketStats Measure(const float* samples, UINT32 frames) {
    PacketStats result;
    long double sum = 0;
    for (uint64_t index = 0; index < static_cast<uint64_t>(frames) * 2; ++index) {
        double value = samples[index];
        Require(std::isfinite(value), "Capture returned invalid floating-point audio.");
        result.peak = std::max(result.peak, std::abs(value));
        sum += static_cast<long double>(value) * value;
        if (value != 0) {
            if (result.firstNonzeroFrame < 0) result.firstNonzeroFrame = static_cast<int64_t>(index / 2);
            result.lastNonzeroFrame = static_cast<int64_t>(index / 2);
        }
    }
    if (frames) result.rms = std::sqrt(static_cast<double>(sum / (frames * 2)));
    return result;
}
static void SelfTest() {
    Require(StrictNumber(L"120", 120) == 120, "Number parsing failed.");
    for (const auto* text : { L"0", L"-1", L"12x", L"121", L"99999999999" }) {
        bool rejected = false; try { StrictNumber(text, 120); } catch (...) { rejected = true; }
        Require(rejected, "Unsafe numeric argument accepted.");
    }
    float samples[] = { 0, 0, .5f, -.5f, 0, 0, -1, 0 };
    auto measured = Measure(samples, 4);
    Require(measured.firstNonzeroFrame == 1 && measured.lastNonzeroFrame == 3 && measured.peak == 1 && measured.rms > 0, "Packet statistics failed.");
    std::cout << "PASS: argument bounds, float WAV header and packet statistics. No audio API activated. Windows build " << HostBuild() << "\n";
}

static int Capture(const Options& options) {
    Require(HostBuild() >= 20348, "Process loopback needs Windows build 20348 or later. No fallback is permitted.");
    Handle process(OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION | SYNCHRONIZE, FALSE, options.pid));
    Require(process.value != nullptr, "Cannot open explicitly specified target PID.");
    wchar_t path[32768]; DWORD length = static_cast<DWORD>(std::size(path));
    Require(QueryFullProcessImageNameW(process.value, 0, path, &length) != FALSE, "Cannot verify target image path.");
    auto actual = fs::weakly_canonical(fs::path(path));
    auto expected = fs::weakly_canonical(options.expectedExecutable);
    Require(_wcsicmp(actual.c_str(), expected.c_str()) == 0, "Target PID does not match the explicitly approved player executable.");
    Require(WaitForSingleObject(process.value, 0) == WAIT_TIMEOUT, "Target player has already exited.");
    Require(fs::exists(options.output.parent_path()), "Create the output directory before capture.");
    fs::path packetsPath = options.output; packetsPath += L".packets.csv";
    fs::path summaryPath = options.output; summaryPath += L".json";
    Require(!fs::exists(options.output) && !fs::exists(packetsPath) && !fs::exists(summaryPath), "Choose new evidence filenames.");

    Check(CoInitializeEx(nullptr, COINIT_MULTITHREADED), "COM initialization");
    struct ComScope { ~ComScope() { CoUninitialize(); } } comScope;
    auto activation = Microsoft::WRL::Make<Activation>();
    Require(activation && activation->ready.value, "Cannot create the activation completion event.");
    AUDIOCLIENT_ACTIVATION_PARAMS params{};
    params.ActivationType = AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK;
    params.ProcessLoopbackParams.TargetProcessId = options.pid;
    params.ProcessLoopbackParams.ProcessLoopbackMode = PROCESS_LOOPBACK_MODE_INCLUDE_TARGET_PROCESS_TREE;
    PROPVARIANT variant{};
    variant.vt = VT_BLOB;
    variant.blob.cbSize = sizeof(params);
    variant.blob.pBlobData = reinterpret_cast<BYTE*>(&params);
    ComPtr<IActivateAudioInterfaceAsyncOperation> operation;
    Check(ActivateAudioInterfaceAsync(VIRTUAL_AUDIO_DEVICE_PROCESS_LOOPBACK, __uuidof(IAudioClient), &variant, activation.Get(), &operation), "Process-only activation");
    Require(WaitForSingleObject(activation->ready.value, 10000) == WAIT_OBJECT_0, "Process-only activation timed out. No fallback is permitted.");
    Check(activation->result, "Process-only activation result");
    auto client = activation->client;
    WAVEFORMATEX format{};
    // Keep the engine's floating-point precision instead of creating PCM16 dither at silence.
    format.wFormatTag = WAVE_FORMAT_IEEE_FLOAT; format.nChannels = 2; format.nSamplesPerSec = 48000;
    format.wBitsPerSample = 32; format.nBlockAlign = 8; format.nAvgBytesPerSec = 384000;
    Check(client->Initialize(AUDCLNT_SHAREMODE_SHARED, AUDCLNT_STREAMFLAGS_LOOPBACK | AUDCLNT_STREAMFLAGS_EVENTCALLBACK | AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM,
        0, 0, &format, nullptr), "Process capture initialization");
    UINT32 capacity = 0;
    Check(client->GetBufferSize(&capacity), "Capture buffer capacity");
    Require(capacity > 0 && capacity <= 48000 * 2, "Unexpected capture buffer capacity.");
    ComPtr<IAudioCaptureClient> capture;
    Check(client->GetService(IID_PPV_ARGS(&capture)), "Capture service");
    Handle samplesReady(CreateEventW(nullptr, FALSE, FALSE, nullptr));
    Require(samplesReady.value != nullptr, "Cannot create the sample event.");
    Check(client->SetEventHandle(samplesReady.value), "Capture event registration");
    std::vector<float> copied(static_cast<size_t>(capacity) * 2);
    NewFile wav(options.output), csv(packetsPath);
    WavHeader header;
    wav.Write(&header, sizeof(header));
    csv.Text("packet,device_frame,audio_qpc_100ns,observed_qpc_ticks,frames,flags,rms,peak,first_nonzero_frame,last_nonzero_frame\n");
    int64_t frequency = Frequency(), beforeStart = Qpc();
    Check(client->Start(), "Capture start");
    struct StopScope { IAudioClient* client; ~StopScope() { if (client) client->Stop(); } } stop{ client.Get() };
    int64_t started = Qpc(), deadline = started + static_cast<int64_t>(options.seconds) * frequency;
    std::wcout << L"PROCESS-ONLY capture active for approved NeuroSaki PID " << options.pid << L"; maximum " << options.seconds << L" seconds.\n" << std::flush;
    uint64_t packets = 0, framesTotal = 0, nonzeroPackets = 0, discontinuities = 0, timestampErrors = 0;
    std::string reason = "duration_elapsed";
    SetConsoleCtrlHandler(ConsoleSignal, TRUE);
    while (Qpc() < deadline && !interrupted.load()) {
        HANDLE waits[] = { samplesReady.value, process.value };
        DWORD waited = WaitForMultipleObjects(2, waits, FALSE, 100);
        if (waited == WAIT_OBJECT_0 + 1) { reason = "target_exited"; break; }
        if (waited == WAIT_TIMEOUT) continue;
        Require(waited == WAIT_OBJECT_0, "Capture wait failed.");
        UINT32 available = 0;
        Check(capture->GetNextPacketSize(&available), "Packet availability");
        while (available && Qpc() < deadline) {
            BYTE* data = nullptr; UINT32 frames = 0; DWORD flags = 0; UINT64 position = 0, timestamp = 0;
            HRESULT got = capture->GetBuffer(&data, &frames, &flags, &position, &timestamp);
            Check(got, "Capture packet");
            if (got == AUDCLNT_S_BUFFER_EMPTY) break;
            int64_t observed = Qpc();
            if (frames > capacity) { capture->ReleaseBuffer(0); throw std::runtime_error("Capture packet exceeded its declared buffer."); }
            if (flags & AUDCLNT_BUFFERFLAGS_SILENT) std::fill_n(copied.data(), static_cast<size_t>(frames) * 2, 0.f);
            else if (data != nullptr) std::copy_n(reinterpret_cast<const float*>(data), static_cast<size_t>(frames) * 2, copied.data());
            else { capture->ReleaseBuffer(0); throw std::runtime_error("Capture returned missing nonsilent PCM."); }
            Check(capture->ReleaseBuffer(frames), "Release capture packet");
            if (WaitForSingleObject(process.value, 0) != WAIT_TIMEOUT) { reason = "target_exited"; break; }
            auto stats = Measure(copied.data(), frames);
            wav.Write(copied.data(), frames * format.nBlockAlign);
            framesTotal += frames; ++packets;
            if (stats.peak) ++nonzeroPackets;
            if (flags & AUDCLNT_BUFFERFLAGS_DATA_DISCONTINUITY) ++discontinuities;
            if (flags & AUDCLNT_BUFFERFLAGS_TIMESTAMP_ERROR) ++timestampErrors;
            std::ostringstream row;
            row << packets << ',' << position << ',' << timestamp << ',' << observed << ',' << frames << ',' << flags << ','
                << std::setprecision(12) << stats.rms << ',' << stats.peak << ',' << stats.firstNonzeroFrame << ',' << stats.lastNonzeroFrame << '\n';
            csv.Text(row.str());
            Check(capture->GetNextPacketSize(&available), "Packet availability");
        }
        if (reason == "target_exited") break;
    }
    if (interrupted.load()) reason = "operator_interrupted";
    Check(client->Stop(), "Capture stop"); stop.client = nullptr;
    int64_t stopped = Qpc();
    header.dataBytes = static_cast<uint32_t>(framesTotal * format.nBlockAlign);
    header.factSamples = static_cast<uint32_t>(framesTotal);
    header.riffBytes = header.dataBytes + static_cast<uint32_t>(sizeof(WavHeader)) - 8;
    wav.Rewind(); wav.Write(&header, sizeof(header)); wav.Flush(); csv.Flush();
    NewFile summary(summaryPath);
    std::ostringstream json;
    json << "{\"capture_mode\":\"include_target_process_tree_only\",\"target_pid\":" << options.pid
        << ",\"expected_image_verified\":true,\"fixture_only_acknowledged\":true,\"sample_rate\":48000,\"channels\":2,\"bits\":32,\"codec\":\"ieee_float\""
        << ",\"requested_seconds\":" << options.seconds << ",\"qpc_frequency\":" << frequency << ",\"before_start_qpc\":" << beforeStart
        << ",\"started_qpc\":" << started << ",\"stopped_qpc\":" << stopped << ",\"frames\":" << framesTotal << ",\"packets\":" << packets
        << ",\"nonzero_packets\":" << nonzeroPackets << ",\"discontinuities\":" << discontinuities << ",\"timestamp_errors\":" << timestampErrors
        << ",\"reason\":\"" << reason << "\",\"system_loopback_used\":false,\"microphone_used\":false}\n";
    summary.Text(json.str()); summary.Flush();
    SetConsoleCtrlHandler(ConsoleSignal, FALSE);
    std::cout << "Capture finished: packets=" << packets << ", nonzero_packets=" << nonzeroPackets << ", reason=" << reason << "\n";
    return packets == 0 ? 3 : 0;
}

int wmain(int argc, wchar_t** argv) {
    try {
        if (argc == 2 && std::wstring(argv[1]) == L"--self-test") { SelfTest(); return 0; }
        if (argc == 1 || (argc == 2 && std::wstring(argv[1]) == L"--help")) {
            std::wcout << L"ProcessLoopback --pid PID --seconds 1..120 --output ABSOLUTE.wav --expected-exe ABSOLUTE\\NeuroSaki.exe --fixture-only\n"
                L"Only the named process tree is included. There is no system audio or microphone fallback.\n"
                L"Do not run capture before the integration owner approves the exact fixture Player PID and time window.\n"
                L"--self-test performs no audio activation or recording.\n";
            return 0;
        }
        return Capture(Parse(argc, argv));
    } catch (const std::exception& error) {
        std::cerr << "PROCESS_CAPTURE_FAILED: " << error.what() << "\n";
        return 1;
    }
}
