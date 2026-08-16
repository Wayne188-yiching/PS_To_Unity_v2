---
name: "Photoshop To Unity V2"
description: "以深墨工作台、深藍資訊面與薄荷綠訊號呈現可驗證的 Photoshop 到 Unity 轉譯。"
colors:
  base-60: "#07131a"
  surface-30: "#12314a"
  accent-10: "#2ee6b2"
  ink: "#f2f8f7"
  muted: "#a8bec3"
  line: "rgba(168, 190, 195, .22)"
  soft-accent: "rgba(46, 230, 178, .10)"
  panel: "#0b1e2a"
typography:
  display:
    fontFamily: '"Report CJK", "Bahnschrift", "Microsoft JhengHei", sans-serif'
    fontSize: "clamp(3.7rem, 9vw, 6rem)"
    fontWeight: 900
    lineHeight: 0.94
    letterSpacing: "-0.04em"
  headline:
    fontFamily: '"Report CJK", "Bahnschrift", "Microsoft JhengHei", sans-serif'
    fontSize: "clamp(2.5rem, 6vw, 5rem)"
    fontWeight: 900
    lineHeight: 1.02
    letterSpacing: "-0.035em"
  title:
    fontFamily: '"Report CJK", "Bahnschrift", "Microsoft JhengHei", sans-serif'
    fontSize: "clamp(1.2rem, 2.1vw, 1.65rem)"
    fontWeight: 700
    lineHeight: 1.25
  body:
    fontFamily: '"Report CJK", "Microsoft JhengHei", "Noto Sans TC", "Segoe UI", sans-serif'
    fontSize: "17px"
    fontWeight: 400
    lineHeight: 1.7
  report-text:
    fontFamily: '"Report CJK", "Microsoft JhengHei", "Noto Sans TC", "Segoe UI", sans-serif'
    fontSize: "18px"
    fontWeight: 400
    lineHeight: 1.7
  label:
    fontFamily: '"Report CJK", "Microsoft JhengHei", "Noto Sans TC", "Segoe UI", sans-serif'
    fontSize: "16px"
    fontWeight: 700
    lineHeight: 1.4
rounded:
  compact: "8px"
  control: "10px"
  container: "14px"
  round: "50%"
spacing:
  xs: "8px"
  sm: "12px"
  md: "16px"
  lg: "24px"
  xl: "34px"
components:
  button-primary:
    backgroundColor: "{colors.accent-10}"
    textColor: "{colors.base-60}"
    typography: "{typography.label}"
    rounded: "{rounded.control}"
    padding: "10px 18px"
    height: "46px"
  button-secondary:
    backgroundColor: "transparent"
    textColor: "{colors.ink}"
    typography: "{typography.label}"
    rounded: "{rounded.control}"
    padding: "10px 18px"
    height: "46px"
  chip:
    backgroundColor: "rgba(7, 19, 26, .36)"
    textColor: "{colors.ink}"
    typography: "{typography.report-text}"
    rounded: "{rounded.compact}"
    padding: "7px 10px"
  navigation-item:
    backgroundColor: "transparent"
    textColor: "{colors.muted}"
    typography: "{typography.label}"
    rounded: "{rounded.compact}"
    padding: "8px 10px"
  mapping-row:
    backgroundColor: "transparent"
    textColor: "{colors.ink}"
    typography: "{typography.report-text}"
    padding: "16px 20px"
    height: "76px"
  app-window:
    backgroundColor: "{colors.panel}"
    textColor: "{colors.ink}"
    rounded: "{rounded.container}"
  prefab-tree:
    backgroundColor: "{colors.base-60}"
    textColor: "{colors.ink}"
    rounded: "{rounded.container}"
    padding: "clamp(28px, 5vw, 58px)"
---

# Design System: Photoshop To Unity V2

## Overview

**Creative North Star: "深墨語意工作台"**

這個視覺世界像一張專注、可信任的製作工作台：深墨承接主要閱讀場，深藍切出資訊面，薄荷綠只在轉譯路徑、狀態與關鍵行動發出訊號。它延續專業創作工具的暗色介面氣質，但以高對比、短文案與清楚的資料結構，讓非技術觀眾也能讀懂。

圖層、節點、連線、階層與狀態標記是可重用的產品語彙；它們讓「設計內容被理解後重建」變得可見。特定報告的成果軌跡章節順序、第一視窗左右構圖、逐句文案與 43 → 26 實例只是該表面的敘事，不是未來畫面的固定模板。

