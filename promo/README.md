# TouchWrite 宣傳片

成品在 `out/`：

| 檔案 | 用途 |
|---|---|
| `TouchWrite 宣傳片（繁體中文）.mp4` | 1920×1080、60 fps、H.264 + AAC，約 51 秒 |
| `TouchWrite 宣传片（简体中文）.mp4` | 同上，簡體字幕 |
| `covers/… 16x9.jpg / .png` | 1920×1080 封面（YouTube 縮圖、嗶哩嗶哩 16:9 封面） |
| `covers/… 4x3.jpg / .png` | 1440×1080 封面（嗶哩嗶哩 4:3 封面） |

YouTube 縮圖上限 2 MB，請用 `.jpg`；嗶哩嗶哩兩種都可以。

## 重新產生

```
powershell -ExecutionPolicy Bypass -File promo\build.ps1
```

需要 Node 22+、Python 3（numpy）、Microsoft Edge、.NET 9 SDK。不需要 ffmpeg：編碼用的是 Windows 內建的 H.264 / AAC 編碼器。

## 製作方式

- `src/promo.html` + `src/promo.js`：所有畫面都是「時間 → 畫面」的純函式，`window.render(t)` 畫出第 t 秒。文字在 `promo.js` 開頭的 `TEXT`（繁／簡各一份）。
- `src/strokes.js`：示範用的手寫筆劃。畫面上顯示的候選字是把這些筆劃交給 Windows 手寫辨識得到的真實結果。
- `src/assets/`：設定視窗與系統匣選單的真實截圖（由 TouchWrite 的 `--settings-shot` / `--tray-shot` 產生）。
- `tools/capture.mjs`：用無頭 Edge 逐格截圖（DevTools Protocol，不需 npm 套件）。
- `tools/music.py`：配樂與音效全部由程式合成，沒有版權問題。音效依頁面輸出的時間點（筆劃、上字、點擊、轉場）對齊。
- `tools/encoder/`：把影格與 WAV 編碼成 MP4（Media Foundation）。
