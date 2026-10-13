const fs = require('fs');
const path = require('path');
const {spawnSync} = require('child_process');
const projectRoot = path.resolve(__dirname, '..');
process.chdir(projectRoot);
const profile = path.resolve('work/cdp-probe-profile');
const port = fs.readFileSync(path.join(profile, 'DevToolsActivePort'), 'utf8').split('\n')[0].trim();
const folder = path.resolve('work/preview-frames');
fs.mkdirSync(folder, {recursive:true});
const fps = 12, duration = 4;
const presets = [['center-shrine','神社双焰','shrine'],['center-storm','天雷夜雨','storm'],['center-tide','月海潮汐','tide'],['center-eclipse','赤月轮舞','eclipse']];
(async () => {
  const targets = await (await fetch(`http://127.0.0.1:${port}/json/list`)).json();
  const target = targets.find(t => t.url === 'app://-/index.html');
  const ws = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((resolve,reject) => {ws.onopen=resolve; ws.onerror=reject;});
  const pending = new Map(); let id=0, frame=0;
  ws.onmessage = e => {const r=JSON.parse(e.data); if(pending.has(r.id)){const p=pending.get(r.id); pending.delete(r.id); r.error?p.reject(r.error):p.resolve(r.result);}};
  function send(method,params={}) {return new Promise((resolve,reject)=>{const n=++id; pending.set(n,{resolve,reject}); ws.send(JSON.stringify({id:n,method,params}));});}
  async function evaluate(expression) {
    const result = await send('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true});
    if(result.exceptionDetails)throw Error(JSON.stringify(result.exceptionDetails));
    return result.result.value;
  }
  try {
    await send('Page.bringToFront');
    await send('Emulation.setDeviceMetricsOverride',{width:1280,height:720,deviceScaleFactor:1,mobile:false});
    await evaluate(fs.readFileSync('work/CodexDreamSkin/EffectRuntime.js','utf8')+'\n'+fs.readFileSync('work/CodexDreamSkin/SkinRuntime.js','utf8'));
    await evaluate(`document.getElementById('root').style.display='none';
      var label=document.createElement('div');label.id='dream-preview-label';
      label.style='position:fixed;left:32px;bottom:28px;z-index:10;color:white;font:600 22px system-ui;padding:12px 20px;border-radius:12px;background:#091220b0;pointer-events:none;';document.body.append(label);`);
    for(const [preset,title,effect] of presets) {
      const source='data:image/png;base64,'+fs.readFileSync('work/CodexDreamSkin/Assets/'+preset+'.png').toString('base64');
      const config={kind:'art',source,mediaId:preset,panel:0,shade:.04,effect,motion:1};
      await evaluate('window.__codexDreamSkin.apply('+JSON.stringify(config)+');document.getElementById("dream-preview-label").textContent='+JSON.stringify('不知火 · '+title+'  /  强动效 100%')+';');
      await new Promise(r=>setTimeout(r,900));
      const state=JSON.parse(await evaluate(`JSON.stringify({living:document.querySelector('.dream-effects').dataset.livingArt,hidden:document.hidden,elapsed:document.querySelector('.dream-effects').dataset.elapsed})`));
      if(state.hidden||state.living!=='gpu') throw Error('Preview animation unavailable: '+JSON.stringify(state));
      console.log('Recording '+title);
      const start=Date.now();
      for(let i=0;i<fps*duration;i++) {
        const wait=start+i*1000/fps-Date.now();if(wait>0)await new Promise(r=>setTimeout(r,wait));
        const result=await send('Page.captureScreenshot',{format:'jpeg',quality:90});
        fs.writeFileSync(path.join(folder,String(frame++).padStart(5,'0')+'.jpg'),Buffer.from(result.data,'base64'));
      }
    }
  } finally {
    await evaluate('window.__codexDreamSkin?.restore();document.getElementById("root").style.display="";document.getElementById("dream-preview-label")?.remove();').catch(()=>{});
    await send('Emulation.clearDeviceMetricsOverride').catch(()=>{});ws.close();
  }
  const ffmpeg=path.join(projectRoot, 'tools', 'ffmpeg', 'ffmpeg.exe');
  const output=path.resolve('outputs/CodexDreamSkin-v4/四款强动效预览.mp4');
  const result=spawnSync(ffmpeg,['-y','-hide_banner','-loglevel','error','-framerate',String(fps),'-i',path.join(folder,'%05d.jpg'),'-frames:v',String(frame),'-c:v','libx264','-preset','fast','-crf','19','-pix_fmt','yuv420p','-movflags','+faststart',output],{encoding:'utf8'});
  if(result.status!==0)throw Error(result.stderr||'ffmpeg failed');
  console.log(output);
})().catch(error=>{console.error(error);process.exitCode=1;});
