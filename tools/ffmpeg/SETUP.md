# 动态预览录制工具

应用本身不需要 FFmpeg；只有 `work/record-theme-preview.cjs` 将录制帧编码为 MP4 时需要。

本地使用 `ffmpeg-static` 5.3.0 提供的 Windows FFmpeg 6.1.1。可执行文件不会提交到 Git，许可证和工具版本资料保留在本目录。

在 Windows 上准备工具：

```powershell
npm install --prefix .\work\preview-tools --no-save ffmpeg-static@5.3.0
Copy-Item .\work\preview-tools\node_modules\ffmpeg-static\ffmpeg.exe .\tools\ffmpeg\ffmpeg.exe
```

录制脚本使用 Node.js 24 的原生 Fetch / WebSocket，需要先通过独立测试资料目录启动 Codex，再运行 `node work/record-theme-preview.cjs`。

上游：[ffmpeg-static](https://github.com/eugeneware/ffmpeg-static)。本目录的许可证适用于对应的第三方工具。
