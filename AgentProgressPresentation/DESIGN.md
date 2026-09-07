---
name: PS to Unity Agent Development Report
description: 以可追溯架構圖呈現 deterministic core 到 Agent orchestration 的技術簡報系統
colors:
  primary-trace: "#2ee6b2"
  current-amber: "#f0bc5e"
  planned-slate: "#7893a0"
  review-amber: "#e8c66a"
  blocked-red: "#ef6b73"
  canvas: "#07131a"
  canvas-deep: "#040d12"
  panel: "#0b1e2a"
  surface: "#12314a"
  ink: "#f2f8f7"
  muted: "#a8bec3"
typography:
  display:
    fontFamily: "Report CJK, Microsoft JhengHei, sans-serif"
    fontSize: "hero clamp(3.4rem, 4.35vw, 5.1rem); section clamp(2.35rem, 3.15vw, 3.85rem)"
    fontWeight: 860
    lineHeight: 1.12
    letterSpacing: "-0.03em"
  body:
    fontFamily: "Report CJK, Microsoft JhengHei, Noto Sans TC, sans-serif"
    fontSize: "18px"
    fontWeight: 400
    lineHeight: 1.6
  label:
    fontFamily: "Cascadia Mono, Consolas, monospace"
    fontSize: "12px"
    fontWeight: 800
    lineHeight: 1.2
    letterSpacing: "0.04em"
rounded:
  status: "6px"
  node: "12px"
spacing:
  xs: "8px"
  sm: "14px"
  md: "24px"
  lg: "44px"
components:
  status-chip:
    textColor: "{colors.primary-trace}"
    typography: "{typography.label}"
    rounded: "{rounded.status}"
    padding: "3px 8px"
  architecture-node:
    backgroundColor: "{colors.panel}"
    textColor: "{colors.ink}"
    rounded: "{rounded.node}"
    padding: "14px"
---

# Design System: PS to Unity Agent Development Report

## Overview

**Creative North Star: "The Architecture Trace"**

介面像一份正在執行的工程追蹤圖：深色工作面承載結構，細線負責說明資料與控制流，只有狀態與目前路徑使用強調色。動畫的工作是揭示依賴、批准閘門與責任邊界，不建立娛樂性場景。

**Key Characteristics:**

- Architecture diagram 是主畫面，文字只解釋當前節點。
- Status color 具有固定語意，不能作裝飾。
- 線性、扁平、精確；避免 AI、機器人、HUD 與發光粒子語彙。

## Colors

低彩度藍黑構成工作面，冷綠表示已驗證路徑，琥珀只表示 current 或 review，紅色只保留給 blocked。

**The Status Is Data Rule.** 強調色必須對應 PASS、CURRENT、PLANNED、NEEDS_REVIEW 或 BLOCKED；沒有狀態意義時使用中性色。

## Typography

**Display Font:** Report CJK（本地 WOFF2，Microsoft JhengHei fallback）

**Body Font:** Report CJK（本地 WOFF2，Microsoft JhengHei / Noto Sans TC fallback）

**Label/Mono Font:** Cascadia Mono（Consolas fallback）

**Character:** 中文標題使用緊實粗體形成投影焦點；路徑、狀態、計數與技術欄位才使用等寬字。

### Hierarchy

- **Display**（860，responsive clamp，1.08–1.12）：章節命題與結語。
- **Body**（400，18px，1.6）：證據敘述與說明。
- **Label**（800，12px，0.04em）：狀態、路徑、步驟與資料標籤。

**The Mono Has Meaning Rule.** 等寬字只用在程式資料、狀態、索引、路徑與量測，不用來裝飾整段內文。

## Layout

每章是一個 100svh 的 motion scene，桌面最大內容寬 1760px，兩側 gutter 隨 viewport 伸縮。標題有固定 hero / section / compact 三層上限，避免超寬螢幕把字級放大成海報。核心流程採水平 topology；窄畫面改為單欄而不縮小到不可讀。2560×1187、1920×1080 與 1366×768 都必須保持完整主畫面。

ScrollTrigger pin 將每章固定在 viewport，scrub 只推進路徑、節點狀態與對應 evidence。resize 後重新計算 trigger。頂欄 motion toggle 讓 presenter 明確選擇 FULL 或 REDUCED；reduced motion 取消 pin 與 scrub，將 PSD flow 與 evidence 改成有秩序的靜態版面。

## Elevation & Depth

系統不使用陰影。深度只由背景階層、1px 邊線、節點重疊順序與 active path 對比建立。

**The Flat Evidence Rule.** 不以 glow 或玻璃效果暗示智慧；判斷與完成狀態必須由標籤和流程位置說明。

## Shapes

節點與 status chip 使用小半徑矩形；主要架構容器維持直線與清楚邊界。唯一較特殊的折角只用於 ambiguity board，表示未解決資料而不是裝飾。

## Components

### Status Chips

- **Shape:** 6px 小圓角、1px 語意色邊線。
- **Content:** 大寫狀態文字；顏色與狀態字必須一致。

### Architecture Nodes

- **Shape:** 12px 或直角矩形，依 topology 密度使用。
- **Background:** panel / surface tonal layer，無陰影。
- **State:** active 時只改線色、邊線、少量 scale 或 opacity。

### Evidence Panels

- **Purpose:** 每個 scroll step 只顯示一份直接相關證據。
- **Transition:** opacity、短距離位移、局部 clip reveal 與 path progress；不做散落飛入。

### Navigation

- 固定頂欄顯示報告名稱、章節索引與當前章名。
- 右側 chapter rail 只在寬桌面出現；鍵盤導覽使用瀏覽器可見 focus ring。

## Do's and Don'ts

### Do:

- **Do** 讓每個動畫變化回答「現在執行到哪裡、誰可以繼續」。
- **Do** 明確區分 repo evidence、demo structure 與 planned architecture。
- **Do** 保持 deterministic core 在圖中可見，Agent layer 在其上方擴展。

### Don't:

- **Don't** 將 proposed、partial 或 planned 視覺化成 completed。
- **Don't** 加入 AI 腦袋、機器人、科幻 HUD、粒子或無意義光效。
- **Don't** 用大量卡片取代一張可追蹤的架構圖。
