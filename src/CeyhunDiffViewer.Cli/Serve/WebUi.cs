namespace CeyhunDiffViewer.Cli.Serve;

/// <summary>
/// The web UI. Two entry points share the same CSS and diff renderer:
///   - <see cref="Html"/>: the interactive app (served by `serve`, scans + drills into assets).
///   - <see cref="StaticPage"/>: a self-contained single-diff page (opened by `difftool`).
/// </summary>
public static class WebUi
{
    public static readonly string Html = HtmlTemplate
        .Replace("/*CSS*/", Css)
        .Replace("/*SHARED*/", Shared)
        .Replace("/*APP*/", AppScript);

    public static string StaticPage(string diffJson, string title) => StaticTemplate
        .Replace("/*CSS*/", Css)
        .Replace("/*SHARED*/", Shared)
        .Replace("<!--TITLE-->", HtmlEscape(title))
        .Replace("/*DIFF*/", diffJson.Replace("</", "<\\/"));

    private static string HtmlEscape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private const string Css = """
:root{
  --bg:#0f1115; --panel:#161a21; --panel2:#1b2029; --border:#272d38;
  --text:#e6e9ef; --muted:#8b93a3; --accent:#5b9dff;
  --added:#3fb950; --deleted:#f85149; --modified:#5b9dff; --renamed:#bc8cff; --replaced:#e3a008;
  --before:#f8514922; --after:#3fb95022;
}
*{box-sizing:border-box}
body{margin:0;font:14px/1.5 -apple-system,BlinkMacSystemFont,"Segoe UI",Roboto,sans-serif;background:var(--bg);color:var(--text)}
header{padding:12px 16px;background:var(--panel);border-bottom:1px solid var(--border);display:flex;gap:10px;align-items:center;flex-wrap:wrap}
header .grow{flex:1}
label{font-size:12px;color:var(--muted);margin-right:4px}
input{background:var(--panel2);border:1px solid var(--border);color:var(--text);border-radius:6px;padding:6px 8px;font:inherit}
input#base,input#target{width:180px}
input#filter{width:150px}
button{background:var(--accent);color:#04122b;border:0;border-radius:6px;padding:7px 14px;font-weight:600;cursor:pointer}
button:hover{filter:brightness(1.08)}
#counts{color:var(--muted);font-size:12px}
main{display:grid;grid-template-columns:minmax(280px,32%) 1fr;height:calc(100vh - 56px)}
#list{overflow:auto;border-right:1px solid var(--border)}
#detail{overflow:auto;padding:16px}
.row{padding:10px 14px;border-bottom:1px solid var(--border);cursor:pointer}
.row:hover{background:var(--panel)}
.row.sel{background:var(--panel2)}
.row .name{font-weight:600}
.row .path{color:var(--muted);font-size:11px;word-break:break-all;margin-top:2px}
.badge{display:inline-block;font-size:10px;font-weight:700;text-transform:uppercase;letter-spacing:.03em;padding:2px 6px;border-radius:5px;margin-right:6px;vertical-align:middle}
.b-added{background:rgba(63,185,80,.15);color:var(--added)}
.b-deleted{background:rgba(248,81,73,.15);color:var(--deleted)}
.b-modified{background:rgba(91,157,255,.15);color:var(--modified)}
.b-renamed{background:rgba(188,140,255,.15);color:var(--renamed)}
.b-replaced{background:rgba(227,160,8,.15);color:var(--replaced)}
.hint{color:var(--muted);padding:24px}
.dhead{margin-bottom:14px}
.dhead h2{margin:0 0 4px;font-size:16px;word-break:break-all}
.dhead .sub{color:var(--muted);font-size:12px;word-break:break-all}
.card{background:var(--panel);border:1px solid var(--border);border-radius:8px;padding:12px 14px;margin-bottom:12px}
.card .k{font-size:11px;font-weight:700;text-transform:uppercase;letter-spacing:.04em}
.card .prop{font-family:ui-monospace,SFMono-Regular,Menlo,monospace;color:var(--accent);margin:4px 0}
.card .tgt{color:var(--muted);font-size:12px}
.val{font-family:ui-monospace,SFMono-Regular,Menlo,monospace;font-size:12px;padding:4px 8px;border-radius:5px;margin-top:4px;word-break:break-all}
.val.before{background:var(--before)}
.val.after{background:var(--after)}
.locs{margin:8px 0 0;padding:0;list-style:none}
.locs li{color:var(--muted);font-size:12px;padding:2px 0;word-break:break-all}
.locs li::before{content:"@ ";color:var(--accent)}
.err{color:var(--deleted);padding:16px;font-family:ui-monospace,monospace;white-space:pre-wrap}
""";

