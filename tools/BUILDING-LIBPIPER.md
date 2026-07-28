# Building libpiper for ChatVoice

You do this once per platform. The output is three things the mod loads at
runtime: `piper.dll`, `onnxruntime.dll`, and the `espeak-ng-data` folder.

## Prerequisites (Windows)

- **CMake 3.21+** — <https://cmake.org/download/>, tick "Add to PATH"
- **Visual Studio 2022 Build Tools** with the "Desktop development with C++"
  workload — <https://visualstudio.microsoft.com/downloads/>
- **Git**

CMake downloads and builds espeak-ng itself, and downloads a prebuilt
onnxruntime. You don't need to fetch either by hand.

There is no prebuilt libpiper to download. The `piper-tts` wheels on PyPI ship
`espeakbridge.pyd` and nothing else — no `libpiper.dll`, no `onnxruntime.dll`
— so the shared library really does have to be compiled here.

## Build

**Build from a short path.** MSVC still enforces `MAX_PATH` on its `.tlog`
intermediates, and espeak-ng is nested deep enough inside the ExternalProject
tree to blow past 260 characters from anywhere normal. `C:\pb` works; a path
under `AppData\Local\Temp\...` does not, and fails with a confusing
`error MSB3491: ... exceeds the OS max path limit`.

```bat
git clone --depth 1 --branch v1.6.0 https://github.com/OHF-Voice/piper1-gpl.git C:\pb\piper1-gpl
cd C:\pb\piper1-gpl\libpiper

cmake -B build -DCMAKE_BUILD_TYPE=Release -DCMAKE_INSTALL_PREFIX=C:/pb/install
cmake --build build --config Release
cmake --install build --config Release
```

`--config Release` is required for Visual Studio generators and harmless
elsewhere, so always pass it.

CMake 4.x builds this fine as-is. Do **not** add
`-DCMAKE_POLICY_VERSION_MINIMUM` to work around the version bump — CMake 4
rejects the value inside the generated ExternalProject scripts and the
configure step fails before it reaches espeak-ng.

Expect five to ten minutes; most of it is espeak-ng, which emits a wall of
`C4005`/`C4068` warnings that are normal.

## Where the files go

Copy from `C:\pb\install\` into the ChatVoice data folder:

```
Documents\My Games\Terraria\tModLoader\ChatVoice\
  native\
    piper.dll                          <- install\piper.dll
    onnxruntime.dll                    <- install\lib\onnxruntime.dll
    onnxruntime_providers_shared.dll   <- install\lib\
    piper_exe.exe                      <- install\bin\   (optional, for testing)
    espeak-ng-data\                    <- install\espeak-ng-data\   (whole folder)
  voices\
    en_US-libritts_r-medium.onnx
    en_US-libritts_r-medium.onnx.json
    es_ES-sharvard-medium.onnx
    es_ES-sharvard-medium.onnx.json
```

Do not copy `install\lib\onnxruntime.pdb` — it is 357 MB of debug symbols and
nothing needs it.

## Checking it works, before involving Terraria

`piper_exe.exe` links the same `piper.dll`, so it isolates the native stack
from the mod entirely:

```bat
cd "%USERPROFILE%\Documents\My Games\Terraria\tModLoader\ChatVoice"
echo hello there | native\piper_exe.exe -m voices\en_US-libritts_r-medium.onnx --espeak_data native\espeak-ng-data -f test.wav
```

A ~250 KB `test.wav` at 22.05 kHz mono means the C++ side is good and anything
still broken is in the P/Invoke layer or the mod.

Voices come from `tools\get-voices.ps1`.

`PiperNative.cs` probes for `piper.dll`, `libpiper.dll`, `libpiper.so` and
`libpiper.dylib`, so whichever name CMake produces will be found — but if you
see a `DllNotFoundException`, check the actual filename in `install\` first.

## Packaging a release

`tools\make-release.ps1` does this. It produces two zips — `native\` and
`voices\` separately, so a change to one doesn't force a re-download of the
other — and prints the SHA-256 of each.

The zips **must** use forward slashes in their entry names. Windows
PowerShell's `Compress-Archive` (and .NET Framework's `ZipFile`) writes
backslashes instead, which some extractors turn into one long filename rather
than a directory tree. The script packs via .NET 8 for that reason, and
`AssetInstaller.Extract` normalizes separators defensively at the other end.

Roughly 137 MB of models plus 14 MB of native libraries.

## Other platforms

The same CMake invocation works on Linux and macOS; use `$PWD` instead of
`%CD%`. You'd produce `libpiper.so` / `libpiper.dylib` and the matching
onnxruntime, and ship a separate zip per platform. The resolver already looks
for those names.

## Licensing

libpiper is GPL-3.0 because it embeds espeak-ng. Because ChatVoice loads it
in-process via P/Invoke rather than talking to a separate program, **the mod
itself is GPL-3.0** — see LICENSE and the note in SETUP.md. When you publish a
release, publish the mod source alongside it, and state which piper1-gpl commit
the binaries were built from.
