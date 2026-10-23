# TouchWrite：Windows 觸控板手寫輸入

在 Windows 上重現 macOS 的「觸控板手寫」：按一個快捷鍵，觸控板就變成手寫板，用手指寫字，辨識出的字直接打進目前的輸入框。

## 執行

```
dist\TouchWrite.exe
```

啟動後程式會在系統匣（右下角）。需要 **.NET 9 桌面執行環境**（這台電腦已經有）。

## 用法

| 操作 | 作用 |
|---|---|
| `Ctrl + Shift + Space` | 開始 / 停止手寫（與 Mac 相同；可在設定中錄製任意組合鍵） |
| 一指在觸控板上寫 | 書寫；停筆約 0.9 秒後自動打出第一個候選字（可在設定中關閉自動輸入） |
| 一指輕點觸控板**頂部** | 選擇該位置的候選字（會替換剛打出的字） |
| `1`–`9` | 選擇候選字 |
| 輕點觸控板**右上角** ⌫ | 刪除（寫字途中則清除筆劃）；按住可連續刪除 |
| 兩指向右滑 | 空格 |
| 兩指向下滑 | 換行（Enter） |
| 兩指向上滑 / `Backspace` | 撤銷上一筆 |
| 兩指輕點 / `Space` / `Enter` | 立刻打出目前的字 |
| `Esc` | 清除筆劃；沒有筆劃時退出手寫 |

手寫面板上的區域與觸控板一一對應：頂部一條是候選字，最右端是刪除鍵，其餘是書寫區。

手寫模式下滑鼠指標會被鎖定（避免寫字時移動游標或誤點），退出後即恢復。

## 設定

右鍵點系統匣圖示 → 設定：快捷鍵（點按鈕後直接按下想要的組合鍵；被佔用時會保留原本的）、辨識引擎、輸出繁體 / 簡體、自動輸入開關與等待時間、面板大小、開機自動啟動。
關閉自動輸入後，寫完的字要點選候選字、按 Space 或兩指輕點才會輸入。
介面語言可選「跟隨系統 / 繁體中文 / 简体中文」（設定 → 一般 → 語言）。簡體介面由繁體文字即時轉換，詞彙差異（滑鼠→鼠标、設定→设置等）列在 `Loc.cs`。
設定檔存放在 `%APPDATA%\TouchWrite\settings.json`。

## 需求與限制

- **精確式觸控板**（Precision Touchpad）。大部分 2018 年後的筆電都是；舊式 Synaptics / ELAN 驅動的觸控板讀不到手指的絕對位置。
- **Windows 手寫辨識語言套件**。這台電腦目前只有「中文(简体)手写识别器」，它也能辨識不少繁體字。想要繁體辨識：設定 → 時間與語言 → 語言與地區 → 中文（台灣）或中文（香港）→ 語言選項 → 安裝「手寫」。或在設定裡把輸出字形設成「一律轉為繁體」。
- 手寫時所有滑鼠（包括外接滑鼠）都會暫停，按 `Esc` 或快捷鍵退出即可。
- Windows 不允許一般程式向**以系統管理員身分執行**的視窗輸入文字。如需在這類視窗手寫，請同樣以系統管理員身分執行 TouchWrite。
- 部分程式（如瀏覽器）會直接處理觸控板的兩指捲動，所以兩指滑動手勢可能同時讓頁面捲動一下。

## 開發

```
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o dist
```

自我檢查（不顯示介面）：

```
TouchWrite.exe --diag 報告.txt      # 觸控板參數 + 用合成筆劃測試辨識
TouchWrite.exe --preview 面板.png   # 把手寫面板畫成圖片
```

### 程式結構

| 檔案 | 內容 |
|---|---|
| `Input/PrecisionTouchpad.cs` | 透過 Raw Input 讀觸控板 HID 報告，解析每根手指的絕對座標（支援 hybrid 模式） |
| `Input/InputInterceptor.cs` | 手寫時用低階掛鉤鎖住滑鼠、攔截選字鍵（在獨立執行緒上執行） |
| `Input/TextSender.cs` | 用 `SendInput` 把 Unicode 文字送到目前焦點 |
| `Recognition/HandwritingRecognizer.cs` | 呼叫 Windows 內建的 `InkRecognizerContainer` 辨識 |
| `Recognition/ChineseConverter.cs` | 用 Windows 內建的 `LCMapStringEx` 做繁簡轉換 |
| `HandwritingController.cs` | 手勢判斷、自動上字、候選字替換 |
| `UI/WritingWindow.xaml` | 不搶焦點的手寫面板 |
| `App.xaml.cs` | 系統匣、快捷鍵、設定、診斷模式 |

## 授權

[MIT](LICENSE)
