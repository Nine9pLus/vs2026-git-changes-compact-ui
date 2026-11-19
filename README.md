# Git Changes Compact UI (VS2022 Style)

還原 Visual Studio 2022 經典緊湊 UI 風格的 Git 變更視窗擴充功能（支援 Visual Studio 2026 / v18+ Fluent 主題）。

---

## 🌟 特色功能 (Features)

1. **緊湊檔案列表行高 (22px Compact Row Height)**
   - 透過動態反射修補 `FluentSizes.ControlMinHeight`，將 VS2026 預設 26px 行高壓縮至最佳視覺比例的 **22px**。
   - 完整保留**左側紫色選取指示條**與**項目的 Fluent 圓角樣式**。

2. **優化 Commit 訊息輸入框 (Compact Commit Message Box)**
   - 將訊息輸入框高度限制於適當範圍 (36px~52px)，解決過高的外層容器佔位問題。
   - 自適應調整下方「認可所有 Repos / 全部提交」按鈕的上下間距與佈局。

3. **清晰的清單分界邊框與內距 (VS2022-Style List Border & Padding)**
   - 為「變更」與「暫存變更」區塊加上 VS2022 經典深色邊框 (`#3F3F46`) 與上下 2px 內距，防止首末項目緊貼邊框，提供更清晰俐落的介面層次感。

---

## 💻 系統需求 (Requirements)

- **Visual Studio**: VS 2022 (v17.14+) / Visual Studio 2026 (v18+)
- **.NET Framework**: 4.7.2+

---

## 🛠️ 開發與建置 (Build & Debug)

```bash
# 建置專案（產生 DLL 與 VSIX）
dotnet build -c Release
```

建置產物位於：
- **VSIX 擴充功能安裝包**: `GitChangesCompactUI/bin/Release/net472/GitChangesCompactUI.vsix`
- **DLL 組件**: `GitChangesCompactUI/bin/Release/net472/GitChangesCompactUI.dll`

---

## 📜 授權 (License)

MIT License
