(function () {
  if (window.__codexDreamEffects?.version === 4) return;
  const factory = function (host, effect, motion) {
    const abort = new AbortController();
    const canvas = document.createElement('canvas');
    canvas.className = 'dream-effects'; canvas.dataset.effect = effect; host.append(canvas);
    const ctx = canvas.getContext('2d', {alpha: true});
    if (!ctx) { canvas.remove(); return () => abort.abort(); }
    const centered = ['shrine', 'storm', 'tide', 'eclipse'].includes(effect);
    const storm = effect === 'storm', tide = effect === 'tide';
    const cold = ['moon', 'snow', 'aurora', 'storm', 'tide'].includes(effect);
    const palette = cold ? ['#65e9ff', '#b5ccff', '#e1ffff'] : effect === 'sakura' ? ['#ffa0cb', '#ffd3e5', '#fce3fa'] : ['#ffc96b', '#ff6d48', '#ffe2a2'];
    const colors = effect === 'shrine' ? ['#64eaff', '#ff654e', '#ffe7af'] : palette;
    let width = innerWidth, height = innerHeight, scale = 1, frame = 0, previous = 0, elapsed = 0;
    let mouseX = 0, mouseY = 0, smoothX = 0, smoothY = 0;
    let lastMetrics = 0, stopped = false;
    const particles = [], rain = [];
    const random = (a, b) => a + Math.random() * (b - a);
    const tau = Math.PI * 2, strength = motion * (.6 + motion * .4);
    const media = host.querySelector('.dream-media'), image = media?.querySelector('img');
    let living;

    // GPU 局部风场：保留脸部轮廓，重点推动发尾、衣摆和扇边火焰。
    function createLivingArt() {
      if (!centered || !image?.complete || !image.naturalWidth || stopped || living) return;
      const art = document.createElement('canvas'); art.className = 'dream-living-art';
      const gl = art.getContext('webgl', {alpha: false, antialias: false});
      if (!gl) return;
      let program, texture, buffer;
      try {
        function shader(type, source) {
          const s = gl.createShader(type); gl.shaderSource(s, source); gl.compileShader(s);
          if (!gl.getShaderParameter(s, gl.COMPILE_STATUS)) {gl.deleteShader(s); throw Error('shader');}
          return s;
        }
        const vertex = shader(gl.VERTEX_SHADER, 'attribute vec2 a; varying vec2 uv; void main(){uv=(a+1.0)*.5;gl_Position=vec4(a,0.,1.);}');
        const fragment = shader(gl.FRAGMENT_SHADER, `precision mediump float;
          varying vec2 uv; uniform sampler2D picture; uniform vec2 cover; uniform float time; uniform float power;
          void main(){
            vec2 p=vec2(uv.x,1.0-uv.y); p=(p-.5)*cover+.5;
            float hair=exp(-pow((p.x-.65)/.16,2.0)-pow((p.y-.66)/.20,2.0));
            float hem=exp(-pow((p.x-.50)/.20,2.0)-pow((p.y-.86)/.10,2.0));
            float fan=exp(-pow((abs(p.x-.5)-.18)/.10,2.0)-pow((p.y-.60)/.20,2.0));
            float face=1.0-exp(-pow((p.x-.5)/.075,2.0)-pow((p.y-.46)/.09,2.0));
            float wave=sin(p.y*27.0-time*3.2)+.45*sin(p.y*51.0-time*4.8+p.x*13.0);
            p.x+=wave*(hair*.006+hem*.004+fan*.004)*face*power;
            p.y+=sin(p.x*32.0+time*4.1)*fan*.0025*face*power;
            gl_FragColor=texture2D(picture,clamp(p,0.001,0.999));
          }`);
        program = gl.createProgram(); gl.attachShader(program, vertex); gl.attachShader(program, fragment); gl.linkProgram(program);
        gl.deleteShader(vertex); gl.deleteShader(fragment);
        if (!gl.getProgramParameter(program, gl.LINK_STATUS)) throw Error('link');
        gl.useProgram(program); buffer = gl.createBuffer(); gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
        gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1,-1,1,-1,-1,1,1,1]), gl.STATIC_DRAW);
        const a = gl.getAttribLocation(program, 'a'); gl.enableVertexAttribArray(a); gl.vertexAttribPointer(a, 2, gl.FLOAT, false, 0, 0);
        texture = gl.createTexture(); gl.bindTexture(gl.TEXTURE_2D, texture);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
        gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGB, gl.RGB, gl.UNSIGNED_BYTE, image);
        const cover = gl.getUniformLocation(program, 'cover'), time = gl.getUniformLocation(program, 'time'), power = gl.getUniformLocation(program, 'power');
        gl.uniform1f(power, strength);
        const updateSize = () => {
          const w = Math.round(width * 1.06 * scale), h = Math.round(height * 1.06 * scale);
          art.width = w; art.height = h; gl.viewport(0, 0, w, h);
          const ratio = image.naturalWidth / image.naturalHeight, view = w / h;
          gl.uniform2f(cover, view < ratio ? view / ratio : 1, view > ratio ? ratio / view : 1);
        };
        updateSize(); media.append(art); image.style.visibility = 'hidden';
        living = {resize: updateSize, draw: t => {gl.uniform1f(time, t); gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4);}, stop: () => {
          image.style.visibility = ''; art.remove(); gl.deleteTexture(texture); gl.deleteBuffer(buffer); gl.deleteProgram(program);
          gl.getExtension('WEBGL_lose_context')?.loseContext();
        }};
        art.addEventListener('webglcontextlost', e => {e.preventDefault(); image.style.visibility = ''; art.style.display = 'none';}, {signal: abort.signal});
        canvas.dataset.livingArt = 'gpu';
      } catch (_) {
        gl.deleteTexture(texture); gl.deleteBuffer(buffer); gl.deleteProgram(program);
        gl.getExtension('WEBGL_lose_context')?.loseContext(); image.style.visibility = ''; art.remove();
      }
    }

    function resize() {
      width = innerWidth; height = innerHeight; scale = Math.min(1, 1800 / Math.max(1, width));
      canvas.width = Math.round(width * scale); canvas.height = Math.round(height * scale);
      particles.length = 0; rain.length = 0;
      const count = Math.round((centered ? 155 : 100) * (.35 + strength * .65));
      for (let i = 0; i < count; i++) particles.push({x: random(0, width), y: random(0, height), r: random(1, 4), speed: random(45, 160), phase: random(0, tau), angle: random(0, tau), color: colors[i % colors.length], butterfly: effect !== 'snow' && effect !== 'sakura' && i % 4 === 0, depth: random(.4, 1.4), orbital: centered && i % 3 === 0});
      if (storm || effect === 'shrine') for (let i = 0; i < (storm ? 160 : 70) * motion; i++) rain.push({x: random(0, width), y: random(0, height), speed: random(650, 1100), length: random(12, 38)});
      living?.resize(); canvas.dataset.particleCount = String(count);
    }
    resize(); createLivingArt();
    image?.addEventListener('load', createLivingArt, {once: true, signal: abort.signal});
    window.addEventListener('resize', resize, {signal: abort.signal});
    window.addEventListener('pointermove', e => {mouseX = (e.clientX / Math.max(1, width) - .5) * 30 * motion; mouseY = (e.clientY / Math.max(1, height) - .5) * 20 * motion;}, {signal: abort.signal, passive: true});

    function wing(x, y, r, flutter, color) {
      ctx.fillStyle = color; ctx.shadowColor = color; ctx.shadowBlur = 13;
      ctx.beginPath(); ctx.ellipse(x - r * .65 * flutter, y, r * flutter, r * .65, -.5, 0, tau); ctx.ellipse(x + r * .65 * flutter, y, r * flutter, r * .65, .5, 0, tau); ctx.fill(); ctx.shadowBlur = 0;
    }

    // 大幅旋转的双焰光带保持半透明，背景特效不接收鼠标事件。
    function drawRibbons(t) {
      if (!centered) return;
      const cx = width * .5, cy = height * .66, rx = Math.min(width * .23, height * .42), ry = height * .30;
      ctx.globalCompositeOperation = 'lighter';
      for (let band = 0; band < 2; band++) {
        const color = effect === 'eclipse' ? (band ? '#ff642c' : '#ffca6c') : cold ? (band ? '#a7a5ff' : '#55daff') : band ? '#ff684d' : '#55daff';
        const start = t * (band ? -.88 : 1.15) * (.5 + motion) + band * Math.PI;
        ctx.beginPath();
        for (let j = 0; j <= 100; j++) {
          const a = start + j / 100 * Math.PI * 1.55, ripple = 1 + .035 * Math.sin(a * 8 - t * 6);
          const x = cx + Math.cos(a) * rx * ripple, y = cy + Math.sin(a) * ry * ripple + Math.sin(a * 3 + t * 2) * height * .017;
          if (!j) ctx.moveTo(x, y); else ctx.lineTo(x, y);
        }
        ctx.strokeStyle = color; ctx.lineCap = 'round'; ctx.shadowColor = color;
        ctx.globalAlpha = .09 * strength; ctx.lineWidth = 20; ctx.shadowBlur = 18; ctx.stroke();
        ctx.globalAlpha = .46 * strength; ctx.lineWidth = 2.3; ctx.shadowBlur = 8; ctx.stroke(); ctx.shadowBlur = 0;
        // 沿光带生成不断伸缩的火舌，使轮廓呈现流焰而非固定圆线。
        for (let i = 0; i < 22; i++) {
          const a = start + i / 22 * Math.PI * 1.55;
          const x = cx + Math.cos(a) * rx, y = cy + Math.sin(a) * ry;
          const length = (16 + 22 * Math.sin(t * 7 + i * 1.7) ** 2) * (.4 + motion * .6);
          ctx.save(); ctx.translate(x, y); ctx.rotate(a + Math.PI * .5);
          ctx.globalAlpha = .35 * strength; ctx.fillStyle = color;
          ctx.beginPath(); ctx.moveTo(-8, 0);
          ctx.bezierCurveTo(-2, -length * .55, 9 + Math.sin(t * 9 + i) * 9, -length * .55, 4, -length);
          ctx.bezierCurveTo(17, -length * .45, 5, -5, 10, 0); ctx.closePath(); ctx.fill(); ctx.restore();
        }
        for (let i = 0; i < 9; i++) {const a = start + i / 9 * tau; ctx.globalAlpha = .7 * strength; wing(cx + Math.cos(a) * rx, cy + Math.sin(a) * ry, 6 + i % 3 * 2, .25 + .75 * Math.abs(Math.sin(t * 12 + i)), color);}
      }
      ctx.globalCompositeOperation = 'source-over'; ctx.globalAlpha = 1;
    }

    function drawWaves(t) {
      if (!tide && effect !== 'moon') return;
      ctx.strokeStyle = '#80efff'; ctx.lineWidth = 1.6;
      for (let i = 0; i < 7; i++) {
        const progress = (t * .22 + i / 7) % 1, baseline = height * (.73 + progress * .30);
        ctx.globalAlpha = Math.sin(progress * Math.PI) * .32 * strength; ctx.beginPath();
        for (let x = -20; x <= width + 20; x += 12) {const y = baseline + Math.sin(x * .014 + t * 3 + i) * (4 + progress * 9) + Math.sin(x * .031 - t * 4) * 3; if (x === -20) ctx.moveTo(x, y); else ctx.lineTo(x, y);}
        ctx.stroke();
      }
      ctx.globalAlpha = 1;
    }

    function drawMist(t) {
      const y = height * (.78 + Math.sin(t * .6) * .02);
      for (let i = 0; i < 3; i++) {
        const x = ((t * 42 * (.4 + motion) + i * width * .45) % (width * 1.8)) - width * .4;
        const g = ctx.createRadialGradient(x, y, 0, x, y, width * .3);
        g.addColorStop(0, cold || effect === 'shrine' ? '#81dbff' : '#eac0bd'); g.addColorStop(1, 'transparent');
        ctx.globalAlpha = .085 * strength; ctx.fillStyle = g;
        ctx.save(); ctx.translate(0, y); ctx.scale(1, .21); ctx.translate(0, -y); ctx.fillRect(x - width * .32, y - width * .32, width * .64, width * .64); ctx.restore();
      }
      ctx.globalAlpha = 1;
    }

    function drawRain(dt, t) {
      if (!rain.length) return;
      ctx.strokeStyle = '#b4e4ff'; ctx.lineWidth = .9; ctx.globalAlpha = .26 * strength; ctx.beginPath();
      for (const r of rain) {
        r.y += r.speed * dt * (.5 + motion); r.x -= r.speed * dt * .25;
        if (r.y > height + 40) {r.y = -40; r.x = random(-20, width + 200);} if (r.x < -100) r.x = width + 40;
        ctx.moveTo(r.x, r.y); ctx.lineTo(r.x - r.length * .25, r.y + r.length);
      }
      ctx.stroke(); ctx.globalAlpha = 1;
      if (!storm) return;
      // 雷电间隔 7 秒，仅使用低透明度远景照明。
      const cycle = t % 7;
      if (cycle < .45) {
        const alpha = Math.sin(cycle / .45 * Math.PI) * strength;
        ctx.globalAlpha = alpha; ctx.strokeStyle = '#d0e5ff'; ctx.lineWidth = 2; ctx.shadowColor = '#65acff'; ctx.shadowBlur = 18;
        const seed = Math.floor(t / 7); let x = width * (.22 + .12 * (seed % 5)), y = -10;
        ctx.beginPath(); ctx.moveTo(x, y);
        for (let j = 0; j < 10; j++) {x += Math.sin(seed * 9 + j * 13) * width * .035; y += height * .034; ctx.lineTo(x, y);}
        ctx.stroke(); ctx.shadowBlur = 0; ctx.globalAlpha = .045 * alpha; ctx.fillStyle = '#a2c2ff'; ctx.fillRect(0, 0, width, height); ctx.globalAlpha = 1;
      }
    }

    function draw(now) {
      frame = requestAnimationFrame(draw);
      if (document.hidden) {previous = now; return;}
      if (now - previous < 1000 / 45) return;
      const dt = Math.min(.055, (now - previous) / 1000 || .025); previous = now; elapsed += dt;
      const t = elapsed;
      ctx.setTransform(scale, 0, 0, scale, 0, 0); ctx.clearRect(0, 0, width, height);
      smoothX += (mouseX - smoothX) * .07; smoothY += (mouseY - smoothY) * .07;
      if (media) media.style.transform = `translate3d(${smoothX.toFixed(2)}px,${smoothY.toFixed(2)}px,0)`;
      living?.draw(t); drawMist(t); drawWaves(t); drawRibbons(t); drawRain(dt, t);
      for (const p of particles) {
        const falling = effect === 'sakura' || effect === 'snow';
        if (p.orbital) {
          p.angle += dt * (.5 + motion) * p.depth; const spread = .65 + .22 * Math.sin(t * .5 + p.phase);
          p.x = width * .5 + Math.cos(p.angle) * Math.min(width * .30, height * .55) * spread;
          p.y = height * .66 + Math.sin(p.angle) * height * .33 * spread;
        } else {
          p.y += (falling ? 1 : -1) * p.speed * dt * (.3 + motion) * p.depth;
          p.x += (Math.sin(t * 1.8 + p.phase) * 24 + (falling ? 35 : 12)) * dt * p.depth;
          if (p.y < -30) p.y = height + 30; if (p.y > height + 30) p.y = -30; if (p.x < -30) p.x = width + 30; if (p.x > width + 30) p.x = -30;
        }
        ctx.globalAlpha = (.38 + .45 * Math.sin(t * 3 + p.phase) ** 2) * strength;
        if (p.butterfly) {ctx.save(); ctx.translate(p.x, p.y); ctx.rotate(Math.sin(t * .8 + p.phase) * .4); wing(0, 0, 5 + p.depth * 4, .2 + .8 * Math.abs(Math.sin(t * 13 + p.phase)), p.color); ctx.restore();}
        else if (effect === 'sakura') {ctx.save(); ctx.translate(p.x, p.y); ctx.rotate(p.angle + t * 2); ctx.fillStyle = p.color; ctx.beginPath(); ctx.ellipse(0, 0, p.r * 2.5, p.r, 0, 0, tau); ctx.fill(); ctx.restore();}
        else {ctx.fillStyle = p.color; ctx.beginPath(); ctx.arc(p.x, p.y, p.r, 0, tau); ctx.fill(); ctx.globalAlpha *= .16; ctx.beginPath(); ctx.arc(p.x, p.y, p.r * 3, 0, tau); ctx.fill();}
      }
      ctx.globalAlpha = 1;
      if (now - lastMetrics > 400) {canvas.dataset.frames = String((Number(canvas.dataset.frames) || 0) + 1); canvas.dataset.elapsed = t.toFixed(2); lastMetrics = now;}
    }
    frame = requestAnimationFrame(draw);
    return function stop() {stopped = true; abort.abort(); cancelAnimationFrame(frame); living?.stop(); living = null; if (media) media.style.transform = ''; canvas.remove();};
  };
  factory.version = 4; window.__codexDreamEffects = factory;
})();
