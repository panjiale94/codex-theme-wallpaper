const fs=require('fs');
const path=require('path');
process.chdir(path.resolve(__dirname, '..'));
const profile=process.env.CODEX_SKIN_PROFILE||'work/cdp-probe-profile';
const port=fs.readFileSync(profile+'/DevToolsActivePort','utf8').split('\n')[0].trim();
let id=0; const pending=new Map();
(async()=>{
const targets=await(await fetch(`http://127.0.0.1:${port}/json/list`)).json();
const target=process.argv[2]==='close-browser'?await(await fetch(`http://127.0.0.1:${port}/json/version`)).json():targets.find(t=>t.url==='app://-/index.html');
if(!target)throw Error('No app target');
const ws=new WebSocket(target.webSocketDebuggerUrl);
await new Promise((res,rej)=>{ws.onopen=res;ws.onerror=rej;});
ws.onmessage=e=>{const m=JSON.parse(e.data);if(m.id&&pending.has(m.id)){const p=pending.get(m.id);pending.delete(m.id);m.error?p.reject(m.error):p.resolve(m.result);}};
ws.onclose=()=>{for(const p of pending.values())p.reject(Error('Socket closed'));pending.clear();};
function send(method,params={}){return new Promise((resolve,reject)=>{const n=++id;pending.set(n,{resolve,reject});ws.send(JSON.stringify({id:n,method,params}));});}
if(process.argv[2]==='close-browser') {
 await send('Browser.close').catch(()=>{}); console.log('Closed isolated test Codex');
}else if(process.argv[2]==='inspect'){
 console.log(JSON.stringify(await send('Runtime.evaluate',{expression:`JSON.stringify({url:location.href,roots:[...document.body.children].map(x=>({tag:x.tagName,id:x.id,cls:x.className})),nodes:[...document.querySelectorAll('main,aside,[data-testid],#root > div,body > div')].slice(0,70).map(x=>({tag:x.tagName,id:x.id,cls:x.className,test:x.getAttribute('data-testid'),bg:getComputedStyle(x).backgroundColor})),vars:[...getComputedStyle(document.documentElement)].filter(x=>x.startsWith('--')&&/bg|background|surface/.test(x)).map(x=>[x,getComputedStyle(document.documentElement).getPropertyValue(x)])})`,returnByValue:true}),null,2));
} else if(process.argv[2]==='eval'){
 console.log(JSON.stringify(await send('Runtime.evaluate',{expression:fs.readFileSync(process.argv[3],'utf8'),returnByValue:true,awaitPromise:true}),null,2));
} else if(process.argv[2]==='front') {
 try {
  const window=await send('Browser.getWindowForTarget',{targetId:target.id});
  await send('Browser.setWindowBounds',{windowId:window.windowId,bounds:{windowState:'normal'}});
 } catch { /* Electron 的部分窗口不暴露 Browser 窗口控制域。 */ }
 await send('Page.bringToFront'); console.log('Brought test Codex to front');
} else if(process.argv[2]==='reload') {
 await send('Page.reload'); console.log('Reloaded test Codex');
} else if(process.argv[2]==='screenshot'){
await send('Input.dispatchMouseEvent',{type:'mouseMoved',x:700,y:200});
await new Promise(r=>setTimeout(r,150));
const r=await send('Page.captureScreenshot',{format:'png'});fs.writeFileSync('work/codex-internal-test.png',Buffer.from(r.data,'base64'));console.log('Saved screenshot');
}
ws.close();
})().catch(e=>{console.error(e);process.exit(1);});
