# Design System

## Direction

鋒兄播放器採用「舞台中心的桌面播放機」：中央媒體舞台佔據最大面積，右側播放清單可收合，底部是固定且高密度的傳輸控制與狀態資訊。介面服務播放，不以卡片、推薦內容或裝飾性資訊分散注意力。

## Color

- 應用背景：`#0D0D0D`
- 深層舞台：`#0A0A0A`
- 面板：`#161616`
- 抬升控制：`#222222`
- 主要文字：`#F0F0F0`
- 次要文字：`#A8A8A8`
- 強調色：`#3B9EFF`
- 細分隔線：`#2A2A2A`

色彩策略為 restrained：中性深灰承載長時間觀看環境，藍色只表示主要操作、進度、選取與鍵盤焦點。不要用多個競爭強調色。

## Typography

使用 Avalonia 內建 Inter 作為跨平台工作字體。標題以 15px、Semibold 區分；一般控制與清單文字為 12–13px；狀態及輔助文字為 11–12px。媒體名稱可截斷，但時間、格式與操作標籤不可因縮放而消失。

## Layout

- 視窗最小寬度 800px。
- 主舞台填滿播放清單與底部 chrome 以外的空間。
- 播放清單 dock 的桌面基準寬度為 300px，隱藏時舞台取得全部寬度。
- 傳輸列最小高度 52px；狀態列最小高度 24px。
- 全螢幕時優先保留影片，chrome 依使用者操作顯示或隱藏。

## Components and States

- 主要播放鍵為圓形藍色控制；其他傳輸鍵使用透明背景與一致命中區。
- 清單列不使用卡片陰影，以背景層級表示 hover、selected 與 current。
- 進度與音量使用同一藍色狀態語言。
- 空播放清單要直接提供「開啟檔案」與拖放提示。
- loading、無法解碼、串流解析失敗與檔案不存在都要提供可採取的下一步。
- 核心操作需有鍵盤焦點；停用控制降低明度但仍可辨識。

## Native Video Constraint

Windows 的 LibVLC 影片畫面使用原生 HWND，Avalonia 控制項不能可靠覆蓋在影片像素上。影片手勢不得作為唯一操作路徑；選單、鍵盤與底部控制列始終是權威控制介面。

## Source of Truth

- 顏色與半徑：`Styles/AppTheme.axaml`
- 控制項狀態：`Styles/Controls.axaml`
- 主視窗結構：`Views/MainWindow.axaml`
- 完整互動與 airspace 決策：`docs/redesign-km-potplayer.md`