    // Shared helpers + the single-asset diff renderer. Used by both pages.
    private const string Shared = """
const $ = s => document.querySelector(s);
const esc = s => (s??"").replace(/[&<>"]/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;'}[c]));
const fileName = p => (p||"").split('/').pop();
function groupKey(c){ return [c.kind, c.object.type, c.propertyPath||'', c.before&&c.before.resolved||'', c.after&&c.after.resolved||'', c.target||''].join('¦'); }
function whereOf(o){ return o.location || o.hierarchy || o.name || ('&'+o.fileId); }
function labelOf(kind){ return ({documentAdded:'OBJECT ADDED',documentRemoved:'OBJECT REMOVED',fieldChanged:'FIELD CHANGED',overrideAdded:'OVERRIDE ADDED',overrideRemoved:'OVERRIDE REMOVED',overrideChanged:'OVERRIDE CHANGED'}[kind])||kind; }
function renderDiff(d){
  const detail = $('#detail');
  const p = d.targetPath || d.basePath;
  let html = `<div class="dhead"><h2>${esc(fileName(p)||d.guid||'asset')}</h2>
    <div class="sub"><span class="badge b-${d.status}">${d.status}</span>${esc(p||'')}</div>`;
  if(d.basePath && d.targetPath && d.basePath!==d.targetPath)
    html += `<div class="sub">renamed from ${esc(d.basePath)}</div>`;
  html += `</div>`;
  const changes = d.changes || [];
  if(!changes.length){
    html += `<div class="hint">${d.status==='added'?'Whole asset added.':d.status==='deleted'?'Whole asset deleted.':'No semantic changes (content identical or metadata-only).'}</div>`;
    detail.innerHTML = html; return;
  }
  const groups = new Map();
  for(const c of changes){
    const k = groupKey(c);
    if(!groups.has(k)) groups.set(k, {sample:c, items:[]});
    groups.get(k).items.push(c);
  }
  for(const g of groups.values()){
    const sample = g.sample, items = g.items;
    const kindClass = ({overrideAdded:'added',documentAdded:'added',overrideRemoved:'deleted',documentRemoved:'deleted'}[sample.kind])||'modified';
    html += `<div class="card">
      <div class="k b-${kindClass}" style="background:none;padding:0">${esc(labelOf(sample.kind))}${items.length>1?` <span class="count" style="color:var(--muted);font-weight:400">×${items.length}</span>`:''}</div>`;
    html += `<div class="tgt">[${esc(sample.object.type)}]${sample.target?` · ${esc(sample.target)}`:''}</div>`;
    if(sample.propertyPath) html += `<div class="prop">${esc(sample.propertyPath)}</div>`;
    if(sample.before) html += `<div class="val before">${esc(sample.before.resolved)}</div>`;
    if(sample.after) html += `<div class="val after">${esc(sample.after.resolved)}</div>`;
    html += `<ul class="locs">${items.map(c=>`<li>${esc(whereOf(c.object))}</li>`).join('')}</ul>`;
    html += `</div>`;
  }
  detail.innerHTML = html;
}
""";

