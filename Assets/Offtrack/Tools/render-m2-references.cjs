// Real browser reference capture. Does not modify the HTML prototypes.
// Usage: node Assets/Offtrack/Tools/render-m2-references.cjs <playwright-core module path>
// Optional: OFFTRACK_BROWSER_EXECUTABLE chooses a Chromium executable.
'use strict';

const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { pathToFileURL } = require('node:url');
const { createHash } = require('node:crypto');
const { chromium } = require(process.argv[2] || 'playwright-core');
const root = path.resolve(__dirname, '../../..');
const output = path.join(root, 'docs/acceptance/m2/evidence');
const frames = [
  ['a', '06.0', 6], ['a', '10.6', 10.6], ['a', '12.8', 12.8],
  ['b', '01.0', 1], ['b', '07.0', 7], ['b', '08.5', 8.5],
];
const hash = file => createHash('sha256').update(fs.readFileSync(file)).digest('hex');

(async () => {
  fs.mkdirSync(output, { recursive: true });
  const browser = await chromium.launch({
    headless: true,
    ...(process.env.OFFTRACK_BROWSER_EXECUTABLE
      ? { executablePath: process.env.OFFTRACK_BROWSER_EXECUTABLE } : {}),
  });
  const errors = [];
  try {
    const page = await browser.newPage({ viewport: { width: 1600, height: 900 }, deviceScaleFactor: 1 });
    page.on('pageerror', error => errors.push(error.message));
    for (const [scene, stamp, time] of frames) {
      const source = path.join(root, `docs/reference/cutscene-${scene}.html`);
      const before = hash(source);
      const url = pathToFileURL(source);
      url.searchParams.set('t', String(time));
      await page.goto(url.href, { waitUntil: 'load' });
      await page.evaluate(async () => {
        await document.fonts.ready;
        await new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve)));
      });
      const state = await page.evaluate(() => ({
        ...window.__state(),
        width: document.querySelector('canvas').width,
        height: document.querySelector('canvas').height,
        dpr: window.devicePixelRatio,
      }));
      assert.equal(state.t, time);
      assert.equal(state.frozen, true);
      assert.equal(state.width, 1600);
      assert.equal(state.height, 900);
      assert.equal(state.dpr, 1);
      const target = path.join(output, `cutscene_${scene}_${stamp}_ref.png`);
      await page.screenshot({ path: target, fullPage: false, animations: 'disabled' });
      assert.equal(hash(source), before, 'Reference HTML must remain unchanged');
      console.log(JSON.stringify({ file: path.relative(root, target), state, sourceSha256: before, pngSha256: hash(target) }));
    }
    assert.deepEqual(errors, [], 'Prototype browser must have no JavaScript exceptions');
    console.log(`Captured ${frames.length} actual prototype frames with ${browser.version()}.`);
  } finally {
    await browser.close();
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
