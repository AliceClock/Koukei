# Koukei

## Native media runtimes

MPV and FFmpeg use the same deterministic runtime layout:

```text
<application-directory>/
├─ ffmpeg/win-x64/*.dll
└─ mpv/win-x64/libmpv-2.dll
```

FFmpeg is restored from the `FFmpeg.LGPL` NuGet package. Place the local MPV
runtime at `Koukei.Mpv/mpv/win-x64/libmpv-2.dll`; it is excluded from Git and
copied during build and publish. To override the runtime locations, set
`KOUKEI_FFMPEG_HOME` or `KOUKEI_MPV_HOME` to the directory containing the
corresponding native libraries.
