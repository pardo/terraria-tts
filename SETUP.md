# Chat Voice — setup

A tModLoader mod that reads chat out loud with [Piper](https://github.com/OHF-Voice/piper1-gpl),
running on your CPU. Each player gets a fixed voice derived from a hash of their username;
English and Spanish are detected per message.

No Python, no background server, no separate process. The mod calls libpiper directly.

---

## For players

Put `ChatVoice.tmod` in `...\tModLoader\Mods\` and enable it. That's the whole install
— see [README.md](README.md).

The mod downloads its own runtime data on first world load, into
`Documents\My Games\Terraria\tModLoader\ChatVoice\` (`native\` and `voices\`). If you
would rather place those by hand, download the two zips from the release and extract
them there yourself; the mod skips the download when the files already exist.

Voice models load the first time someone talks in that language, which takes a second
or two; after that it's immediate.

Test it solo:

```
/tts hello there, this is a test
/tts as Carlos hola a todos, donde esta el jefe
```

`as <name>` lets you audition how different usernames map to different voices without a
second player. Settings are under **Settings → Mod Configuration → Chat Voice**.

---

## For you (building it)

### Toolchain

- **.NET 8 SDK** — <https://dotnet.microsoft.com/download/dotnet/8.0>
- **CMake** and **Visual Studio 2022 Build Tools** (Desktop development with C++) —
  only needed to build libpiper, once

### Build the native library

See `tools\BUILDING-LIBPIPER.md`. Short version:

```bat
git clone https://github.com/OHF-Voice/piper1-gpl.git
cd piper1-gpl\libpiper
cmake -B build -DCMAKE_BUILD_TYPE=Release -DCMAKE_INSTALL_PREFIX=%CD%/install
cmake --build build --config Release
cmake --install build --config Release
```

Then copy `piper.dll`, `onnxruntime.dll` and `espeak-ng-data\` into
`...\tModLoader\ChatVoice\native\`.

### Get the voices

```
powershell -ExecutionPolicy Bypass -File tools\get-voices.ps1
```

~156 MB, straight into the live data folder.

### Build the mod

Link `ChatVoice\` into `ModSources` so the repo stays the single source of truth,
rather than copying it and ending up with two divergent trees:

```bat
mklink /J "%USERPROFILE%\Documents\My Games\Terraria\tModLoader\ModSources\ChatVoice" "<repo>\ChatVoice"
```

`ModSources\tModLoader.targets` must exist next to it, importing the game's
`tMLMod.targets`:

```xml
<Project ToolsVersion="14.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
	<Import Project="<steam>\steamapps\common\tModLoader\tMLMod.targets" />
</Project>
```

Then either build in-game (**Workshop → Develop Mods → Build + Reload**) or from a
terminal, which is much faster to iterate on:

```bat
cd "%USERPROFILE%\Documents\My Games\Terraria\tModLoader\ModSources\ChatVoice"
dotnet build
```

`dotnet build` compiles *and* packages `ChatVoice.tmod` straight into `Mods\`, because
`tMLMod.targets` runs tModLoader's packer as a post-build step. Use
`dotnet build -p:BuildMod=false` for a compile-only check when you just want to see
C# errors.

### Cut a release

`tools\make-release.ps1` builds the two data zips from the live data folder and prints
their SHA-256 hashes. Those hashes are pinned in `AssetInstaller.cs`, so if you rebuild
the zips you must update the constants there and rebuild the .tmod before publishing.

---

## How it works

```
Terraria chat  ──►  ChatHelper.DisplayMessage   (MonoMod hook, client-side)
                          │
                          │  "<Carlos> donde esta el jefe"
                          ▼
                    ChatVoiceSystem             strips the <name> prefix,
                          │                     applies the per-player cooldown
                          ▼
                    PiperEngine (worker thread)
                          │  TextCleaner        strips [i:29], links, "aaaaaaa"
                          │  LanguageDetector   stopword vote -> "es"
                          │  VoiceAssigner      sha256("carlos") -> speaker 1,
                          │                       rate 0.98, variation 0.75
                          │  libpiper           P/Invoke, float PCM chunks
                          ▼
                    PumpPlayback (main thread)  float -> PCM16 -> SoundEffect,
                                                one line at a time
```

### Voice assignment

Everything comes from `sha256(username.lowercase())`:

| bytes | drives | range |
|---|---|---|
| 0–3 | `speaker_id` | `% num_speakers` in the model |
| 4–5 | `length_scale` (speaking rate) | 0.92 – 1.14 |
| 6–7 | `noise_w_scale` (speaking variation) | 0.60 – 1.00 |

Stable on purpose — `String.GetHashCode()` is randomized per process in .NET and would
hand a player a different voice every launch.

**English** uses `en_US-libritts_r-medium`: one 78 MB file with ~900 speakers in it, so
collisions are rare.

**Spanish** is the awkward one. `es_ES-sharvard-medium` is the best available and has
exactly **2** speakers. The rate and variation jitter exists to make two Spanish speakers
sound like different people despite that; the timbre still repeats in a large group. Add
more models to `VoiceFiles` in `PiperEngine.cs` if it bothers you — `es_ES-davefx`,
`es_ES-carlfm` and `es_MX-ald` are each ~60 MB and one more distinct person, though
selection would then need to hash across the model set rather than just speaker ids.

### Language detection

A stopword vote plus a check for Spanish-only characters (`áéíóúñü¿¡`). Two languages
doesn't justify shipping a statistical model. Verified on 12 chat-length sentences;
under two words it falls back to English. *Always use English* in the config skips it.

---

## Licensing

**The mod is GPL-3.0**, because it links libpiper (which embeds espeak-ng) into its own
process. See `LICENSE.md` for what that means and what the alternative would have been.
Voice models carry separate licenses — `get-voices.ps1` saves each `MODEL_CARD` next to
its `.onnx`.

---

## Troubleshooting

**`DllNotFoundException` in client.log.** The resolver looks in
`...\ChatVoice\native\` for `piper.dll` / `libpiper.dll`. Check what CMake actually
named the file.

**"Piper files not found" in chat.** The zip went somewhere other than
`Documents\My Games\Terraria\tModLoader\ChatVoice\`.

**Nothing is spoken, no errors.** Turn on *Log to client.log* and check whether the hook
attached — look for "Could not find ChatHelper.DisplayMessage".

**Crash on first line.** Most likely the native call. See the risk list in `CLAUDE.md`;
the struct layout and the by-value options return are the two things most likely wrong.

**Wrong language picked.** Short messages default to English by design. Add words to
`EsWords` in `LanguageDetector.cs` if your group has particular slang.
