# Product

<!-- impeccable:product-schema 1 -->

## Platform

adaptive

## Users

主要使用者是在 Windows、macOS 或 Linux 桌面上播放本機影音、音樂與網路串流的繁體中文使用者。典型工作是在單一視窗內開啟媒體、管理播放佇列、切換字幕與音軌，並以鍵盤快速控制播放。

## Product Purpose

「鋒兄播放器」是一款以 Avalonia 建置的跨平台桌面影音播放器。成功標準是讓常見本機格式與網路串流能可靠播放，播放狀態清楚、控制反應直接，且大部分 UI 與應用邏輯可跨平台共用。

## Positioning

採用舞台為中心的專業播放器資訊架構：播放畫面是主角，播放清單以可收合 dock 輔助，常用傳輸控制固定在底部；播放核心則與 Avalonia UI 分層，保留日後更換引擎的空間。

## Operating Context

- 從檔案選擇器或拖放開啟本機音訊、影片與字幕。
- 直接開啟 HTTP(S)、RTSP 等網路串流；網頁影片可透過系統安裝的 yt-dlp 解析。
- 使用滑鼠、播放列、選單與鍵盤快捷鍵操作。
- 播放清單預設為空，媒體由使用者明確加入。

## Capabilities and Constraints

- Avalonia 桌面應用，目標框架為 .NET 10。
- CommunityToolkit.Mvvm 管理 ViewModel、命令與狀態通知。
- LibVLCSharp 負責解碼、播放、串流、字幕、音軌與原生影片輸出。
- TagLibSharp 讀取媒體標籤、封面與基本媒體資訊。
- System.Text.Json 儲存最近播放與最近串流；目前未導入 SQLite。
- Windows 內嵌影片使用原生 HWND，必須遵守 Avalonia airspace 限制。
- Windows 隨程式帶入 LibVLC runtime；macOS 與 Linux 的原生庫部署方式依平台處理。
- FFmpeg、SQLite、Serilog 與依賴注入屬於需要相關功能時再導入的擴充，不是目前執行必要條件。

## Brand Commitments

- 正式中文名稱：鋒兄播放器。
- 介面以繁體中文為主。
- 產品是桌面專業播放器，不採用串流平台式內容推薦或社交資訊架構。
- 視覺基準參考 KMPlayer / PotPlayer 的高密度、舞台中心操作模式，但不複製其品牌資產。

## Evidence on Hand

- 可建置的 Avalonia 應用與 LibVLC 播放服務。
- 播放清單、最近播放、串流解析、LRC、字幕、媒體中繼資料與原生影片宿主實作。
- 介面與互動規格位於 `docs/redesign-km-potplayer.md`。
- 尚未提供正式品牌標誌、產品截圖或跨平台發佈簽章。

## Product Principles

1. 播放可靠性高於裝飾效果。
2. 常用操作一眼可見，進階操作保留在選單與快捷鍵。
3. UI 與播放核心維持清楚邊界，平台特殊處理集中封裝。
4. 未使用的基礎設施不提前加入；需求出現時再擴充資料庫、轉檔與遙測能力。
5. 網路與檔案錯誤必須用繁體中文說明問題與復原方式。

## Accessibility & Inclusion

所有核心播放操作需可由鍵盤完成；文字與控制項在深色背景上維持清楚對比，焦點與停用狀態必須可辨識。
