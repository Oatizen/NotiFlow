const { chromium } = require('playwright');
const path = require('path');
const { spawn } = require('child_process');

(async () => {
  console.log("Launching browser in 1080p (Frame-by-Frame deterministic mode)...");
  const browser = await chromium.launch({
      args: ['--force-device-scale-factor=1', '--disable-gpu-vsync', '--window-size=1920,1080']
  });
  
  const width = 1920;
  const height = 1080;
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
  
  const mp4Out = path.join(__dirname, 'ppt_demo_1080p60_master.mp4');
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
    
        const slideDurations = [4, 12, 4, 9, 5, 5, 18, 15, 5];
    const targetDurationSec = i < slideDurations.length ? slideDurations[i] : 5;
    const targetFrames = targetDurationSec * fps;
    let localFrame = 0;

    while (localFrame < targetFrames) {
        const dt = 1000 / fps;

        // Advance JS Time
        await page.evaluate((delta) => {
            const now = performance.now();
            window.__TIME += delta;
            window.__VIRTUAL_TIME += delta;
            
            // Execute and remove triggered timers
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
                    } catch(e) {}
                }
            }
            
            // Advance CSS/WAAPI Animations
            document.getAnimations().forEach(a => {
                if (a.playState === 'running' || a.playState === 'pending') {
                    a.pause();
                }
                a.currentTime = (a.currentTime || 0) + delta;
            });
            
            // Sync HTML5 Video elements to virtual time
            document.querySelectorAll('video').forEach(v => {
                if (!v.paused) v.pause();
                if (v.duration && v.duration > 0) {
                    v.currentTime = (window.__VIRTUAL_TIME / 1000) % v.duration;
                } else {
                    v.currentTime = (window.__VIRTUAL_TIME / 1000);
                }
            });
        }, dt);

        // Trigger RequestAnimationFrame
        await page.evaluate(() => {
            return new Promise(resolve => requestAnimationFrame(resolve));
        });

        const buffer = await page.screenshot({ type: 'jpeg', quality: 95 });
        await writeBuffer(buffer);
        
        globalFrame++;
        localFrame++;
    }
    
    // We don't need the 0.5s wait at the end anymore because the slideDurations include padding!
  }
  
  await context.close();
  await browser.close();
  
  ffmpeg.stdin.end();
  
  console.log("Finishing encoding, flushing buffers...");
  await new Promise(resolve => ffmpeg.on('close', resolve));
  console.log(`Saved ${globalFrame} frames to ${mp4Out}`);
})();
