# SeasonAudio

Managed wrapper over the [audio.cpp](https://github.com/SeasonRealms/audio.cpp) C ABI.

One native library (`audiocpp`) covering sixteen tasks - voice activity, ASR,
diarization, source separation, music/song generation, TTS, voice cloning,
conversion, speech-to-speech, alignment, voice design, speaker embedding,
singing conversion, MIDI, wake word and classification. This package is the
managed half; the native half is produced by
[`.github/workflows/build-audio-cpp.yml`](.github/workflows/build-audio-cpp.yml).

SeasonAudio is *not* a replacement for SeasonTTS / SeasonSTT / SeasonMusic. It is
the broad, model-agnostic entry point: the user imports the weights they are
licensed to use, and the wrapper exposes whatever that family declares. The
narrow packages stay where they are, keep their iOS targets, and remain the right
choice when one specific model is all that is needed.

## Platform support

| Target | ggml | Status |
| --- | --- | --- |
| Windows x64 | static, private | supported (CPU / CUDA / Vulkan) |
| Linux x64 | static, private | supported (CPU / CUDA / Vulkan) |
| Mac Catalyst arm64 | static, private | supported (Metal) |
| Android arm64-v8a | static, private | supported, CPU only |
| iOS | - | **not built.** Every ggml runtime there is a static archive linked into the app, so `libaudiocpp.a` would add its own ggml next to `libggml.a` (SeasonGGML) and the ggml objects inside `libqwen.a` - duplicate definitions no linker flag can reconcile. Use SeasonTTS / SeasonSTT / SeasonMusic on iOS. |
| Browser | - | not supported; the C ABI has no wasm transport. |

Android is CPU only on purpose: a second `VkInstance` in the same process is a
known hang on mobile drivers, and on Android SeasonGGML's Vulkan runtime is
already resident.

## ggml is private, and that is the design

audio.cpp vendors its own ggml under `external/ggml` (ggml **0.12**). The rest of
the fleet builds against `SeasonRealms/ggml` (ggml **0.27**). Fifteen minor
versions apart: the two trees cannot be merged, and no single `libggml` can serve
both.

They do not have to. `audiocpp` is a shared library that links its ggml
**statically**, exports only `audiocpp_*` symbols, and gets the ggml types
stripped from its dynamic symbol table at link time (`--version-script` +
`--exclude-libs,ALL` on ELF, `-exported_symbols_list` on Mach-O, the default
`CJSON_HIDE_SYMBOLS` visibility on Windows). Verified by the workflow on every
build, and repeatable by hand:

```powershell
dumpbin /DEPENDENTS audiocpp.dll      # no ggml*.dll in the list
dumpbin /EXPORTS    audiocpp.dll      # only audiocpp_*
```

```bash
ldd libaudiocpp.so                    # no libggml*.so
nm -D --defined-only libaudiocpp.so   # only audiocpp_*
```

This is why `SeasonAudio.csproj` has **no `ProjectReference` to SeasonGGML**. An
app that wants both references both packages and the two ggml copies coexist:
they never exchange a pointer, never share a backend registry, and each
`ggml-*` symbol stays inside its own library.

Two consequences worth knowing:

* **CUDA, cuBLAS, cuDNN and cuFFT *are* shared.** Loaders key on module name
  (`cublas64_12.dll`, `libcudart.so.12`), so both ggmls bind the same copy. Those
  DLLs come from `cudacudnn-runtime` and are not shipped here.
* **`GGML_NATIVE` is off** (`-DENGINE_ENABLE_NATIVE_CPU=OFF`). audio.cpp turns it
  on by default, which bakes `-march=native` into the binary - fine on the build
  machine, `SIGILL` on an older CPU. Distributable builds must not use it.

## Loading, sessions, and what is actually worth reusing

The ABI has four layers, and they cost very different things:

```
registry  ->  model  ->  session  ->  request / result
(loaders)     (weights)   (backend)     (one unit of work)
```

**The model is the reusable thing.** Load is a memory map plus family
identification; a session is where the backend, its buffers and the family's
runtime state live. Holding an `AudioModel` across calls to the same weights is
what makes the second call cheap.

**A session is reusable, but it is not a cache.** `audiocpp_session_run` prepares
with the request it is about to run, and preparation carries the *input length* -
a session prepared for one request must not run another without being prepared
again. The ABI does that automatically, so:

* feed requests of similar size back to back and the buffers are reused;
* alternate between a 3-second clip and a 3-minute one and the allocation is
  paid every time regardless.

So the split buys **weight reuse, not buffer reuse**. Keep one model, keep one
session per (task, mode, backend) tuple, and let the length vary.

**This is not SeasonThreeD.** That package drives an out-of-process worker that
streams weights as the TRELLIS pipeline advances, because a run lasts minutes and
the model set is huge. audio.cpp has no such pipeline - the whole model is mapped
up front and generation is the inner loop - so there is nothing to page in
lazily. `StartStream` is the nearest analogue of "inference as you go", and it
streams *audio in* and *events out*, not weights in. Making it a separate process
the way SeasonThreeD does would add IPC on top of a 0.5-3 s call: not worth it.

Sessions are not thread-safe. One session per thread, or serialize; separate
handles may run concurrently, so a thread pool of sessions over one shared model
is the right shape for batch work.

## Quick start

```csharp
using Season.Audio;

// One shot: loads the model, runs, tears everything down.
AudioData speech = SeasonAudio.Synthesize(modelPath, "你好，世界。", language: "zh");
```

```csharp
// What a resident app should do instead.
using var model = SeasonAudio.DefaultRegistry.LoadModel(modelPath);
using var session = model.CreateSession(AudioTask.Tts, AudioMode.Offline);

foreach (var line in lines)
{
    using var request = new AudioRequest();
    request.SetText(line, "zh").SetSpeakingRate(1.05f);
    using var result = session.Run(request);
    Play(result.GetAudio());
}
```

### Introspection instead of hardcoded knowledge

Every family declares its own options, languages and capabilities in its spec, and
the ABI reports them. A picker can be generic:

```csharp
Console.WriteLine($"{model.Family}: {model.Description}");
Console.WriteLine($"timestamps={model.SupportsTimestamps} clone={model.SupportsSpeakerReference}");
Console.WriteLine(string.Join(", ", model.GetLanguages()));

foreach (var option in model.GetOptions(AudioOptionScope.Request))
{
    Console.WriteLine($"{option.Name}={option.DefaultValue}  [{option.MinValue}..{option.MaxValue}]  {option.Description}");
}
```

### Streaming

Pull based - no callback crosses the ABI:

```csharp
using var session = model.CreateSession(AudioTask.Asr, AudioMode.Streaming);
var policy = session.GetStreamPolicy();
session.StartStream();

foreach (var chunk in chunks)          // policy.PreferredChunkSamples frames each
{
    if (session.Push(chunk, 16000) is { } produced)
    {
        using (produced)
        {
            if (produced.GetResult() is { } partial)
            {
                Console.WriteLine(partial.GetText()?.Text);
            }
        }
    }

    while (session.NextEvent() is { } queued)
    {
        using (queued) { /* voice activity, partials, ... */ }
    }
}

using var final = session.FinishStream();
```

### Artifacts: reuse derived voice state

Results can carry opaque payloads (speaker embeddings, acoustic tokens,
diarization state). Persist them and feed them back instead of re-deriving:

```csharp
var embedding = result.GetArtifacts().First(a => a.Kind == AudioArtifactKind.SpeakerEmbedding);
File.WriteAllBytes(cachePath, embedding.Payload);

// later
using var request = new AudioRequest();
request.SetText(line);
request.AddArtifact(embedding.Kind, embedding.Id, File.ReadAllBytes(cachePath));
```

## Sharing a process with SeasonGGML

Reference both packages. Keep these habits:

* **Pick one backend per GPU.** If stable diffusion already owns CUDA device 0,
  run audio.cpp on CPU (`AudioBackendConfig.Default`) or on a different device
  index. Two GPU runtimes on one device double the VRAM lease.
* **Never `FreeLibrary` / unload `audiocpp`.** ggml keeps thread-locals and a
  backend registry; unloading mid-process is undefined.
* **Do not set `GGML_BACKEND` expecting it to reach audio.cpp.** Each ggml has its
  own backend discovery; the environment variable is read by whichever copy
  initialises first. Select with `AudioBackendConfig` instead.
* **`Threads = 1` while another runtime is busy.** Default is one thread for
  exactly this reason.

## Models

This package ships tooling, not weights. The user imports what they are licensed
to use, following each family's own license - audio.cpp's own distribution
already separates the Apache-2.0 code from the model zoo for this reason.

The default build set in `build-audio-cpp.yml` deliberately **excludes** `yue2`
(CC-BY-NC) and `minimax_music3` (conditional commercial terms). Both remain
available: add them to the `models` input when the deployment has the rights.

## Native artifacts

`build-audio-cpp.yml` builds one target per run (Windows / Linux / Mac Catalyst /
Android) and publishes:

```
runtimes/<rid>/native/
  audiocpp.dll | libaudiocpp.so | libaudiocpp.dylib
  audiocpp.h
  audio.cpp.txt   # ref, commit, configured model set
  ggml.txt        # vendored ggml version - pinned by audio.cpp, not chosen here
  README.txt
```

No `ggml*` binaries, no CUDA runtime: the first is static inside `audiocpp`, the
second is `cudacudnn-runtime`'s job.

## License

Wrapper: MIT. audio.cpp: Apache-2.0. Model weights: per model.
