# License

**ChatVoice is licensed under the GNU General Public License, version 3.**

The full text is in [`COPYING`](COPYING), and at
<https://www.gnu.org/licenses/gpl-3.0.txt>.

## Why

ChatVoice calls `libpiper` in-process through P/Invoke. libpiper embeds
espeak-ng, and both are GPL-3.0. Loading a GPL library into your own process and
calling its functions creates a combined work, so the mod inherits GPL-3.0.

This was a deliberate trade. The earlier design ran Piper as a separate program
and communicated over HTTP, which keeps the two works at arm's length and would
have left the mod's own license open — but it meant shipping a bundled Python
runtime. Direct linking removes ~90 MB and a whole class of packaging problems,
at the cost of the mod being GPL.

## What that means in practice

- Anyone you give the mod to gets the right to the source. In practice: publish
  the repo, and link it from the release page.
- Anyone can modify and redistribute it, under GPL-3.0.
- You cannot relicense it, and you cannot put it in a closed-source project.

For a mod shared on GitHub with friends, none of this is burdensome — you're
publishing the source anyway.

If you ever wanted the mod under a different license, the route back is the
separate-process design: ship Piper as its own executable and talk to it over a
socket. Slower, bulkier, but it decouples the licenses.

## Third-party components

| Component | License | Notes |
|---|---|---|
| [piper1-gpl](https://github.com/OHF-Voice/piper1-gpl) | GPL-3.0 | `piper.dll`, embeds espeak-ng |
| [espeak-ng](https://github.com/espeak-ng/espeak-ng) | GPL-3.0 | phonemization, and the reason Piper is GPL |
| [onnxruntime](https://github.com/microsoft/onnxruntime) | MIT | neural inference |
| Voice models | **varies per voice** | see below |

Voice models are *not* covered by Piper's license. `tools\get-voices.ps1` saves
each model's `MODEL_CARD` next to its `.onnx`, and `make-release.ps1` ships those
cards inside `ChatVoice-voices.zip` so the attribution travels with the models.

Checked before publishing the data pack:

| Model | Dataset | License |
|---|---|---|
| `en_US-libritts_r-medium` | [LibriTTS-R](http://www.openslr.org/141/) | CC BY 4.0 |
| `es_ES-sharvard-medium` | [Sharvard corpus](https://datashare.ed.ac.uk/handle/10283/574) | CC BY 3.0 |

Both permit redistribution with attribution, which is what the release does. If
you add a voice, check its card before putting it in a public download.

I'm not a lawyer and this isn't legal advice.