**Key Characteristics:**

- 嚴格的深墨／深藍／薄荷綠 60／30／10 色彩權重。
- 嵌入式 Report CJK 可變字重，確保離線繁體中文一致呈現。
- 以色面、細線與結構建立平坦深度，不靠浮誇陰影。
- 圖層列、映射列、應用程式視窗與 Prefab 樹承載產品識別。
- 投影可讀的關鍵文字與完整的鍵盤、減少動態支援。

## Colors

整體是冷調而高對比的暗色工作台；鮮明薄荷綠是唯一的視覺訊號，不作大面積裝飾。

### Primary

- **薄荷訊號** (#2ee6b2)：只用於主要行動、當前狀態、連線、指示圖示、數字重點與精準標記。

### Neutral

- **深墨工作台** (#07131a)：占最大面積的頁面底色，也可作反相按鈕與 Prefab 樹底色。
- **深藍資訊面** (#12314a)：用於章節色面、視窗標題列與層級區隔。
- **深色面板** (#0b1e2a)：應用程式視窗的內部面板，介於底色與資訊面之間。
- **高亮墨白** (#f2f8f7)：主要文字與關鍵名稱，維持暗底上的清楚對比。
- **冷灰說明字** (#a8bec3)：次要說明、版本資訊與輔助標籤。
- **冷灰結構線** (rgba(168, 190, 195, .22))：一像素分隔線、邊框與軌道結構。
- **薄荷低語** (rgba(46, 230, 178, .10))：導覽目前項目等低強度狀態背景。

### Named Rules

**The 60／30／10 Rule.** 深墨約占 60%、深藍約占 30%、薄荷綠不超過約 10%；薄荷綠的稀少性就是層級。

**The One Signal Rule.** 不加入第二個高彩度強調色；警示或狀態先靠文案、圖示與既有明暗層次表達。

## Typography

**Display Font:** Report CJK（Bahnschrift、Microsoft JhengHei、sans-serif 後備）  
**Body Font:** Report CJK（Microsoft JhengHei、Noto Sans TC、Segoe UI、sans-serif 後備）

Report CJK 以 WOFF2 嵌入並支援 100–900 可變字重，使離線報告、投影與跨機器呈現保持一致。標題緊縮而強勢，內文寬鬆而直接；兩者同源，讓技術內容不顯零碎。

### Hierarchy

- **Display** (900, fluid 3.7rem–6rem, 0.94): 只用於單一主價值主張；CSS 字級為 `clamp(3.7rem, 9vw, 6rem)`，字距 -0.04em，控制在約 11 個字元寬。
- **Headline** (900, fluid 2.5rem–5rem, 1.02): 章節標題；CSS 字級為 `clamp(2.5rem, 6vw, 5rem)`，字距 -0.035em，控制在約 15 個字元寬。
- **Title** (700, fluid 1.2rem–1.65rem, 1.25): 卡片、步驟與視窗內的小標題；CSS 字級為 `clamp(1.2rem, 2.1vw, 1.65rem)`。
- **Body** (400, 17px, 1.7): 一般敘述；行長約 66ch。
- **Report Text** (400 或 700–900, 18px, 1.7): 映射、流程、結果與護欄等觀眾必須讀到的內容。
- **Label** (700, 16px, 1.4): 導覽、版本、狀態與緊湊控制項；品牌標記可縮至 14px，但不得承載必要資訊。

### Named Rules

**The Projection Floor Rule.** 觀眾必須理解的報告內容不得小於 18px；16px 僅供導覽與輔助標記，14px 僅限品牌符號。

## Layout

內容置於最大寬度 1180px 的置中容器。主要章節以最小高度 82vh 與 `clamp(84px, 11vw, 154px)` 的垂直留白形成演示節奏；桌面採不對稱雙欄或多欄資料結構，讓主張、證據與結構示意各自有清楚的掃讀路徑。間距以 8、12、16、24、34px 為常用節拍，較大的 48–96px 間距只用於章節與主要群組。

在 940px 以下，雙欄內容收為單欄，五欄與四欄序列收為兩欄，桌面導覽暫時隱藏。在 680px 以下，所有主要序列收成單欄，導覽以可水平捲動列重新出現，左右映射表切換為逐項配對列，程式型別尾欄可隱藏；章節取消視窗高度要求並使用 16px 側邊距。任何新表面都可採自己的敘事順序，不應複製既有報告的成果軌跡構圖。

## Elevation & Depth

系統沒有盒狀陰影詞彙。深度由深墨、深藍與面板色的平面疊層、一像素半透明邊線、留白和裁切建立；固定導覽可使用 `backdrop-filter: blur(12px)` 與接近不透明的深墨底。轉譯節點背後的徑向薄荷光只是一個局部焦點，不是通用卡片陰影。

### Named Rules

**The Flat Workbench Rule.** 靜止表面保持平坦；先用色面、邊線與間距表達層級，不為一般卡片增加陰影。

## Shapes

主要應用程式視窗與 Prefab 樹使用柔和但克制的 14px 圓角。按鈕使用 10px；導覽項目、晶片、狀態標記與小型標籤多使用 7–8px。結構列以水平一像素細線組織；節點可使用 3px 小方框，流程橋接與視窗控制點才使用完整圓形。圓角用於保持工具感的友善度，不應把所有資訊容器做成膠囊。

## Components

### Buttons

- **Shape:** 緊湊而有重量的圓角矩形（10px），最小高度 46px，內距 10px 18px。
- **Primary:** 薄荷訊號底搭配深墨文字與同色邊框，供單一主要行動使用。
- **Secondary:** 透明底、高亮墨白文字與冷灰結構線，與主要按鈕並列但不爭奪注意力。
- **Hover / Focus:** 滑入時邊框轉為薄荷訊號；鍵盤焦點使用 3px 薄荷外框與 4px 位移。狀態變化不依賴動畫才可理解。

### Chips

- **Style:** 低透明深墨底、冷灰結構線、8px 圓角、7px 10px 內距；文字使用至少 18px、700 字重。
- **State:** 作能力與技術標籤，不作第二套彩色狀態系統；需要選取時沿用薄荷低語背景與薄荷訊號文字。

### Cards / Containers

- **Corner Style:** 主要結構容器使用 14px；小型控制或標籤使用 8–10px。
- **Background:** 應用程式視窗使用深色面板，標題列使用深藍資訊面；Prefab 樹使用深墨工作台。
- **Shadow Strategy:** 不使用盒狀陰影，依靠色面與一像素冷灰結構線。
- **Internal Padding:** 視窗列通常 9–16px；大型 Prefab 樹使用 clamp(28px, 5vw, 58px)。

### Navigation

黏附式導覽以接近不透明的深墨底、底部細線與 12px 模糊保持上下文。導覽文字為 16px、700；預設冷灰，滑入與目前項目使用薄荷文字加薄荷低語背景。小螢幕回復為可鍵盤操作、可水平捲動的章節列。

### Mapping Rows

映射列是語意轉譯的核心資料元件：每列最小高度 76px、內距 16px 20px，以薄荷線性圖示、主要名稱、18px 冷灰說明與一像素分隔線組成。桌面可在兩組列之間置入單向細箭頭；手機改為每項就地配對，避免橫向對照失去上下文。

### App Window

應用程式視窗以 14px 圓角、深色面板和一像素邊框建立 Photoshop／Unity 工具語感。50px 高的深藍標題列承載應用程式名稱與三個低對比圓點；內容以 48px 以上的圖層列、薄荷線圖示和縮排呈現階層。

### Prefab Tree

Prefab 樹使用深墨底、14px 圓角與可變內距。標題列、節點列與型別標籤形成清楚三層：高亮名稱、冷灰節點、薄荷元件型別。深度以每層 24px 縮排表達；小螢幕可隱藏型別欄，但不可破壞節點順序。

## Do's and Don'ts

### Do:

- **Do** 以 60／30／10 維持深墨主場、深藍資訊面與稀少薄荷訊號。
- **Do** 讓所有觀眾必讀的映射、流程、結果與護欄文字至少 18px。
- **Do** 用圖層、節點、連線、映射列與 Prefab 階層把抽象轉譯變成可掃讀的結構。
- **Do** 保留 940px 與 680px 的響應式轉換、清楚的 `:focus-visible` 外框及 `prefers-reduced-motion` 降級。

### Don't:

- **Don't** 加入第二個高彩度強調色、霓虹漸層或大面積薄荷裝飾。
- **Don't** 用陰影堆出一般卡片層級；平面色階、一像素邊線與間距才是預設方法。
- **Don't** 把特定報告的章節順序、第一視窗構圖、完整文案或 43 → 26 證據套用到其他表面。
- **Don't** 為了桌面對稱而在手機保留難以閱讀的並排映射或程式型別尾欄。
