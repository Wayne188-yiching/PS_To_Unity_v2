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
    fontSize: "clamp(2.4rem, 5.2vw, 4.4rem)"
    fontWeight: 900
    lineHeight: 1.12
    letterSpacing: "-0.02em"
  headline:
    fontFamily: '"Report CJK", "Bahnschrift", "Microsoft JhengHei", sans-serif'
    fontSize: "clamp(1.75rem, 4.2vw, 3.5rem)"
    fontWeight: 900
    lineHeight: 1.16
    letterSpacing: "-0.02em"
  title:
    fontFamily: '"Report CJK", "Bahnschrift", "Microsoft JhengHei", sans-serif'
    fontSize: "clamp(1.2rem, 2.1vw, 1.65rem)"
    fontWeight: 700
    lineHeight: 1.35
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
  code:
    fontFamily: 'ui-monospace, "Cascadia Mono", "Consolas", "Roboto Mono", monospace'
    fontSize: "16px"
    fontWeight: 700
    lineHeight: 1.6
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
  inline-highlight:
    backgroundColor: "{colors.accent-10}"
    textColor: "{colors.base-60}"
    padding: "0 .16em"
    bandHeight: "1.08em"
    bandOffset: "0.22em"
  code-token:
    backgroundColor: "{colors.soft-accent}"
    textColor: "{colors.accent-10}"
    typography: "{typography.code}"
    rounded: "6px"
    padding: "4px 8px"
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

圖層、節點、連線、階層與狀態標記是可重用的產品語彙；它們讓「設計內容被理解後重建」變得可見。特定報告的成果軌跡章節順序、第一視窗構圖、逐句文案與 43 → 26 實例只是該表面的敘事，不是未來畫面的固定模板。

**Key Characteristics:**

- 嚴格的深墨／深藍／薄荷綠 60／30／10 色彩權重。
- 嵌入式 Report CJK 可變字重，確保離線繁體中文一致呈現。
- 為中文校準的排版度量：`em` 量測、不低於 1.12 的標題行高、語意逗號斷行。
- 以色面、細線與結構建立平坦深度，不靠浮誇陰影。
- 圖層列、映射列、應用程式視窗與 Prefab 樹承載產品識別。
- 等寬字只服務字面值，是唯一允許的第二個字族。
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
**Code Font:** 系統等寬堆疊（ui-monospace、Cascadia Mono、Consolas、Roboto Mono、monospace 後備）

Report CJK 以 WOFF2 嵌入並支援 100–900 可變字重，使離線報告、投影與跨機器呈現保持一致。標題緊縮而強勢，內文寬鬆而直接；兩者同源，讓技術內容不顯零碎。

等寬堆疊只服務字面值：JSON 欄位、圖層標記（`[SCROLL_V]`）、警告碼（`LAYOUT_CROSS_AXIS_DEGRADED`）、程式識別字與工具名稱。它是唯一允許的第二個字族，因為比例式中文字會讓 JSON 縮排與對齊失去意義；一般敘述與標題一律不得使用。

### Hierarchy

- **Display** (900, fluid 2.4rem–4.4rem, 1.12): 只用於單一主價值主張；CSS 字級為 `clamp(2.4rem, 5.2vw, 4.4rem)`，字距 -0.02em，寬度上限 `8.5em`（約 8 個中文字）。
- **Headline** (900, fluid 1.75rem–3.5rem, 1.16): 章節標題；CSS 字級為 `clamp(1.75rem, 4.2vw, 3.5rem)`，字距 -0.02em，寬度上限 `11em`；堆疊式標題可放寬到 `14em`，置中結語為 `13em`。
- **Title** (700, fluid 1.2rem–1.65rem, 1.35): 卡片、步驟與視窗內的小標題；CSS 字級為 `clamp(1.2rem, 2.1vw, 1.65rem)`。
- **Body** (400, 17px, 1.7): 一般敘述；行長約 40em。
- **Report Text** (400 或 700–900, 18px, 1.7): 映射、流程、結果與護欄等觀眾必須讀到的內容。
- **Label** (700, 16px, 1.4): 導覽、版本、狀態與緊湊控制項；品牌標記可縮至 14px，但不得承載必要資訊。
- **Code** (700, 15–17px, 1.6): 標記、警告碼、欄位名與路徑；JSON 區塊使用 16px / 1.85，獨立展示的圖層名標本可放大到 `clamp(1.15rem, 3vw, 2rem)`。

### Named Rules

**The Projection Floor Rule.** 觀眾必須理解的報告內容不得小於 18px；16px 僅供導覽、輔助標記與程式碼標籤，14px 僅限品牌符號。

**The CJK Measure Rule.** 中文文字容器的寬度上限一律用 `em`，不得用 `ch`。`ch` 是拉丁數字「0」的寬度（約 0.5em），`15ch` 對中文只有 7.5 個字；沿用 `ch` 會讓標題每行只剩五、六個字，斷出「同一個畫／面」這種切詞。

**The CJK Leading Rule.** 標題行高不得低於 1.12。Report CJK 的 inline box 約 1.47em，遠高於拉丁顯示字慣用的 0.94–1.02 行高；行高一旦低於字框，任何有背景色的行內元素都會覆蓋上一行的字。

**The Break-at-Comma Rule.** 固定文案的標題在語意逗號處以 `<br>` 手動斷行，讓每行是一個完整子句；同時全站啟用 `line-break: strict`（句號、逗號不落行首）與內文 `text-wrap: pretty`（避免尾行孤字）。標題保留 `text-wrap: balance`。

## Layout

