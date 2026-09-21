# U01-00 Unity foundation

The current implementation candidate follows [ADR16](../../docs/adr/0016-unity-2022-r41-fallback-validation.md): Unity 2022.3, the complete official Cubism R4_1 SDK and Built-in rendering. A0 has authorized migration; final version/interface freeze and dependency release require review of the formal build and runtime evidence. Historical R5 preparation remains in Git history.

## Exact candidate

| Component | Version / setting |
| --- | --- |
| Editor | 2022.3.62f3c1 / 1623fc0bbb97 |
| SDK / Components | Official 5-r.4.1 / ca8babb42333a2e4407aa72a78a12d7268294455 |
| Native Core | Same-package Windows x64 5.1.0 / 0x05010000 |
| Rendering / build | Built-in, Gamma, D3D11, Windows x64 Mono |
| UI packages | uGUI 1.0.0; TextMeshPro 3.0.6; actual resolution in packages-lock.json |
| Input | Legacy Unity input for the fixture Escape key; full UI input/IME belongs to U01-02 |
| Assets | Same-package Mao; pinned Noto Sans CJK SC Regular 2.004 |

The R4_1 README lists Unity 2022.3.61f1 and 6000.0.49f1 as development environments. This machine's distinct c1 patch is validated by our recorded builds, not by treating those editor versions as identical. R4_1 does not promise the R5 advanced blending/offscreen drawing feature set. Do not mix R5 prefabs, materials, Framework or Core into this project.

## Restore and build from a clean source directory

Requirements: the exact Editor above, working Unity license, Windows Standalone x64 Mono support, PowerShell 7 and Python 3.12.10 (repository baseline). From a new worktree at the reported source SHA:

