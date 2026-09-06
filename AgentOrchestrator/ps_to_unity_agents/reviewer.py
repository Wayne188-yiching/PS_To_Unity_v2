from __future__ import annotations

import html
import json
from datetime import datetime, timezone
from http import HTTPStatus
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Any
from urllib.parse import parse_qs, quote, urlparse

from .models import PipelineRequest


DECISIONS_FILE = "review_decisions.json"
VALID_DECISIONS = {"approved", "rejected", "unsure"}
VALID_MODES = {"simple", "sliced", "mask", "scroll", "layout_group"}


def load_manifest(request: PipelineRequest) -> dict[str, Any]:
    path = request.output_folder / "semantic_manifest.json"
    if not path.is_file():
        raise FileNotFoundError(f"Semantic manifest not found: {path}")
    return json.loads(path.read_text(encoding="utf-8-sig"))


def load_decisions(output_folder: Path) -> dict[str, Any]:
    path = output_folder / DECISIONS_FILE
    if not path.is_file():
        return {"schemaVersion": "1.0", "decisions": {}}
    return json.loads(path.read_text(encoding="utf-8-sig"))


def save_decision(output_folder: Path, manifest: dict[str, Any], payload: dict[str, Any]) -> dict[str, Any]:
    node_path = str(payload.get("nodePath") or "")
    entries = {entry["nodePath"]: entry for entry in manifest.get("entries") or []}
    if node_path not in entries:
        raise ValueError("Unknown nodePath.")

    decision = str(payload.get("decision") or "")
    mode = str(payload.get("mode") or "")
    if decision not in VALID_DECISIONS:
        raise ValueError("Invalid decision.")
    if mode and mode not in VALID_MODES:
        raise ValueError("Invalid render mode.")
    if decision == "approved" and not mode:
        raise ValueError("Approved items require a render mode.")

    raw_border = payload.get("spriteBorder") or {}
    border = {}
    for side in ("left", "top", "right", "bottom"):
        try:
            value = float(raw_border.get(side) or 0)
        except (TypeError, ValueError) as error:
            raise ValueError("Sprite borders must be numbers.") from error
        if value < 0:
            raise ValueError("Sprite borders cannot be negative.")
        border[side] = value

    entry = entries[node_path]
    size = entry["assetPixelSize"]
    if decision == "approved" and mode == "sliced":
        if border["left"] + border["right"] > size["width"]:
            raise ValueError("Left and right borders exceed the asset width.")
        if border["top"] + border["bottom"] > size["height"]:
            raise ValueError("Top and bottom borders exceed the asset height.")

    result = load_decisions(output_folder)
    result["schemaVersion"] = "1.0"
    result["caseId"] = manifest.get("caseId")
    result["updatedAt"] = datetime.now(timezone.utc).isoformat()
    result.setdefault("decisions", {})[node_path] = {
        "decision": decision,
        "mode": mode or None,
        "spriteBorder": border,
        "note": str(payload.get("note") or "").strip(),
    }
    output_folder.mkdir(parents=True, exist_ok=True)
    destination = output_folder / DECISIONS_FILE
    temporary = destination.with_suffix(".tmp")
    temporary.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    temporary.replace(destination)
    return result["decisions"][node_path]


def _proposal_mode(entry: dict[str, Any]) -> str:
    mode = str((entry.get("proposal") or {}).get("mode") or "")
    return "simple" if mode == "simple_stretch" else mode if mode in VALID_MODES else ""


