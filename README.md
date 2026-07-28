# Chat Voice — Terraria TTS

A client-side [tModLoader](https://www.tmodloader.net/) mod that reads Terraria chat
out loud with [Piper](https://github.com/OHF-Voice/piper1-gpl), running locally on your
CPU. Every player gets a consistent voice derived from a hash of their username, and
English and Spanish are detected per message.

No Python, no background server, no account, no API key. Nothing you type leaves your
machine.

---

## Install

1. Download **`ChatVoice.tmod`** from the [latest release](https://github.com/pardo/terraria-tts/releases/latest).
2. Drop it into:

   ```
   Documents\My Games\Terraria\tModLoader\Mods\
   ```

3. Start tModLoader, open **Workshop → Mods**, enable **Chat Voice**, and reload.

That's it. The first time you load into a world the mod downloads about 150 MB of
speech data (the Piper engine and the voice models) and tells you in chat when it's
done. That happens once.

> Only the person who wants to *hear* chat needs the mod. It's client-side — your
> friends don't have to install anything, and it works on any server.

### Trying it out

```
/tts hello there, this is a test
/tts as Carlos hola a todos, donde esta el jefe
```

`as <name>` auditions how a given username maps to a voice, so you can hear the
assignment without a second player. Settings live under
**Settings → Mod Configuration → Chat Voice**.

If the download fails, `/tts install` retries it.

---

## What it sounds like

Voice choice is deterministic — the same username always gets the same voice, on every
machine, every launch. Everything derives from `sha256(username.lowercase())`:

| bytes | drives | range |
|---|---|---|
| 0–3 | `speaker_id` | `% num_speakers` in the model |
| 4–5 | `length_scale` (speaking rate) | 0.92 – 1.14 |
| 6–7 | `noise_w_scale` (variation) | 0.60 – 1.00 |

**English** uses `en_US-libritts_r-medium`, which carries ~900 speakers, so two people
colliding is rare. **Spanish** uses `es_ES-sharvard-medium`, which has only 2 — the rate
and variation jitter is what keeps two Spanish speakers from sounding identical. In a
large Spanish-speaking group the timbre will still repeat.

Language detection is a stopword vote plus a check for Spanish-only characters
(`áéíóúñü¿¡`). Messages under two words fall back to English. *Always use English* in
the config skips detection entirely.

---

## Troubleshooting

Turn on **Log to client.log** in the mod config for per-line diagnostics, then read
`Documents\My Games\Terraria\tModLoader\Logs\client.log`.

| Symptom | Cause |
|---|---|
| "Download failed" in chat | Network or GitHub outage. Retry with `/tts install`. |
| Nothing spoken, no errors | Check the log for `Could not find ChatHelper.DisplayMessage` — the chat hook didn't attach. |
| `DllNotFoundException` | The `native\` folder is incomplete. Delete `Documents\My Games\Terraria\tModLoader\ChatVoice\` and run `/tts install`. |
| Wrong language picked | Short messages default to English by design. Add words to `EsWords` in `LanguageDetector.cs`. |

To check the speech engine independently of the game, the data pack ships
`native\piper_exe.exe`:

```bat
cd "%USERPROFILE%\Documents\My Games\Terraria\tModLoader\ChatVoice"
echo hello there | native\piper_exe.exe -m voices\en_US-libritts_r-medium.onnx --espeak_data native\espeak-ng-data -f test.wav
```

If that produces audio and the mod doesn't, the problem is in the mod, not in Piper.

---

## Building it yourself

See [SETUP.md](SETUP.md) for the mod and
[tools/BUILDING-LIBPIPER.md](tools/BUILDING-LIBPIPER.md) for the native library.

---

## Licensing

**GPL-3.0.** The mod links libpiper — which embeds espeak-ng — into its own process via
P/Invoke, so the whole thing inherits GPL-3.0. See [LICENSE.md](LICENSE.md).

Voice models carry their own separate licenses; each `MODEL_CARD.txt` ships next to its
`.onnx` in the data pack. Read those before redistributing the models.