The [official China archive API](https://unity.cn/api/releases?releaseType=full&major=2022) identifies `2022.3.62f3` with `chinesePostfix=f3c1` and `chineseHash=1623fc0bbb97`. Its [Windows installer](https://download.unitychina.cn/download_unity/1623fc0bbb97/Windows64EditorInstaller/UnitySetup64.exe) returned HTTP 200 in the recorded 2026-09-20 HEAD check (3,759,910,736 bytes). Obtain that official installer, activate a valid license for the target machine and add the installed Editor to Hub if necessary. Verify the complete product version and Windows Mono templates before building; this machine's Mono support came with the Windows Editor. Do not substitute the global `96770f904ca7` modules listed elsewhere in the same API. The full installer was not downloaded or hashed during this task, and no second-machine installation/build is claimed.

```powershell
python tools/unity/restore_assets.py --accept-live2d-terms
python tools/unity/verify_native_assets.py --output .bootstrap/unity/native-result.json
pwsh -File tools/unity/Build-Windows.ps1 -UnityEditor '<editor>/Editor/Unity.exe'
```

Read [resource sources and conditions](../../assets/manifest/unity-foundation-resources.md) before restoration. Complete official SDK, Core, model, texture, animation and meta hashes are in [the manifest](../../assets/manifest/unity-foundation.json). `--cache <empty-folder>` exercises full official downloading; `--sdk-package <official-package>` permits a byte-verified local archive. Existing `Assets/Live2D` or its root meta is rejected to prevent mixed SDK trees. Use a new checkout, or explicitly archive the entire old ignored SDK before restoring. Never copy a Library directory from another project.

The SDK assembly itself enables unsafe code; restoration omits only its two global csc/mcs response files and their metas. Official GUIDs are retained. Unity may reserialize imported model prefabs, animations and fade assets; pre-import source hashes and post-import changes are distinct evidence. Restore and run the native probe before Editor import if checking original package bytes.

Scene, owned metas, ProjectSettings and Editor-generated package lock are committed. The normal build uses those files and does not regenerate the scene. Development-only setup is a separate invocation:

```powershell
pwsh -File tools/unity/Build-Windows.ps1 -UnityEditor '<editor>/Editor/Unity.exe' -PrepareOnly
```

After preparation, review and commit generated changes before claiming an exact source build. The build rejects other editor versions and nonempty output directories; each Editor invocation has a 30-minute external deadline. It emits the complete Windows directory/ZIP, license notices, Unity warning/error summary and SHA-256 receipt. `sourceDirty=true` is not clean-commit acceptance evidence. Raw Editor logs may contain machine/licensing data; retain privately and publish only inspected evidence.

## Run the same Windows package

```powershell
pwsh -File tools/unity/Test-Player.ps1 -Player '<build>/AICompanion.Foundation.exe' -EvidenceDirectory '<new-evidence-folder>' -Seconds 600 -GraceSeconds 30
```

This is a visible graphical Player. It instantiates the official Mao, uses pinned Chinese font text, and exits after the requested duration or Escape. No cloud, conversation, microphone or audio playback is started. The label is a read-only IMGUI fixture; it is not the future TMP chat UI or proof of IME support.

The test wrapper enforces a host-clock deadline of Seconds + GraceSeconds. Timeout kills only the Process it launched, waits at most 5 more seconds, retains available evidence and exits 124. Other verification failures exit 1. Runtime counters, logs and images are real Player output; they are never synthesized. External wait excludes file hashing and final evidence writing. The already-reviewed watchdog regression can be reproduced separately:

```powershell
python tools/unity/tests/test_player_harness.py --report .tmp/player-harness.json
```

Those controlled-process fixtures produce synthetic files only under ignored .tmp and cannot count as Unity rendering evidence.

## Rendering and behavior evidence

The fixture repeats a 60-second bounded sequence: Idle; neutral; direct mouth parameter open/close; left/right/both eyes close and reopen; breath low/high/wave; gaze left/right/up/down/center; a single TapBody[0] clip; a single special_01 clip; return to Idle. Parameter writes run through Cubism's ordered update path. The fixture's greeting candidate is mtn_02 from TapBody[0]; the official model does not label it as greeting.

The first cycle captures each stage. Subsequent cycles retain bounded stage data, actual Core geometry response, currently visible masked/inverted drawable IDs and animation callbacks. `behavior-summary.json` explicitly lists unobserved mask paths and failures; visible Core flags do not guarantee an on-screen pixel contribution. `behavior-stages.csv` records submitted parameter proxies and later stable Core samples; they are not asserted to be identical-frame values.

`player-result.json` includes actual runtime/error/material counts, monotonic frame gaps after the first three seconds, the percentage at or below 33.3 ms, gaps at least one second, and Windows GetProcessMemoryInfo counters with a 30-second baseline. Working-set peak is the OS-reported process peak; private bytes and Unity allocations are sampled every five seconds. Review all relevant pictures for textures, alpha edges, clipping and sorting. Counter success, a moved parameter, or a native geometry change alone is insufficient visual proof.

These sweeps establish rendering capability only. They do not implement AvatarPresenter, interactive pointer gaze, natural automatic blinking or real audio-driven lip sync. Full UA02 and later audio/UI/cloud gates retain their original task ownership.

## Module ownership and wiring

| Directory | Owner / boundary |
| --- | --- |
| Runtime/Foundation, Editor | U01-00 rendering fixture and integration tools |
| Runtime/Contracts | Integration owner; pure C# interfaces/DTOs following the reviewed proposal, final freeze by A0 |
| Runtime/Avatar, Prefabs/Avatar | U01-01; consumes actual playback amplitude; owns SDK adaptation |
| Runtime/UI, Runtime/Session/History, Prefabs/UI | U01-02; UI uses Session only |
| Runtime/Session (except History), Transport, Audio | U01-04; identity checks, cancellation, bounded real audio/capture |
| Scenes, Settings, Packages, ProjectSettings | Integration owner only |

See [interface proposal](../../docs/reports/U01/U01-00/interfaces-proposal.md) and [compiled boundary notes](Assets/Companion/Runtime/Contracts/README.md). Module owners deliver components/prefabs and wiring notes, preserve metas, and do not edit total scenes concurrently. Frozen root contracts and historical React/RN projects remain unchanged. U01-00 evidence supports A0 review; it does not itself declare U-G0 or the complete desktop companion finished.
