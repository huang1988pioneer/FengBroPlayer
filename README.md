# MusicVideoMediaPlayer

多媒體播放器 · AvaloniaUI

依設計稿實作的跨平台桌面多媒體播放器介面（音樂 + 影片），使用 **AvaloniaUI 12** + **MVVM**（CommunityToolkit.Mvvm）。

## 功能介面

| 區域 | 內容 |
|------|------|
| **左側邊欄** | 媒體庫導覽、播放清單、設定、迷你播放器 |
| **音樂播放器** | 專輯封面、曲目資訊、歌詞、波形、播放控制、音量、播放清單 |
| **影片播放器** | 影片舞台、進度列、互動按鈕、接下來播放佇列、自動播放 |

示範資料採用周杰倫曲目與旅遊 Vlog 佇列，方便直接預覽 UI。

### 開啟本機檔案

| 方式 | 說明 |
|------|------|
| **開啟檔案** | 音樂 + 影片（上方工具列 / 側欄 / `Ctrl+O`） |
| **開啟音樂** | mp3、flac、wav、m4a、aac、ogg… |
| **開啟影片** | mp4、mkv、avi、mov、webm… |
| **拖放** | 將檔案拖進視窗即可加入 |

本機音訊／影片以 **LibVLC** 實際播放；示範曲目無實體檔時僅作 UI 預覽。

## 需求

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Windows / Linux / macOS（Avalonia 桌面）

## 執行

```bash
dotnet restore
dotnet run
```

或建置後執行：

```bash
dotnet build -c Release
dotnet bin/Release/net10.0/MusicVideoMediaPlayer.dll
```

## 專案結構

```
MusicVideoMediaPlayer/
├── Models/           # TrackItem, VideoItem, NavItem, PlaylistItem
├── ViewModels/       # MainViewModel（狀態與指令）
├── Views/            # MainWindow 主介面
├── Styles/           # 深色主題 tokens、控制項樣式
├── Converters/       # 綁定轉換器
├── Helpers/          # 封面色相漸層
├── Controls/         # 內嵌影片宿主
├── Services/         # LibVLC 播放與媒體中繼資料
└── Assets/           # 圖示資源
```

## 設計語言

- 深色夜間介面（`#0B0D14` / `#151925`）
- 主色紫系強調（`#8B6CFF` → `#A78BFA`）
- FluentTheme Dark + 自訂圓角、間距與 transport 按鈕
- 中文介面文案對齊設計稿
- 預設音量 100%

## 後續可擴充

- 本機檔案掃描與媒體庫資料庫
- 主題切換（淺色 / 自訂強調色）
- 鍵盤快捷鍵與系統媒體鍵
