// Offline evidence encoder only: no window, screen, endpoint or microphone capture APIs.
// Input images and PCM already exist; native QPC-derived 100ns timestamps are preserved.
#define NOMINMAX
#include <windows.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mfreadwrite.h>
#include <mferror.h>
#include <wincodec.h>
#include <wrl.h>
#include <algorithm>
#include <cstdint>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <sstream>
#include <stdexcept>
#include <string>
#include <vector>
using Microsoft::WRL::ComPtr;
namespace fs = std::filesystem;
static void Need(bool b, const char* message) { if (!b) throw std::runtime_error(message); }
static void Check(HRESULT hr, const char* action) {
    if (FAILED(hr)) { std::ostringstream s; s << action << " HRESULT=0x" << std::hex << static_cast<unsigned long>(hr); throw std::runtime_error(s.str()); }
}
static int64_t Number(const std::wstring& s, int64_t max) {
    Need(!s.empty() && s.size() <= 19, "Invalid integer."); int64_t n = 0;
    for (wchar_t c : s) { Need(c >= L'0' && c <= L'9' && n <= (max - (c - L'0')) / 10, "Integer out of bounds."); n = n * 10 + c - L'0'; }
    return n;
}
struct Row { int64_t time = 0, duration = 0; fs::path image; uint64_t offset = 0, length = 0; };
static std::vector<Row> ReadRows(const fs::path& path, bool video) {
    std::wifstream f(path); Need(f.good(), "Cannot open timeline."); std::vector<Row> rows; std::wstring line;
    while (std::getline(f, line)) {
        if (!line.empty() && line.back() == L'\r') line.pop_back();
        std::wistringstream fields(line); std::wstring time, duration, rest;
        Need(static_cast<bool>(std::getline(fields, time, L'\t')) && static_cast<bool>(std::getline(fields, duration, L'\t')) && static_cast<bool>(std::getline(fields, rest)), "Invalid timeline row.");
        Row r; r.time = Number(time, 1200000000); r.duration = Number(duration, 1200000000);
        Need(r.duration > 0 && (rows.empty() || r.time > rows.back().time), "Non-monotonic timeline.");
        if (video) { r.image = rest; Need(r.image.is_absolute() && fs::is_regular_file(r.image), "Missing image."); }
        else { const auto tab = rest.find(L'\t'); Need(tab != std::wstring::npos, "Missing audio byte range."); r.offset = static_cast<uint64_t>(Number(rest.substr(0, tab), 100000000)); r.length = static_cast<uint64_t>(Number(rest.substr(tab + 1), 192000)); Need(r.length && r.length % 4 == 0, "Invalid PCM16 stereo byte range."); }
        rows.push_back(r); Need(rows.size() <= 20000, "Timeline is too long.");
    }
    Need(!rows.empty(), "Empty timeline."); return rows;
}
static std::vector<BYTE> ImageNv12(IWICImagingFactory* factory, const fs::path& path, UINT width, UINT height) {
    ComPtr<IWICBitmapDecoder> decoder; Check(factory->CreateDecoderFromFilename(path.c_str(), nullptr, GENERIC_READ, WICDecodeMetadataCacheOnLoad, &decoder), "Decode image");
    ComPtr<IWICBitmapFrameDecode> frame; Check(decoder->GetFrame(0, &frame), "Image frame");
    UINT w = 0, h = 0; Check(frame->GetSize(&w, &h), "Image size"); Need(w == width && h == height, "Image size changed.");
    ComPtr<IWICFormatConverter> convert; Check(factory->CreateFormatConverter(&convert), "Image converter");
    Check(convert->Initialize(frame.Get(), GUID_WICPixelFormat32bppRGBA, WICBitmapDitherTypeNone, nullptr, 0, WICBitmapPaletteTypeCustom), "Image RGBA");
    const UINT pixels = width * height; std::vector<BYTE> rgba(static_cast<size_t>(pixels) * 4), nv12(static_cast<size_t>(pixels) * 3 / 2);
    Check(convert->CopyPixels(nullptr, width * 4, pixels * 4, rgba.data()), "Image pixels");
    auto clamp = [](int x) { return static_cast<BYTE>(std::clamp(x, 0, 255)); };
    // Explicit BT.601 limited-range conversion, no resizing, overlays or frame interpolation.
    for (UINT y = 0; y < height; ++y) for (UINT x = 0; x < width; ++x) {
        size_t at = (static_cast<size_t>(y) * width + x) * 4; int r = rgba[at], g = rgba[at + 1], b = rgba[at + 2];
        nv12[static_cast<size_t>(y) * width + x] = clamp(((66*r + 129*g + 25*b + 128) >> 8) + 16);
        if ((y % 2) == 0 && (x % 2) == 0) {
            int sr = 0, sg = 0, sb = 0;
            for (UINT dy = 0; dy < 2; ++dy) for (UINT dx = 0; dx < 2; ++dx) { auto p = (static_cast<size_t>(y + dy) * width + x + dx) * 4; sr += rgba[p]; sg += rgba[p + 1]; sb += rgba[p + 2]; }
            r = (sr + 2) / 4; g = (sg + 2) / 4; b = (sb + 2) / 4;
            size_t uv = pixels + static_cast<size_t>(y / 2) * width + x;
            nv12[uv] = clamp(((-38*r - 74*g + 112*b + 128) >> 8) + 128);
            nv12[uv + 1] = clamp(((112*r - 94*g - 18*b + 128) >> 8) + 128);
        }
    }
    return nv12;
}
static ComPtr<IMFMediaType> VideoType(const GUID& subtype, UINT w, UINT h, UINT fps, bool output) {
    ComPtr<IMFMediaType> t; Check(MFCreateMediaType(&t), "Video type");
    Check(t->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video), "Video major"); Check(t->SetGUID(MF_MT_SUBTYPE, subtype), "Video subtype");
    Check(t->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive), "Progressive");
    Check(MFSetAttributeSize(t.Get(), MF_MT_FRAME_SIZE, w, h), "Video size"); Check(MFSetAttributeRatio(t.Get(), MF_MT_FRAME_RATE, fps, 1), "Nominal video rate");
    Check(MFSetAttributeRatio(t.Get(), MF_MT_PIXEL_ASPECT_RATIO, 1, 1), "Square pixels");
    Check(t->SetUINT32(MF_MT_YUV_MATRIX, MFVideoTransferMatrix_BT601), "YUV matrix");
    Check(t->SetUINT32(MF_MT_VIDEO_NOMINAL_RANGE, MFNominalRange_16_235), "YUV range");
    if (output) Check(t->SetUINT32(MF_MT_AVG_BITRATE, 8000000), "Video bitrate");
    return t;
}
static ComPtr<IMFMediaType> AudioType(bool compressed) {
    ComPtr<IMFMediaType> t; Check(MFCreateMediaType(&t), "Audio type"); Check(t->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Audio), "Audio major");
    Check(t->SetGUID(MF_MT_SUBTYPE, compressed ? MFAudioFormat_AAC : MFAudioFormat_PCM), "Audio subtype");
    Check(t->SetUINT32(MF_MT_AUDIO_NUM_CHANNELS, 2), "Stereo"); Check(t->SetUINT32(MF_MT_AUDIO_SAMPLES_PER_SECOND, 48000), "Audio rate");
    Check(t->SetUINT32(MF_MT_AUDIO_BITS_PER_SAMPLE, 16), "Audio bits"); Check(t->SetUINT32(MF_MT_AUDIO_AVG_BYTES_PER_SECOND, compressed ? 24000 : 192000), "Audio bitrate");
    Check(t->SetUINT32(MF_MT_AUDIO_BLOCK_ALIGNMENT, compressed ? 1 : 4), "Audio alignment");
    if (compressed) Check(t->SetUINT32(MF_MT_AAC_PAYLOAD_TYPE, 0), "AAC raw payload");
    return t;
}
static void WriteSample(IMFSinkWriter* writer, DWORD stream, const BYTE* data, DWORD bytes, const Row& row) {
    ComPtr<IMFMediaBuffer> buffer; Check(MFCreateMemoryBuffer(bytes, &buffer), "Sample buffer"); BYTE* target = nullptr;
    Check(buffer->Lock(&target, nullptr, nullptr), "Buffer lock"); memcpy(target, data, bytes); Check(buffer->Unlock(), "Buffer unlock"); Check(buffer->SetCurrentLength(bytes), "Buffer size");
    ComPtr<IMFSample> sample; Check(MFCreateSample(&sample), "Sample"); Check(sample->AddBuffer(buffer.Get()), "Sample data");
    Check(sample->SetSampleTime(row.time), "Sample PTS"); Check(sample->SetSampleDuration(row.duration), "Sample duration"); Check(writer->WriteSample(stream, sample.Get()), "Encode sample");
}
static void Encode(const fs::path& videoPath, const fs::path& pcmPath, const fs::path& audioPath, UINT w, UINT h, UINT fps, const fs::path& output) {
    Need(output.is_absolute() && output.extension() == L".mp4" && !fs::exists(output), "Choose new absolute MP4 output.");
    auto video = ReadRows(videoPath, true), audio = ReadRows(audioPath, false);
    std::ifstream pcm(pcmPath, std::ios::binary); Need(pcm.good(), "Cannot open PCM."); auto pcmSize = fs::file_size(pcmPath);
    ComPtr<IWICImagingFactory> images; Check(CoCreateInstance(CLSID_WICImagingFactory, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&images)), "WIC factory");
    ComPtr<IMFAttributes> options; Check(MFCreateAttributes(&options, 2), "Writer options");
    Check(options->SetUINT32(MF_SINK_WRITER_DISABLE_THROTTLING, TRUE), "Offline encoding");
    ComPtr<IMFSinkWriter> writer; Check(MFCreateSinkWriterFromURL(output.c_str(), nullptr, options.Get(), &writer), "MP4 writer");
    auto vo = VideoType(MFVideoFormat_H264,w,h,fps,true), vi = VideoType(MFVideoFormat_NV12,w,h,fps,false), ao = AudioType(true), ai = AudioType(false);
    DWORD vstream = 0, astream = 0;
    Check(writer->AddStream(vo.Get(), &vstream), "H264 output"); Check(writer->SetInputMediaType(vstream, vi.Get(), nullptr), "NV12 input");
    Check(writer->AddStream(ao.Get(), &astream), "AAC output"); Check(writer->SetInputMediaType(astream, ai.Get(), nullptr), "PCM input");
    Check(writer->BeginWriting(), "Begin offline encoding");
    size_t v = 0, a = 0;
    while (v < video.size() || a < audio.size()) {
        if (v < video.size() && (a == audio.size() || video[v].time <= audio[a].time)) {
            auto pixels = ImageNv12(images.Get(), video[v].image, w, h); WriteSample(writer.Get(), vstream, pixels.data(), static_cast<DWORD>(pixels.size()), video[v++]);
        } else {
            const auto& row = audio[a++]; Need(row.offset + row.length <= pcmSize, "PCM range beyond end.");
            std::vector<BYTE> data(static_cast<size_t>(row.length)); pcm.seekg(static_cast<std::streamoff>(row.offset)); pcm.read(reinterpret_cast<char*>(data.data()), static_cast<std::streamsize>(data.size())); Need(pcm.good(), "PCM read failed.");
            WriteSample(writer.Get(), astream, data.data(), static_cast<DWORD>(data.size()), row);
        }
    }
    Check(writer->Finalize(), "Finalize MP4"); std::cout << "Encoded " << v << " actual image samples and " << a << " timestamped PCM chunks. No capture performed.\n";
}
static void Inspect(const fs::path& input, const fs::path& directory) {
    Need(!fs::exists(directory), "Choose new inspection directory."); fs::create_directory(directory);
    std::ofstream csv(directory / L"decoded-timeline.csv"), audio(directory / L"decoded-audio.pcm",std::ios::binary);
    csv << "stream,time_100ns,duration_100ns,bytes,flags\n";
    for (bool isAudio : {false, true}) {
        ComPtr<IMFSourceReader> reader; Check(MFCreateSourceReaderFromURL(input.c_str(), nullptr, &reader), "Inspect MP4");
        DWORD wanted = static_cast<DWORD>(isAudio ? MF_SOURCE_READER_FIRST_AUDIO_STREAM : MF_SOURCE_READER_FIRST_VIDEO_STREAM);
        Check(reader->SetStreamSelection(static_cast<DWORD>(MF_SOURCE_READER_ALL_STREAMS), FALSE), "Deselect streams");
        Check(reader->SetStreamSelection(wanted, TRUE), "Select inspection stream");
        if (isAudio) { auto pcm = AudioType(false); Check(reader->SetCurrentMediaType(wanted, nullptr, pcm.Get()), "Decode AAC for inspection"); }
        for (;;) {
            DWORD stream = 0, flags = 0; LONGLONG timestamp = 0; ComPtr<IMFSample> sample;
            Check(reader->ReadSample(wanted, 0, &stream, &flags, &timestamp, &sample), "Read encoded evidence");
            if (flags & MF_SOURCE_READERF_ENDOFSTREAM) break;
            if (!sample) continue;
            LONGLONG duration = 0; Check(sample->GetSampleDuration(&duration), "Decoded duration");
            ComPtr<IMFMediaBuffer> data; Check(sample->ConvertToContiguousBuffer(&data), "Decoded bytes"); BYTE* bytes = nullptr; DWORD length = 0;
            Check(data->Lock(&bytes, nullptr, &length), "Decoded lock");
            csv << (isAudio ? "audio" : "video") << ',' << timestamp << ',' << duration << ',' << length << ',' << flags << '\n';
            if (isAudio) audio.write(reinterpret_cast<char*>(bytes), length);
            Check(data->Unlock(), "Decoded unlock");
        }
    }
    Need(csv.good() && audio.good(), "Inspection write failed.");
}

