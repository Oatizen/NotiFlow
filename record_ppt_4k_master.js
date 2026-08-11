const { chromium } = require('playwright');
const path = require('path');
const { spawn } = require('child_process');

(async () => {
  console.log("Launching browser in 4K (Frame-by-Frame deterministic mode)...");
  const browser = await chromium.launch({
      args: ['--force-device-scale-factor=1', '--disable-gpu-vsync', '--window-size=3840,2160']
  });
  
  const width = 3840;
  const height = 2160;
  const fps = 60;
  
  const context = await browser.newContext({
    viewport: { width, height },
    deviceScaleFactor: 1
  });
  
  const page = await context.newPage();
  
  // Mock performance.now to freeze time during screenshots
  await page.addInitScript(() => {
      window.__VIRTUAL_TIME = 0;
      window.__TIME_FROZEN = false;
      const origPerf = performance.now.bind(performance);
      const origDate = Date.now.bind(Date);
      
      performance.now = () => {
          return window.__TIME_FROZEN ? window.__VIRTUAL_TIME : origPerf();
      };
      Date.now = () => {
          return window.__TIME_FROZEN ? Math.floor(window.__VIRTUAL_TIME) : origDate();
      };
      const origRaf = window.requestAnimationFrame;
      window.requestAnimationFrame = (cb) => {
          return origRaf((realTime) => {
              cb(window.__TIME_FROZEN ? window.__VIRTUAL_TIME : realTime);
          });
      };
      
      const origSetTimeout = window.setTimeout.bind(window);
      const origClearTimeout = window.clearTimeout.bind(window);
      const origSetInterval = window.setInterval.bind(window);
      const origClearInterval = window.clearInterval.bind(window);
      
      window.__virtualTimers = new Map();
      window.__virtualTimerId = 100000;
      
      window.setTimeout = (cb, delay, ...args) => {
          if (!window.__TIME_FROZEN) {
              return origSetTimeout(cb, delay, ...args);
          }
          const id = window.__virtualTimerId++;
          window.__virtualTimers.set(id, {
              type: 'timeout',
              triggerTime: window.__VIRTUAL_TIME + (delay || 0),
              cb, args
          });
          return id;
      };
      
      window.setInterval = (cb, delay, ...args) => {
          if (!window.__TIME_FROZEN) {
              return origSetInterval(cb, delay, ...args);
          }
          const id = window.__virtualTimerId++;
          window.__virtualTimers.set(id, {
              type: 'interval',
              interval: Math.max(delay || 0, 1),
              triggerTime: window.__VIRTUAL_TIME + (delay || 0),
              cb, args
          });
          return id;
      };
      
      window.clearTimeout = window.clearInterval = (id) => {
          if (id >= 100000) {
              window.__virtualTimers.delete(id);
          } else {
              origClearTimeout(id);
          }
      };
  });
  
  console.log("Loading PPT...");
  await page.goto('file:///' + path.join(__dirname, 'ppt', 'index.html').replace(/\\/g, '/'), { waitUntil: 'load' });
  
  await page.evaluate(() => { window.__lowPowerMode = false; });
  await page.evaluate(() => {
    const nav = document.getElementById('nav');
    if(nav) nav.style.display = 'none';
    const hint = document.getElementById('hint');
    if(hint) hint.style.display = 'none';
  });
  
  const numSlides = await page.evaluate(() => document.querySelectorAll('.slide').length);
  console.log(`Found ${numSlides} slides.`);
  
  const mp4Out = path.join(__dirname, 'ppt_demo_4k60_master.mp4');
  console.log("Encoding MP4 to", mp4Out);
  
  const ffmpeg = spawn('ffmpeg', [
      '-y',
      '-f', 'image2pipe',
      '-vcodec', 'mjpeg',
      '-framerate', String(fps),
      '-i', '-',
      '-c:v', 'libx264',
      '-pix_fmt', 'yuv420p',
      '-crf', '14',
      '-preset', 'slow',
      mp4Out
  ]);
  
  ffmpeg.stderr.on('data', d => {
      // Uncomment to debug ffmpeg:
      // console.log(d.toString());
  });
  
  const writeBuffer = async (buffer) => {
      if (!ffmpeg.stdin.write(buffer)) {
          await new Promise(r => ffmpeg.stdin.once('drain', r));
      }
  };
  
  const frameDuration = 1000 / fps;
  let globalFrame = 0;
  
  // Initial wait to let page settle
  await page.waitForTimeout(1000);
  
  // Freeze time permanently!
  await page.evaluate(() => {
      window.__VIRTUAL_TIME = performance.now();
      window.__TIME_FROZEN = true;
  });

  for (let i = 0; i < numSlides; i++) {
    console.log(`Recording slide ${i + 1}/${numSlides}...`);
    
    if (i > 0) {
        await page.keyboard.press('ArrowRight');
        // Give one real frame for the keydown event to be processed and CSS transition to start
        await page.evaluate(() => new Promise(r => requestAnimationFrame(r)));
    }
    
    let animating = true;
    let localFrame = 0;
    while (animating) {
        animating = await page.evaluate((dt) => {
            window.__VIRTUAL_TIME += dt;
            
            // Tick virtual timers
            const timersToRun = [];
            window.__virtualTimers.forEach((timer, id) => {
                if (window.__VIRTUAL_TIME >= timer.triggerTime) {
                    timersToRun.push({id, timer});
                }
            });
            timersToRun.sort((a, b) => a.timer.triggerTime - b.timer.triggerTime);
            
            for (const {id, timer} of timersToRun) {
                if (window.__virtualTimers.has(id)) {
                    if (timer.type === 'timeout') {
                        window.__virtualTimers.delete(id);
                    } else {
                        timer.triggerTime += timer.interval;
                        if (timer.triggerTime <= window.__VIRTUAL_TIME) {
                            timer.triggerTime = window.__VIRTUAL_TIME + timer.interval;
                        }
                    }
                    try {
                        if (typeof timer.cb === 'function') {
                            timer.cb(...timer.args);
                        } else if (typeof timer.cb === 'string') {
                            eval(timer.cb);
                        }
                    } catch(e) { console.error(e); }
                }
            }
            
            let hasActive = false;
            document.getAnimations().forEach(a => {
                if (a.playState === 'running' || a.playState === 'pending') {
                    a.pause();
                }
                const timing = a.effect ? a.effect.getComputedTiming() : null;
                const isInfinite = timing && timing.iterations === Infinity;
                
                a.currentTime = (a.currentTime || 0) + dt;
                
                if (!isInfinite && timing) {
                    const endTime = timing.delay + timing.activeDuration + (timing.endDelay || 0);
                    if ((a.currentTime || 0) < endTime) {
                        hasActive = true;
                    }
                }
            });
            
            // If there are pending timeouts, we are still animating/waiting
            window.__virtualTimers.forEach(t => {
                if (t.type === 'timeout') {
                    hasActive = true;
                }
            });
            
            return hasActive;
        }, frameDuration);
        
        await page.evaluate(() => new Promise(r => requestAnimationFrame(r)));
        
        const buffer = await page.screenshot({ type: 'jpeg', quality: 95 });
        await writeBuffer(buffer);
        
        globalFrame++;
        localFrame++;
        
        // Increase timeout to 30 seconds (1800 frames) since we have long sequences now
        if (localFrame > fps * 30) {
            console.log("Animation taking too long (>30s), breaking to next slide...");
            break;
        }
    }
    
    // Exactly 0.5s wait at the end of the slide
    for(let j = 0; j < Math.round(fps * 0.5); j++) {
        await page.evaluate((dt) => {
            window.__VIRTUAL_TIME += dt;
            
            // Still tick timers just in case
            const timersToRun = [];
            window.__virtualTimers.forEach((timer, id) => {
                if (window.__VIRTUAL_TIME >= timer.triggerTime) {
                    timersToRun.push({id, timer});
                }
            });
            timersToRun.sort((a, b) => a.timer.triggerTime - b.timer.triggerTime);
            for (const {id, timer} of timersToRun) {
                if (window.__virtualTimers.has(id)) {
                    if (timer.type === 'timeout') window.__virtualTimers.delete(id);
                    else {
                        timer.triggerTime += timer.interval;
                        if (timer.triggerTime <= window.__VIRTUAL_TIME) timer.triggerTime = window.__VIRTUAL_TIME + timer.interval;
                    }
                    try {
                        if (typeof timer.cb === 'function') timer.cb(...timer.args);
                        else if (typeof timer.cb === 'string') eval(timer.cb);
                    } catch(e) {}
                }
            }

            document.getAnimations().forEach(a => {
                if (a.playState === 'running' || a.playState === 'pending') {
                    a.pause();
                }
                a.currentTime = (a.currentTime || 0) + dt;
            });
        }, frameDuration);
        await page.evaluate(() => new Promise(r => requestAnimationFrame(r)));
        
        const buffer = await page.screenshot({ type: 'jpeg', quality: 95 });
        await writeBuffer(buffer);
        
        globalFrame++;
    }
  }
  
  await context.close();
  await browser.close();
  
  ffmpeg.stdin.end();
  
  console.log("Finishing encoding, flushing buffers...");
  await new Promise(resolve => ffmpeg.on('close', resolve));
  console.log(`Saved ${globalFrame} frames to ${mp4Out}`);
})();
