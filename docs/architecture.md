# 技術架構

鋒兄播放器採用 Avalonia UI、MVVM 與獨立媒體服務的分層方式。UI 負責顯示與輸入，`MainViewModel` 協調播放佇列與應用狀態，`MediaEngine` 封裝 LibVLC 的播放生命週期與原生資源。

## 目前已使用

| 能力 | 框架／庫 | 專案中的責任 |
|---|---|---|
| 跨平台桌面 UI | Avalonia、Avalonia.Desktop、Fluent Theme | 視窗、選單、播放舞台、播放清單與控制列 |
| MVVM | CommunityToolkit.Mvvm | ObservableProperty、Command 與 ViewModel 狀態 |
| 播放核心 | LibVLCSharp.Avalonia | 本機影音、網路串流、字幕、音軌、倍速與 seek |
| Windows 原生 runtime | VideoLAN.LibVLC.Windows | 隨 Windows 組建部署 LibVLC |
| macOS 原生 runtime | VideoLAN.LibVLC.Mac／系統 VLC | Intel 套件；Apple Silicon 優先使用系統 VLC.app |
| 媒體中繼資料 | TagLibSharp | 標題、演出者、專輯、封面、codec 與解析度 |
| 設定與歷史 | System.Text.Json | 最近播放及最近串流的輕量 JSON 儲存 |
| 網頁串流解析 | yt-dlp（外部可選） | 將支援的網頁網址解析成 LibVLC 可播放來源 |

`System.Text.Json` 隨 .NET 提供，不需要額外 NuGet 套件。檔案選擇使用 Avalonia `StorageProvider`，也不需要第三方 file picker。

## 邊界

```text
Views / AXAML
      │ binding、事件
      ▼
MainViewModel
      │ 播放、佇列、最近紀錄
      ├──────────────► RecentPlayStore / RecentStreamStore
      ├──────────────► MediaMetadata / StreamResolver
      ▼
MediaEngine
      │
      ▼
LibVLCSharp + 平台原生 LibVLC
```

`MediaEngine` 是播放引擎邊界。若日後需要切換到 libmpv 或 Avalonia 商業版 MediaPlayer，應先抽出 `IPlaybackService`，再由新實作接替；View 與大部分 ViewModel 不應直接依賴新的原生 API。

## 按需求導入的擴充

| 需求出現時 | 建議庫 | 導入時機 |
|---|---|---|
| 大型媒體庫、收藏、跨條件查詢 | Microsoft.Data.Sqlite | JSON 已無法有效查詢或遷移時 |
| 縮圖、波形、轉檔、剪輯 | FFmpeg / ffprobe | 加入媒體分析與輸出工作流程時；不要用它重造完整播放時鐘 |
| 結構化診斷與崩潰追蹤 | Serilog | 有正式發佈、支援與隱私政策後 |
| 多組可替換服務 | Microsoft.Extensions.DependencyInjection | 服務生命週期與測試替身開始複雜時 |
| 封面裁切與模糊背景 | SkiaSharp | Avalonia 內建影像能力不足時 |
| 完整單元／整合測試 | xUnit + Moq | 將播放介面抽象後，測試 ViewModel 與佇列規則 |

避免只為符合套件清單而加入未使用依賴。每個原生庫都會增加跨平台發佈矩陣、應用體積與安全更新責任。

## 平台部署

- Windows：`dotnet publish -r win-x64`，由 NuGet 套件帶入 LibVLC 原生檔案。
- macOS：Intel 可使用套件；Apple Silicon 目前優先偵測 `/Applications/VLC.app`。
- Linux：安裝與發行版相容的 `libvlc`，或在發佈流程中明確打包並設定 plugin path。

所有平台都需驗證：開啟與連續切換媒體、seek、字幕、全螢幕、關閉後原生 decoder/音訊/GPU 資源釋放。
