// Rendert banner.html nach docs/assets/banner.png (2560×840, also 2× für scharfe Darstellung).
// Nutzt das installierte Google Chrome; anderer Browser über CHROME_PATH.
const { chromium } = require('playwright-core');
const path = require('path');

(async () => {
  const browser = await chromium.launch(process.env.CHROME_PATH ? { executablePath: process.env.CHROME_PATH } : { channel: 'chrome' });
  const page = await browser.newPage({ viewport: { width: 1280, height: 420 }, deviceScaleFactor: 2 });
  await page.goto('file:///' + path.join(__dirname, 'banner.html').replace(/\\/g, '/'));
  await page.locator('.banner').screenshot({ path: path.join(__dirname, '..', '..', 'docs', 'assets', 'banner.png') });
  await browser.close();
  console.log('banner.png geschrieben');
})().catch(e => { console.error(e); process.exit(1); });
