# MusicVideoMediaPlayer

多媒體播放器 · AvaloniaUI · **Stage-centric Pro Player**（參考 KMPlayer / PotPlayer IA）

跨平台桌面多媒體播放器，使用 **AvaloniaUI 12** + **MVVM**（CommunityToolkit.Mvvm）+ **LibVLC** 實際播放。

## 介面架構

| 區域 | 內容 |
|------|------|
| **選單列** | 檔案 / 播放 / 檢視（開啟檔案、網路串流、全螢幕、播放清單…） |
| **主舞台** | 影片：內嵌 `EmbeddedVideoView`；音樂／待機：封面 + 波形 |
| **播放清單** | 右側可開關 dock（300px），統一音樂 + 影片佇列 |
| **控制列** | 停止、上一首、播放/暫停、下一首、±10s、進度、靜音、音量、清單、全螢幕、開啟 |
| **狀態列** | 狀態訊息與格式摘要 |

示範資料：1 筆音訊 + 1 筆影片（無實體檔，僅 UI 預覽）。

### 開啟媒體

| 方式 | 說明 |
|------|------|
| **檔案 → 開啟檔案** / `Ctrl+O` | 音樂 + 影片 |
| **拖放** | 將檔案拖進視窗 |
| **檔案 → 開啟網路串流…** | http(s) URL |

本機音訊／影片以 **LibVLC** 實際播放；示範列無檔案時僅 UI 預覽。清除清單會**全清**（含示範列）。

## 快捷鍵

| 按鍵 | 功能 |
|------|------|
| `Space` | 播放／暫停（活躍媒體） |
| `S` | 停止 |
| `←` / `→` | 倒退／快轉 5 秒 |
| `Shift+←` / `→` | ±30 秒 |
| `↑` / `↓` | 音量 ±5% |
| `M` | 靜音 |
| `P` | 播放清單顯示切換 |
| `F` | 全螢幕 |
| `Esc` | 離開全螢幕 |
| `Ctrl+O` | 開啟檔案 |

音訊舞台：單擊播放/暫停、滾輪調音量、右鍵選單、雙擊全螢幕。  
影片舞台：因 HWND airspace，以鍵盤與底部控制列為主（點在影像上不保證觸發 Avalonia 手勢）。

## 需求

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Windows / Linux / macOS（Avalonia 桌面；影片 HWND 嵌入以 Windows 為主）

## 執行

```bash
dotnet restore
dotnet run
```

## 專案結構

```
MusicVideoMediaPlayer/
├── Models/           # MediaItem, MediaKind, ChromeMode, …
├── ViewModels/       # MainViewModel
├── Views/            # MainWindow 舞台殼層
├── Styles/           # Pro Dark tokens、控制項樣式
├── Controls/         # EmbeddedVideoView
├── Services/         # LibVLC MediaEngine、中繼資料
└── docs/             # 設計文件（KM/Pot 重設計）
```

## 設計語言

- **Pro Dark**：中性深灰 `#0D0D0D`–`#1E1E1E`
- 強調色鎖定 **`#3B9EFF`**
- 舞台中心 + 底部密集 control bar（非串流 App 側欄）
- 中文介面文案；預設音量 100%

設計規格見 [`docs/redesign-km-potplayer.md`](docs/redesign-km-potplayer.md)。

## 後續可擴充

- 單一 `MediaEngine` 合併（PR-7）
- Compact 模式、A-B、字幕檔、原生影片舞台點擊
- 主題切換、媒體庫資料庫
