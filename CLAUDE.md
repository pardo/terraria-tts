# ChatVoice — handoff notes

Reads Terraria chat aloud with Piper TTS, called directly through P/Invoke.
Client-side tModLoader mod, no sidecar process, no Python.
Written by Claude in Cowork; **none of the C# has ever been compiled.**
Read this before touching anything.

Architecture history, so nobody re-litigates it:

1. Kyutai Pocket TTS + Python HTTP sidecar — dropped, 2 GB PyTorch download.
2. Piper + Python sidecar frozen with PyInstaller — dropped, still shipped a
   Python runtime and a ~250 MB zip.
3. **libpiper via P/Invoke** — current. No Python at all. The cost is that the
   mod is now GPL-3.0 by linkage (see LICENSE.md).

Any reference to `tts-server`, `pocket-tts`, `ChatVoiceTTS.exe` or an HTTP
`/speak` endpoint is stale and should be deleted.

## Layout

```
ChatVoice/                 the mod (C#, net8.0) -> must live in ModSources/ChatVoice
  PiperNative.cs           P/Invoke bindings + DllImport resolver
  PiperEngine.cs           synthesizers, worker thread, playback queue
  VoiceAssigner.cs         sha256 -> speaker id, rate, variation
  LanguageDetector.cs      en/es stopword vote
  TextCleaner.cs           chat markup stripping
  ChatVoiceSystem.cs       the ChatHelper.DisplayMessage hook
  AssetInstaller.cs        first-run download of native\ and voices\
tools/
  BUILDING-LIBPIPER.md     CMake build + where the DLLs go
  get-voices.ps1           downloads the two voice models
  make-release.ps1         packs the two data zips + prints their hashes
```