    // Interactive-app-only logic (scan, list, drill-down, ref pickers, prefs).
    private const string AppScript = """
const base = () => $('#base').value.trim();
const target = () => $('#target').value.trim();
async function loadRefs(){
  try{
    const refs = await (await fetch('/api/refs')).json();
    $('#refs').innerHTML = refs.map(r => `<option value="${esc(r.value)}">${esc(r.kind)}${r.label? ': '+esc(r.label):''}</option>`).join('');
  }catch(e){}
}
function savePrefs(){
  try{ localStorage.setItem('cdv.base',base()); localStorage.setItem('cdv.target',target()); localStorage.setItem('cdv.filter',$('#filter').value.trim()); }catch(e){}
}
async function scan(){
  savePrefs();
  const list = $('#list');
  list.innerHTML = '<div class="hint">Scanning…</div>';
  $('#counts').textContent = '';
  $('#detail').innerHTML = '<div class="hint">Select an asset to see its semantic diff.</div>';
  try{
    const url = `/api/scan?base=${encodeURIComponent(base())}&target=${encodeURIComponent(target())}&filter=${encodeURIComponent($('#filter').value.trim())}`;
    const res = await fetch(url);
    if(!res.ok) throw new Error((await res.json()).error || res.statusText);
    renderList(await res.json());
  }catch(e){ list.innerHTML = `<div class="err">${esc(e.message)}</div>`; }
}
function renderList(data){
  const list = $('#list');
  const assets = data.assets || [];
  $('#counts').textContent = Object.entries(data.counts||{}).map(([k,v])=>`${k}: ${v}`).join('   ');
  if(!assets.length){ list.innerHTML = '<div class="hint">No matching asset changes.</div>'; return; }
  list.innerHTML = assets.map((a,i)=>{
    const p = a.targetPath || a.basePath;
    return `<div class="row" data-i="${i}" onclick="pick(${i})">
      <div class="name"><span class="badge b-${a.status}">${a.status}</span>${esc(fileName(p))}</div>
      <div class="path">${esc(p)}</div></div>`;
  }).join('');
  window.__assets = assets;
}
async function pick(i){
  document.querySelectorAll('.row').forEach(r=>r.classList.toggle('sel', +r.dataset.i===i));
  const a = window.__assets[i];
  const detail = $('#detail');
  detail.innerHTML = '<div class="hint">Loading diff…</div>';
  try{
    const p = a.targetPath || a.basePath;
    const url = `/api/diff?base=${encodeURIComponent(base())}&target=${encodeURIComponent(target())}&path=${encodeURIComponent(p)}`;
    const res = await fetch(url);
    if(!res.ok) throw new Error((await res.json()).error || res.statusText);
    renderDiff(await res.json());
  }catch(e){ detail.innerHTML = `<div class="err">${esc(e.message)}</div>`; }
}
['base','target','filter'].forEach(id => document.getElementById(id).addEventListener('keydown', e => { if(e.key==='Enter') scan(); }));
(function initPrefs(){
  try{
    const b=localStorage.getItem('cdv.base'), t=localStorage.getItem('cdv.target'), f=localStorage.getItem('cdv.filter');
    if(b) $('#base').value=b; if(t) $('#target').value=t; if(f) $('#filter').value=f;
  }catch(e){}
})();
loadRefs();
scan();
""";

    private const string HtmlTemplate = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>CeyhunDiffViewer</title>
<style>/*CSS*/</style>
</head>
<body>
<header>
  <span style="font-weight:700">CeyhunDiffViewer</span>
  <span><label for="base">base</label><input id="base" list="refs" value="HEAD~1"></span>
  <span><label for="target">target</label><input id="target" list="refs" value="HEAD"></span>
  <span><label for="filter">filter</label><input id="filter" value="*.prefab"></span>
  <datalist id="refs"></datalist>
  <button onclick="scan()">Scan</button>
  <span class="grow"></span>
  <span id="counts"></span>
</header>
<main>
  <div id="list"><div class="hint">Pick two refs and hit Scan.</div></div>
  <div id="detail"><div class="hint">Select an asset to see its semantic diff.</div></div>
</main>
<script>
/*SHARED*/
/*APP*/
</script>
</body>
</html>
""";

    private const string StaticTemplate = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title><!--TITLE--> — CeyhunDiffViewer</title>
<style>/*CSS*/
main{display:block;height:auto}
#detail{max-width:960px;margin:0 auto}
</style>
</head>
<body>
<header>
  <span style="font-weight:700">CeyhunDiffViewer</span>
  <span style="color:var(--muted)"><!--TITLE--></span>
</header>
<main><div id="detail"></div></main>
<script>
/*SHARED*/
renderDiff(/*DIFF*/);
</script>
</body>
</html>
""";
}