int wmain(int argc, wchar_t** argv) {
    try {
        Check(CoInitializeEx(nullptr,COINIT_MULTITHREADED), "COM"); Check(MFStartup(MF_VERSION), "Media Foundation");
        if (argc == 10 && std::wstring(argv[1]) == L"--encode") {
            UINT w = static_cast<UINT>(Number(argv[5],1920)), h = static_cast<UINT>(Number(argv[6],1080)), fps = static_cast<UINT>(Number(argv[7],60));
            Need(w >= 16 && h >= 16 && w % 2 == 0 && h % 2 == 0 && fps > 0 && std::wstring(argv[9]) == L"--offline-only", "Invalid dimensions or missing offline acknowledgement.");
            Encode(argv[2],argv[3],argv[4],w,h,fps,argv[8]);
        } else if (argc == 4 && std::wstring(argv[1]) == L"--inspect") Inspect(argv[2],argv[3]);
        else throw std::runtime_error("Use --encode frames.tsv audio.pcm audio.tsv width height nominal_fps NEW.mp4 --offline-only; or --inspect MP4 NEW_DIRECTORY.");
        MFShutdown(); CoUninitialize(); return 0;
    } catch (const std::exception& e) { std::cerr << "ERROR: " << e.what() << '\n'; return 1; }
}