內容置於最大寬度 1180px 的置中容器。主要章節以最小高度 82svh 與 `clamp(84px, 11vw, 154px)` 的垂直留白形成演示節奏；桌面採不對稱雙欄或多欄資料結構，讓主張、證據與結構示意各自有清楚的掃讀路徑。間距以 8、12、16、24、34px 為常用節拍，較大的 48–96px 間距只用於章節與主要群組。

章節標題欄不得小於整體的 `1fr`（搭配說明欄 `.8fr`）。中文標題需要每行 10 字以上才不會切詞，欄位一旦壓到 `.7fr` 就會退回逐字斷行。並列的護欄章節使用 `.85fr / 1.15fr`。

第一視窗採單欄：標題與行動在上、轉譯示意圖佔滿容器寬度。左右並列在 1180px 容器下放不進兩個 18px 文字的視窗（右欄最多約 587px，需要約 664px），硬塞的代價是圖層名稱被截斷——而那些名稱正是示意圖要證明的內容。

在 940px 以下，雙欄內容收為單欄，五欄與四欄序列收為兩欄，桌面導覽暫時隱藏。在 680px 以下，所有主要序列收成單欄，導覽以可水平捲動列重新出現，映射表由並列切換為堆疊配對，圖層列收緊間距，程式型別尾欄可隱藏；章節取消視窗高度要求並使用 16px 側邊距。任何新表面都可採自己的敘事順序，不應複製既有報告的成果軌跡構圖。

### Named Rules

**The Zero-Minimum Track Rule.** 所有 grid 軌道使用 `minmax(0, 1fr)`，容器層級再加 `min-width: 0`。`auto` 軌道的最小尺寸是 min-content，一段長 JSON 或長識別字就會把整條軌道撐破章節寬度，而 `overflow-x: clip` 只會把溢出的內容默默裁掉。根層級使用 `overflow-x: clip`，不用 `hidden`。

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

### Inline Highlight

行內重點使用薄荷底、深墨字，一句只標一段，一個章節不超過一處。

底色以 `linear-gradient` 當背景圖繪製，高度固定 `1.08em`、自內容框頂端偏移 `0.22em`，並使用 `box-decoration-break: clone` 讓多行片段各自成塊。**不得用 `background-color` 直接塗滿行內框**：Report CJK 的 inline box 約 1.47em，比任何標題行高都高，直接塗色必定覆蓋上一行的字。

### Code Tokens

字面值使用等寬堆疊：圖層標記與警告碼採薄荷低語底、薄荷文字與薄荷細框（6–8px 圓角、4–9px 內距）；JSON 區塊使用深色面板與 16px / 1.85 的等寬字，欄位名以薄荷色標示、省略處以冷灰標示。程式碼區塊只用一像素邊框與標題列，**不得補上假的視窗控制點或瀏覽器外框**。橫向過長時由區塊自身 `overflow-x: auto` 承接，並補 `tabindex="0"` 與說明標籤讓鍵盤可捲動。

所有品牌名、標記與識別字標上 `translate="no"`，避免瀏覽器自動翻譯把 `TextMeshPro`、`[SCROLL_V]` 譯成亂碼。

### Cards / Containers

- **Corner Style:** 主要結構容器使用 14px；小型控制或標籤使用 8–10px。
- **Background:** 應用程式視窗使用深色面板，標題列使用深藍資訊面；Prefab 樹使用深墨工作台。
- **Shadow Strategy:** 不使用盒狀陰影，依靠色面與一像素冷灰結構線。
- **Internal Padding:** 視窗列通常 9–16px；大型 Prefab 樹使用 clamp(28px, 5vw, 58px)。

### Navigation

黏附式導覽以接近不透明的深墨底、底部細線與 12px 模糊保持上下文。導覽文字為 16px、700；預設冷灰，滑入與目前項目使用薄荷文字加薄荷低語背景。小螢幕回復為可鍵盤操作、可水平捲動的章節列。

### Mapping Rows

映射列是語意轉譯的核心資料元件：每列最小高度 76px、內距 16px 20px，以薄荷線性圖示、主要名稱、18px 冷灰說明與一像素分隔線組成。

一組來源、箭頭與結果就是一列，桌面以 `1fr / 92px / 1fr` 並列，手機同一份標記直接堆疊、箭頭轉 90 度。**不得為桌面與手機各寫一份映射表**：重複標記會讓兩份內容漂移，而寫死列高的箭頭欄一遇到中文換行就會與對應列錯位。欄位標題在桌面由表頭承擔，手機改由每格內的側別標籤接手，兩者不同時出現。

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
- **Do** 中文容器寬度用 `em`、標題行高不低於 1.12，並在語意逗號處手動斷行。
- **Do** 交付前逐一量測 320／375／414／768／960／1180／1440px：不得有水平溢出，示意圖裡的圖層名與識別字不得被截斷。

### Don't:

- **Don't** 加入第二個高彩度強調色、霓虹漸層或大面積薄荷裝飾；薄荷不得作為整個章節的底色。
- **Don't** 用陰影堆出一般卡片層級；平面色階、一像素邊線與間距才是預設方法。
- **Don't** 把特定報告的章節順序、第一視窗構圖、完整文案或 43 → 26 證據套用到其他表面。
- **Don't** 為了桌面對稱而在手機保留難以閱讀的並排映射或程式型別尾欄。
- **Don't** 用 `ch` 當中文容器寬度、用 `background-color` 塗行內重點、或為同一份資料寫兩套響應式標記。
- **Don't** 用 `nth-of-type` 之類的位置選擇器決定章節底色；深藍資訊面要以語意類別明確指定。