def render_page(manifest: dict[str, Any], decisions: dict[str, Any]) -> str:
    review_entries = [entry for entry in manifest.get("entries") or [] if entry.get("status") == "NEEDS_REVIEW"]
    saved = decisions.get("decisions") or {}
    cards = []
    for index, entry in enumerate(review_entries, 1):
        node_path = str(entry["nodePath"])
        asset = str(entry["assetPath"])
        asset_size = entry["assetPixelSize"]
        target_size = entry["expectedUnityRectSize"]
        proposal = entry.get("proposal") or {}
        proposal_mode = str(proposal.get("mode") or "unresolved")
        proposal_border = proposal.get("border") or {"left": 0, "top": 0, "right": 0, "bottom": 0}
        state = saved.get(node_path) or {}
        selected_mode = state.get("mode") or _proposal_mode(entry)
        border = state.get("spriteBorder") or proposal_border
        options = ['<option value="">請選擇</option>']
        for value, label in (
            ("simple", "Simple／普通縮放"),
            ("sliced", "Sliced／九宮格"),
            ("mask", "Mask／遮罩"),
            ("scroll", "Scroll／捲動區"),
            ("layout_group", "LayoutGroup／自動排版"),
        ):
            selected = " selected" if selected_mode == value else ""
            options.append(f'<option value="{value}"{selected}>{label}</option>')
        status = str(state.get("decision") or "")
        cards.append(f"""
        <article class="card" data-node="{html.escape(node_path, quote=True)}"
                 data-width="{asset_size['width']}" data-height="{asset_size['height']}"
                 data-saved="{html.escape(status, quote=True)}">
          <div class="card-head"><span class="number">{index}</span><code>{html.escape(node_path)}</code><span class="saved-label"></span></div>
          <div class="content">
            <div class="preview-wrap">
              <div class="preview">
                <img src="/asset?name={quote(asset)}" alt="{html.escape(asset, quote=True)}">
                <i class="slice-line vertical left"></i><i class="slice-line vertical right"></i>
                <i class="slice-line horizontal top"></i><i class="slice-line horizontal bottom"></i>
              </div>
              <div class="filename">{html.escape(asset)}</div>
            </div>
            <div class="details">
              <div class="sizes"><span>原圖 <b>{asset_size['width']} × {asset_size['height']}</b></span><span>PS／Unity 目標 <b>{target_size['width']} × {target_size['height']}</b></span></div>
              <div class="proposal">Agent 建議：<b>{html.escape(proposal_mode)}</b></div>
              <label>實作方式<select class="mode">{''.join(options)}</select></label>
              <div class="borders">
                <label>左<input class="border" data-side="left" type="number" min="0" value="{border.get('left', 0)}"></label>
                <label>上<input class="border" data-side="top" type="number" min="0" value="{border.get('top', 0)}"></label>
                <label>右<input class="border" data-side="right" type="number" min="0" value="{border.get('right', 0)}"></label>
                <label>下<input class="border" data-side="bottom" type="number" min="0" value="{border.get('bottom', 0)}"></label>
              </div>
              <label>備註<textarea class="note" rows="2" placeholder="例如：左右固定，中間可以拉長">{html.escape(str(state.get('note') or ''))}</textarea></label>
              <div class="actions">
                <button data-decision="approved" class="approve">同意</button>
                <button data-decision="rejected" class="reject">不同意</button>
                <button data-decision="unsure" class="unsure">不確定</button>
              </div>
              <div class="message"></div>
            </div>
          </div>
        </article>""")

    safe_case_id = html.escape(str(manifest.get("caseId") or ""))
    return f"""<!doctype html>
<html lang="zh-Hant"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>PS → Unity 語意複查</title>
<style>
:root{{--bg:#101217;--panel:#191d25;--line:#303746;--text:#eef2f8;--muted:#aeb8c8;--blue:#63a8ff;--green:#51d19a;--red:#ff7c83;--yellow:#efc56a}}
*{{box-sizing:border-box}}body{{margin:0;background:var(--bg);color:var(--text);font:15px/1.5 system-ui,"Microsoft JhengHei",sans-serif}}
header{{position:sticky;top:0;z-index:5;background:#101217ee;border-bottom:1px solid var(--line);padding:18px max(24px,calc((100vw - 1180px)/2))}}
h1{{font-size:22px;margin:0 0 5px}}header p{{margin:0;color:var(--muted)}}#progress{{color:var(--blue);font-weight:700}}
main{{max-width:1180px;margin:auto;padding:24px}}.card{{background:var(--panel);border:1px solid var(--line);border-radius:14px;margin-bottom:18px;overflow:hidden}}
.card-head{{display:flex;gap:12px;align-items:center;padding:13px 16px;border-bottom:1px solid var(--line)}}.card-head code{{overflow-wrap:anywhere}}.number{{background:#283345;border-radius:50%;min-width:28px;height:28px;text-align:center;line-height:28px}}.saved-label{{margin-left:auto;font-weight:700}}
.content{{display:grid;grid-template-columns:minmax(280px,42%) 1fr;gap:22px;padding:18px}}.preview-wrap{{min-width:0}}.preview{{position:relative;display:grid;place-items:center;height:300px;background:#0a0c10 url("data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg' width='20' height='20'%3E%3Cpath fill='%231a1e27' d='M0 0h10v10H0zm10 10h10v10H10z'/%3E%3C/svg%3E");overflow:hidden}}
.preview img{{max-width:100%;max-height:100%;object-fit:contain}}.filename{{color:var(--muted);padding-top:6px;overflow-wrap:anywhere}}.slice-line{{display:none;position:absolute;background:#ff4ecb;box-shadow:0 0 2px #000}}.slice-line.vertical{{top:0;bottom:0;width:2px}}.slice-line.horizontal{{left:0;right:0;height:2px}}
.sizes{{display:flex;gap:12px;flex-wrap:wrap}}.sizes span,.proposal{{background:#232936;padding:8px 10px;border-radius:8px}}.proposal{{margin:12px 0}}label{{display:block;color:var(--muted);margin-top:10px}}select,input,textarea{{width:100%;margin-top:5px;background:#0f1218;color:var(--text);border:1px solid #3a4354;border-radius:7px;padding:8px}}.borders{{display:grid;grid-template-columns:repeat(4,1fr);gap:8px}}
.actions{{display:flex;gap:8px;margin-top:14px}}button{{border:0;border-radius:8px;padding:9px 18px;font-weight:700;cursor:pointer}}.approve{{background:var(--green)}}.reject{{background:var(--red)}}.unsure{{background:var(--yellow)}}.message{{height:22px;padding-top:5px;color:var(--muted)}}
.card[data-saved="approved"]{{border-color:var(--green)}}.card[data-saved="rejected"]{{border-color:var(--red)}}.card[data-saved="unsure"]{{border-color:var(--yellow)}}
@media(max-width:760px){{.content{{grid-template-columns:1fr}}.preview{{height:240px}}.borders{{grid-template-columns:repeat(2,1fr)}}}}
</style></head><body>
<header><h1>PS → Unity 語意複查</h1><p>案例：{safe_case_id}　<span id="progress"></span>　決定只存本機，不會直接生成 Prefab。</p></header>
<main>{''.join(cards) if cards else '<p>目前沒有待複查項目。</p>'}</main>
<script>
const cards=[...document.querySelectorAll('.card')];
const labels={{approved:'已同意',rejected:'不同意',unsure:'不確定'}};
function updateProgress(){{
  const saved=cards.filter(card=>card.dataset.saved).length;
  document.querySelector('#progress').textContent=`已處理 ${{saved}} / ${{cards.length}}`;
  cards.forEach(card=>card.querySelector('.saved-label').textContent=labels[card.dataset.saved]||'待處理');
}}
function updateSlice(card){{
  const sliced=card.querySelector('.mode').value==='sliced';
  const width=Number(card.dataset.width),height=Number(card.dataset.height);
  const value=side=>Number(card.querySelector(`[data-side="${{side}}"]`).value)||0;
  const map={{left:['left',value('left')/width*100],right:['right',value('right')/width*100],top:['top',value('top')/height*100],bottom:['bottom',value('bottom')/height*100]}};
  for(const [side,[property,percent]] of Object.entries(map)){{
    const line=card.querySelector(`.slice-line.${{side}}`); line.style.display=sliced?'block':'none'; line.style[property]=`${{Math.min(100,percent)}}%`;
  }}
}}
async function save(card,decision){{
  const mode=card.querySelector('.mode').value;
  const message=card.querySelector('.message');
  if(decision==='approved'&&!mode){{message.textContent='請先選擇實作方式。';return}}
  const spriteBorder={{}}; card.querySelectorAll('.border').forEach(input=>spriteBorder[input.dataset.side]=Number(input.value)||0);
  message.textContent='儲存中…';
  const response=await fetch('/api/decision',{{method:'POST',headers:{{'Content-Type':'application/json'}},body:JSON.stringify({{nodePath:card.dataset.node,decision,mode,spriteBorder,note:card.querySelector('.note').value}})}});
  const result=await response.json();
  if(!response.ok){{message.textContent=result.error||'儲存失敗';return}}
  card.dataset.saved=decision; message.textContent='已儲存'; updateProgress();
}}
cards.forEach(card=>{{card.querySelectorAll('.mode,.border').forEach(input=>input.addEventListener('input',()=>updateSlice(card)));card.querySelectorAll('button').forEach(button=>button.addEventListener('click',()=>save(card,button.dataset.decision)));updateSlice(card)}});
updateProgress();
</script></body></html>"""


