AUDIOCPP_REPO: SeasonRealms/audio.cpp
AUDIOCPP_REF: local audio.cpp-main tree (no .git in this checkout)
AUDIOCPP_COMMIT: unknown - rebuilt locally on 2026-10-31
GGML: vendored, 0.12.0, statically linked into audiocpp.dll
GGML_MAX_NAME: 128
Enabled backends: cpu (static, single ISA), cuda12 (sm_89 only), vulkan=OFF
Configuration: Release
Model set: custom
AUDIOCPP_MODELS: builtin_audio_utils,qwen3_tts,qwen3_asr,stable_audio,ace_step,heartmula,kokoro_tts,piper_tts,f5_tts,chatterbox,sense_asr,parakeet_tdt,audiosr,demucs,roformer,yue2,minimax_music3
CUDA Toolkit version: 12.8
CUDA runtime: not packaged - cudart / cublas / cublasLt / cufft come from cudacudnn-runtime

Family list matches the build-audio-cpp.yml default: yue2 (CC-BY-NC-4.0) and
minimax_music3 (Conditional) are compiled in. The package carries loader code
only - the weights are downloaded by the user - so a family's weight license
constrains the use of those weights, not what this binary may contain.
A paid/store build (SeasonStudio) must trim AUDIOCPP_MODELS to the families it
is cleared to ship.

The previous package (2026-10-30, 176,833,536 bytes, portable arch list,
vulkan=ON, no yue2) is kept as .dbg/audiocpp-stock-20261030.dll.

Self-contained by design: this package ships NO ggml DLL. audio.cpp's ggml is
0.12 (its vendored tree) while SeasonGGML ships SeasonRealms/ggml 0.27, and the
two cannot be merged - they share a process only by each keeping its own copy.
The export surface is audiocpp_* only, which is what keeps them apart.

Artifact contents:
audio.cpp.txt
audiocpp.dll
audiocpp.h
ggml.txt
README.txt
