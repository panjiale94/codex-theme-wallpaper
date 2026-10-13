(function () {
  if (location.protocol !== 'app:' || location.search.includes('avatar-overlay')) return;
  const key = '__codexDreamSkin';
  if (window[key]?.version === 4) return;
  if (window[key]) window[key].restore();
  let originalTheme;
  let observer;
  let host;
  let style;
  let config;
  let mediaAbort;
  let stopEffects;
  let effectKey;
  const css = `
html[data-dream-skin] { color-scheme: dark; }
html[data-dream-skin], html[data-dream-skin] body { background: #10151e !important; }
html[data-dream-skin] #root { position: relative; z-index: 1; background: transparent !important; }
#codex-dream-background { position: fixed; inset: 0; z-index: 0; overflow: hidden; pointer-events: none; background: #0a111d; }
#codex-dream-background .dream-media { position:absolute;inset:-3%;will-change:transform; }
#codex-dream-background img, #codex-dream-background video { width:100%; height:100%; object-fit:cover; }
#codex-dream-background .dream-living-art { position:absolute;inset:0;width:100%;height:100%; }
#codex-dream-background .dream-art { animation:dream-drift 18s ease-in-out infinite alternate; }
#codex-dream-background[data-motion-zero] .dream-art { animation-play-state:paused; }
#codex-dream-background::after { content:''; position:absolute; inset:0; z-index:5;background:rgba(3,9,18,var(--dream-shade,.12)); }
#codex-dream-background .dream-effects { position:absolute;inset:0;width:100%;height:100%;z-index:3; }
#codex-dream-background .dream-atmosphere { position:absolute;inset:0;z-index:2;opacity:var(--dream-motion,.7);animation:dream-breathe 7s ease-in-out infinite alternate; }
#codex-dream-background .dream-atmosphere-fire { background:radial-gradient(ellipse at 88% 88%,#ff690033,transparent 42%),radial-gradient(ellipse at 8% 10%,#ffaa2925,transparent 30%); }
#codex-dream-background .dream-atmosphere-moon { background:radial-gradient(ellipse at 78% 8%,#7deeff33,transparent 48%),linear-gradient(140deg,transparent 35%,#9fcaff18 45%,transparent 58%); }
#codex-dream-background .dream-atmosphere-sakura { background:radial-gradient(ellipse at 60% 90%,#c467aa28,transparent 60%),radial-gradient(ellipse at 5% 8%,#ffb27720,transparent 40%); }
#codex-dream-background .dream-atmosphere-crimson { background:radial-gradient(ellipse at 85% 98%,#ff5b0a50,transparent 55%),radial-gradient(ellipse at 70% 5%,#dc161730,transparent 43%); }
#codex-dream-background .dream-water { position:absolute;left:-15%;right:-15%;bottom:-5%;height:28%;z-index:2;mix-blend-mode:screen;opacity:calc(var(--dream-motion,.7)*.35);background:repeating-radial-gradient(ellipse at 70% 100%,transparent 0px,#9ce4eb40 2px,transparent 5px,transparent 42px);filter:blur(2px);animation:dream-water 12s ease-in-out infinite alternate; }
#codex-dream-background .dream-water-fire, #codex-dream-background .dream-water-crimson { background:repeating-radial-gradient(ellipse at 75% 100%,transparent 0px,#ffc27340 2px,transparent 5px,transparent 44px); }
#codex-dream-background .dream-water-sakura { background:repeating-radial-gradient(ellipse at 75% 100%,transparent 0px,#ffc4e040 2px,transparent 5px,transparent 46px); }
@keyframes dream-breathe { from {filter:brightness(.75);transform:translateY(0)} to {filter:brightness(1.3);transform:translateY(-1%)} }
@keyframes dream-water { from {transform:scaleX(1) translateY(0)} to {transform:scaleX(1.08) translateY(15px)} }
#codex-dream-background .dream-aurora { position:absolute; inset:-25%; background:radial-gradient(ellipse at 20% 30%,#0476a899,transparent 50%),radial-gradient(ellipse at 75% 60%,#723ec388,transparent 50%),radial-gradient(ellipse at 45% 80%,#059778aa,transparent 50%),#081222; animation:dream-drift 24s ease-in-out infinite alternate; }
@keyframes dream-drift { from {transform:scale(1.05) translateX(-1%)} to {transform:scale(1.12) translateX(1%)} }
html[data-dream-skin] [class*='_PageSurface_'],
html[data-dream-skin] [class*='_Layout_'],
html[data-dream-skin] [class*='_ContentClip_'] { background:transparent !important; }
html[data-dream-skin] [class*='_MainContentSurface_'] { background:rgba(10,16,26,var(--dream-panel,.46)) !important; }
html[data-dream-skin] .app-shell-left-panel,
html[data-dream-skin] [data-slate-sidebar-content] { background:rgba(10,16,26,var(--dream-sidebar,.34)) !important; }
html[data-dream-skin] [class*='_NavigationRail'] { background:rgba(10,16,26,.70) !important; }
html[data-dream-skin] { --color-surface:rgba(15,22,34,var(--dream-panel,.46)) !important; --app-color-background-shell:transparent !important; --app-color-background-surface:transparent !important; --app-color-background-surface-under:transparent !important; --color-token-bg-primary:transparent !important; --color-token-main-surface-primary:transparent !important; --chat-background-color:transparent !important; --color-token-side-bar-background:transparent !important; --codex-base-surface:transparent !important; }
html[data-dream-skin] [class*='_ComposerLayoutRoot_'] { background:rgba(15,22,34,.84) !important; }
`;
  function ensureTheme() {
    if (!config) return;
    if (document.documentElement.getAttribute('data-theme') !== 'dark')
      document.documentElement.setAttribute('data-theme', 'dark');
  }
  function apply(next) {
    config = next;
    if (!document.body || !document.getElementById('root')) {
      document.addEventListener('DOMContentLoaded', () => apply(next), {once:true});
      return;
    }
    if (originalTheme === undefined) originalTheme = document.documentElement.getAttribute('data-theme');
    ensureTheme();
    if (!style) {
      style = document.createElement('style'); style.id='codex-dream-style'; style.textContent=css; document.head.append(style);
      observer = new MutationObserver(ensureTheme);
      observer.observe(document.documentElement, {attributes:true,attributeFilter:['data-theme']});
    }
    document.documentElement.setAttribute('data-dream-skin','');
    style.textContent = css + `html[data-dream-skin]{--dream-panel:${next.panel};--dream-sidebar:${Math.max(.15,next.panel-.12)};--dream-shade:${next.shade};}`;
    if (!host) {
      host=document.createElement('div'); host.id='codex-dream-background'; document.body.prepend(host);
    }
    if (host.dataset.source !== next.mediaId) {
      stopEffects?.();stopEffects=null;effectKey=null;
      mediaAbort?.abort(); mediaAbort=new AbortController();
      delete host.dataset.error;
      const oldVideo = host.querySelector('video'); if (oldVideo) { oldVideo.pause(); oldVideo.removeAttribute('src'); oldVideo.load(); }
      host.replaceChildren();
      const media=document.createElement('div');media.className='dream-media';host.append(media);
      if (next.kind === 'aurora') {
        const glow=document.createElement('div'); glow.className='dream-aurora'; media.append(glow);
      } else if (next.kind === 'video') {
        const video=document.createElement('video'); video.autoplay=true; video.loop=true; video.muted=true; video.playsInline=true; video.src=next.source;
        video.addEventListener('error',()=>host.dataset.error=String(video.error?.code||'decode'),{signal:mediaAbort.signal});
        media.append(video); video.play().catch(()=>{});
      } else {
        const img=document.createElement('img'); img.src=next.source; img.className=next.kind==='art'?'dream-art':'';
        img.addEventListener('error',()=>host.dataset.error='image',{signal:mediaAbort.signal});media.append(img);
      }
      host.dataset.source = next.mediaId;
    }
    const motion=Math.max(0,Math.min(1,next.motion??.7));
    host.style.setProperty('--dream-motion',motion);host.toggleAttribute('data-motion-zero',motion===0);
    const desiredEffect=(next.effect||'none')+':'+motion;
    if(effectKey !== desiredEffect) {
      stopEffects?.();stopEffects=null;
      if(next.effect && next.effect!=='none' && motion>0 && window.__codexDreamEffects)
        stopEffects=window.__codexDreamEffects(host,next.effect,motion);
      effectKey=desiredEffect;
    }
    host.dataset.effect=next.effect||'none';
    return {applied:true,backgroundInsideApp:true};
  }
  function restore() {
    config = null;
    stopEffects?.();stopEffects=null;
    observer?.disconnect(); observer=null;
    mediaAbort?.abort();mediaAbort=null;
    const video=host?.querySelector('video'); if(video){video.pause();video.removeAttribute('src');video.load();}
    host?.remove();host=null; style?.remove();style=null;
    document.documentElement.removeAttribute('data-dream-skin');
    if(originalTheme !== undefined) {
      if(originalTheme === null) document.documentElement.removeAttribute('data-theme');
      else document.documentElement.setAttribute('data-theme',originalTheme);
    }
    originalTheme=undefined;
    delete window[key];
    return {restored:true};
  }
  window[key]={apply,restore,version:4};
})();