def serve_review(request: PipelineRequest, port: int = 8765) -> None:
    manifest = load_manifest(request)
    asset_folder = Path(manifest["assetFolder"]).resolve()
    allowed_assets = {str(entry["assetPath"]): (asset_folder / str(entry["assetPath"])).resolve() for entry in manifest.get("entries") or []}

    class Handler(BaseHTTPRequestHandler):
        def do_GET(self) -> None:  # noqa: N802
            parsed = urlparse(self.path)
            if parsed.path == "/":
                body = render_page(manifest, load_decisions(request.output_folder)).encode("utf-8")
                self._send(HTTPStatus.OK, "text/html; charset=utf-8", body)
                return
            if parsed.path == "/asset":
                name = (parse_qs(parsed.query).get("name") or [""])[0]
                path = allowed_assets.get(name)
                if path and path.is_file():
                    content_type = "image/jpeg" if path.suffix.casefold() in {".jpg", ".jpeg"} else "image/png"
                    self._send(HTTPStatus.OK, content_type, path.read_bytes())
                    return
            self._json(HTTPStatus.NOT_FOUND, {"error": "Not found."})

        def do_POST(self) -> None:  # noqa: N802
            if urlparse(self.path).path != "/api/decision":
                self._json(HTTPStatus.NOT_FOUND, {"error": "Not found."})
                return
            try:
                length = int(self.headers.get("Content-Length") or 0)
                if length <= 0 or length > 64 * 1024:
                    raise ValueError("Invalid request size.")
                payload = json.loads(self.rfile.read(length))
                saved = save_decision(request.output_folder, manifest, payload)
                self._json(HTTPStatus.OK, {"saved": saved})
            except (json.JSONDecodeError, ValueError) as error:
                self._json(HTTPStatus.BAD_REQUEST, {"error": str(error)})

        def log_message(self, format: str, *args: Any) -> None:
            return

        def _json(self, status: HTTPStatus, payload: dict[str, Any]) -> None:
            self._send(status, "application/json; charset=utf-8", json.dumps(payload, ensure_ascii=False).encode("utf-8"))

        def _send(self, status: HTTPStatus, content_type: str, body: bytes) -> None:
            self.send_response(status)
            self.send_header("Content-Type", content_type)
            self.send_header("Content-Length", str(len(body)))
            self.send_header("Cache-Control", "no-store")
            self.end_headers()
            self.wfile.write(body)

    server = ThreadingHTTPServer(("127.0.0.1", port), Handler)
    print(f"Review page: http://127.0.0.1:{port}")
    print(f"Decisions: {request.output_folder / DECISIONS_FILE}")
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()