Runtime files are **not** in the .tmod — tModLoader can't load native code from
a mod archive. They live at `Documents\My Games\Terraria\tModLoader\ChatVoice\`
in `native\` (piper.dll, onnxruntime.dll, espeak-ng-data\) and `voices\`.
`AssetInstaller` fetches both from the GitHub release on first world load, so a
player only has to drop one .tmod into `Mods\`.

`ModSources\ChatVoice` is a **directory junction** back to `ChatVoice\` in this
repo, so there is one source tree rather than two. `dotnet build` there both
compiles and packages the .tmod. If you find a real copy there instead of a
junction, someone has reintroduced the divergence problem.

## Current state

**Verified**: the pure logic was extracted from the C# source and re-run against
the Python reference implementation it was ported from.

- `LanguageDetector` word lists (75 es / 60 en) and the accent shortcut classify
  all 12 test sentences correctly; `¿` and `ñ` short-circuit to Spanish;
  under two words falls back to English.
- `VoiceAssigner` reproduces the reference values exactly — `Steve` →
  `(653, 0.9269, 0.9977)`, `Alice` → `(233, 1.0292, 0.6011)`, and so on —
  and is stable across case and surrounding whitespace.

**Verified since**: the mod compiles and packages, and the native stack runs.

- `dotnet build` in `ModSources\ChatVoice` compiles clean — 0 errors, 0 warnings
  — and packages `ChatVoice.tmod` into `Mods\`. Toolchain: .NET 8.0.423 SDK,
  tModLoader v2026.05 (`TML_2026_05`).
- libpiper builds from piper1-gpl **v1.6.0**. The C API in
  `libpiper/include/piper.h` is unchanged from the v1.4.2 transcription, so
  `PiperNative.cs` still matches.
- `piper_exe.exe`, which links the same `piper.dll`, synthesizes both languages:
  English 22.05 kHz mono, Spanish likewise. So espeak-ng, onnxruntime and the
  models are all good, and the sample rate `PiperEngine` assumes is right.

**Verified in-game** (tML 2026.5.3.0), which closed most of the risk list:

- The MonoMod hook attaches: `Hook Terraria.Chat.ChatHelper::DisplayMessage(
  NetworkText, Color, byte) added by ChatVoice`. The `byte messageAuthor`
  signature is still current.
- `SetDllImportResolver` works from inside tModLoader's AssemblyLoadContext.
  `piper_create` returns a live handle, and `num_speakers` reads 904 / 2 —
  matching the model cards exactly, so the regex is right too.

**The bug that made it silent** (fixed): `piper_synthesize_next` returns
`PIPER_DONE` *on the same call that carries the final audio chunk*, not on a
later empty one. The loop checked the return code before consuming the chunk,
so it discarded it. Most chat lines synthesize to a single chunk, so this threw
away 100% of the audio — with no error, no exception, and a log that looked
healthy. The empty-audio path now logs a warning instead of returning silently.

Use `tools\piper-smoketest` before blaming the game for anything like this; it
reproduces the P/Invoke layer in about a second.

**Still not verified:**

- Whether `SoundEffect(byte[], int, AudioChannels)` actually plays Piper's
  22.05 kHz 16-bit mono PCM. Synthesis is now known to produce bytes; playback
  is the next link in the chain and has never succeeded.
- `Main.QueueMainThreadAction` (risk 6) has never been reached.
- `AssetInstaller` — the first-run downloader — has never run against a real
  release. Its URLs and checksums were verified out-of-band with curl, but the
  C# path is unexercised, and the local data folder is already populated, so
  the game has never taken that branch.

## Known risk points, in the order they'll probably bite

1. ~~**`piper_default_synthesize_options` returns a struct by value.**~~
   **Resolved — marshals correctly.** `sizeof` is 16 as expected and the values
   come back as `speaker=0 length=1 noise=0.333 noise_w=0.333`, exactly the
   documented multi-speaker defaults. Do not hardcode `noise_scale`.
2. ~~**`PiperAudioChunk` layout.**~~ **Resolved — correct.** `sizeof` is 72,
   `IsLast` reads as a proper bool, `NumSamples` and `SampleRate` are sane. The
   `UnmanagedType.I1` padding assumption was right.
3. **DLL name.** CMake may emit `piper.dll` or `libpiper.dll`. `PiperNative`
   probes both plus the .so/.dylib names. A `DllNotFoundException` means neither
   was found in `native\` — check the real filename first.
4. **onnxruntime load order.** The resolver loads onnxruntime by absolute path
   before libpiper, so the loader has it and picks up its directory. If libpiper
   fails to bind, that ordering is the first thing to check.
5. **`MonoModHooks.Add(MethodInfo, Delegate)`** in `ChatVoiceSystem.Load()`.
   Chosen over the generated `On_ChatHelper.DisplayMessage` event because those
   class names move between tModLoader versions. Failure is soft — grep
   `client.log` for "Could not find ChatHelper.DisplayMessage".
6. **`Main.QueueMainThreadAction`** in `PiperEngine.WarnMissingOnce`. If it
   doesn't exist under this tModLoader version, drop the chat notice and log.
7. **`ModConfig` attributes.** `[Label]`/`[Tooltip]`/`[Header]` deliberately
   omitted — deprecated in favour of localization. Labels come from
   `Localization/en-US.hjson`. Raw property names on the config page means the
   hjson keys are wrong.
8. **`ChatHelper.DisplayMessage(NetworkText, Color, byte)`** — signature from
   the v2026.05 docs. If `messageAuthor` became an `int`, the lookup returns
   null.
9. **Message text shape.** The hook assumes `"<PlayerName> message body"`. A
   server that formats chat differently leaves `speaker` null and the line is
   treated as a system message (dropped by default).

## How to test without a second player

```
/tts hello there
/tts as Carlos hola a todos, donde esta el jefe
```

Turn on *Log to client.log* in the mod config for per-line diagnostics
(`Documents/My Games/Terraria/tModLoader/Logs/client.log`).

To isolate the native layer from the game entirely, build the small CLI in
`piper1-gpl/libpiper` and synthesize a wav from the same model. If that works
and the mod doesn't, the problem is in the P/Invoke layer, not in Piper.

## Design decisions worth not undoing

- **Everything about a voice derives from `sha256(username.ToLowerInvariant())`**
  — speaker id from bytes 0–3, rate from 4–5, variation from 6–7, read
  big-endian. Must stay a stable hash; `String.GetHashCode()` is randomized per
  process and would give a player a different voice every launch.
  (Note: the reference used Python `casefold()`; `ToLowerInvariant()` differs
  for a handful of non-ASCII cases. Irrelevant for ASCII names, worth knowing.)
- **Rate/variation jitter exists because Spanish only has 2 speakers.** English
  has ~900 and wouldn't need it. Don't strip it as redundant.
- **Stopword language detection, not a model.** Two languages doesn't justify
  the dependency. A third language is the threshold to reconsider.
- **`num_speakers` is read from the `.onnx.json` with a regex**, because the C
  API doesn't expose it and the file also carries a several-hundred-entry
  speaker map that isn't worth parsing. The key appears once, at top level.
- **Audio objects only touch the main thread.** The worker produces raw PCM
  bytes; `PumpPlayback()` in `PostUpdateEverything` is the only thing that
  creates, plays or disposes a `SoundEffect`.
- **Synthesis is serialised** and the queue drops lines past `MaxQueue` rather
  than buffering, so speech can't drift minutes behind a busy chat.
- **Failed voice loads are cached as null** so a missing model doesn't retry
  `piper_create` on every single chat line.

## Licensing

The mod is **GPL-3.0** because it links libpiper (which embeds espeak-ng) in
process. This is a consequence of the P/Invoke design and was chosen knowingly
over the separate-process design, which kept the licenses apart but required
bundling a Python runtime. See LICENSE.md. Voice models have their own separate
licenses; `get-voices.ps1` saves each `MODEL_CARD` next to its `.onnx`.

## Likely next steps

- Get it to compile, then get one native call to work. Those are two separate
  milestones and worth treating as such.
- Streaming playback. `piper_synthesize_next` already yields chunks per
  sentence; feeding them to a `DynamicSoundEffectInstance` instead of buffering
  the whole line would cut perceived latency a lot.
- More Spanish voices — see the note in SETUP.md; needs hashing across a model
  set rather than just speaker ids.
- Linux/macOS: the resolver already probes for `.so`/`.dylib`, but nobody has
  built those.
