import puppeteer from 'puppeteer';
import dotenv from 'dotenv';
import { writeFile } from 'fs/promises';
import path from 'path';
import { fileURLToPath } from 'url';

dotenv.config();

const BASE = process.env.LEARNPOINT_URL;
const USER = process.env.LEARNPOINT_USERNAME;
const PASS = process.env.LEARNPOINT_PASSWORD;
const GROUP_IDS = (process.env.LEARNPOINT_GROUP_IDS || '')
  .split(',')
  .map((s) => s.trim())
  .filter(Boolean);
const DEEP = process.env.DEEP_SCRAPE === 'true';
const OUT_FILE =
  process.env.OUT_FILE ||
  path.join(path.dirname(fileURLToPath(import.meta.url)), '..', 'output', 'learnpoint-content.json');

if (!BASE || !USER || !PASS || GROUP_IDS.length === 0) {
  throw new Error(
    'Sätt LEARNPOINT_URL, LEARNPOINT_USERNAME, LEARNPOINT_PASSWORD och LEARNPOINT_GROUP_IDS i .env',
  );
}

const loginFields = {
  username: '#ctl00_SiteContentPlaceHolder_ctlUserLoginControl_txtUserName',
  password: '#ctl00_SiteContentPlaceHolder_ctlUserLoginControl_txtPassword',
  login: '#ctl00_SiteContentPlaceHolder_ctlUserLoginControl_btnLogin',
};

async function login(page) {
  await page.goto(BASE, { waitUntil: 'networkidle0' });
  if (!(await page.$(loginFields.username))) return; // redan inloggad

  await page.type(loginFields.username, USER);
  await page.type(loginFields.password, PASS);
  await Promise.all([
    page.waitForNavigation({ waitUntil: 'networkidle0' }),
    page.click(loginFields.login),
  ]);
  console.log('Inloggad');
}

async function scrapeGroup(page, id) {
  await page.goto(`${BASE}/GroupForms/Group_LearningContent_Content.aspx?Id=${id}`, {
    waitUntil: 'networkidle0',
  });
  return page.evaluate(
    (groupId) => {
      const txt = (el) => (el ? el.innerText.trim() : '');
      return {
        groupId,
        url: location.href,
        scrapedAt: new Date().toISOString(),
        groupTitle: txt(document.querySelector('.site-content-header__title')),
        groupSubTitle: txt(document.querySelector('.site-content-header__sub-title')),
        courseGrade: txt(document.querySelector('.learning-content-content__course-grade-status')),
        noContent: !!document.querySelector('.learning-content-content__no-sections-panel'),
        sections: [...document.querySelectorAll('.learning-content-content-section')].map((section) => ({
          title: txt(section.querySelector('.learning-content-content-section__title')),
          description: txt(section.querySelector('.learning-content-content-section__description-text')),
          items: [...section.querySelectorAll('.learning-content-content-section__item')].map((item) => ({
            title: txt(item.querySelector('.learning-content-content-section__item-link')),
            href: item.querySelector('.learning-content-content-section__item-link')?.getAttribute('href') ?? '',
            status: item.querySelector('.learning-content-content-section__item-status-icon')?.getAttribute('title') ?? '',
            date: txt(item.querySelector('.learning-content-content-section__item-date')),
          })),
        })),
      };
    },
    id,
  );
}

async function scrapeItem(page, id, itemId) {
  const url = `${BASE}/GroupForms/Group_LearningContent_Item.aspx?Id=${id}&ItemId=${itemId}`;
  await page.goto(url, { waitUntil: 'networkidle0' });
  const data = await page.evaluate(() => {
    const txt = (el) => (el ? el.innerText.trim() : '');
    return {
      title: txt(document.querySelector('.ascx-learning-content-item-header__title')),
      meta: txt(document.querySelector('.ascx-learning-content-item-header__meta')),
      tab: txt(document.querySelector('.ascx-learning-content-item-header__tab-item.SELECTED')),
      status: document
        .querySelector('.ascx-learning-content-item-header__status-icon')
        ?.getAttribute('title') ?? '',
      content: txt(document.querySelector('.learning-content-item__content-body')),
      links: [
        ...(document.querySelector('.learning-content-item__content-body')?.querySelectorAll('a') ?? []),
      ].map((a) => ({ text: a.innerText.trim(), href: a.getAttribute('href') })),
      hasContent: !!document.querySelector('.learning-content-item__content-body'),
    };
  });
  return { groupId: id, itemId, url, scrapedAt: new Date().toISOString(), ...data };
}

async function main() {
  const browser = await puppeteer.launch({ headless: 'new' });
  try {
    const page = await browser.newPage();
    await login(page);

    const results = [];
    for (const id of GROUP_IDS) {
      const group = await scrapeGroup(page, id);
      if (DEEP && !group.noContent) {
        group.items = [];
        for (const section of group.sections) {
          for (const item of section.items) {
            const itemId = item.href.match(/ItemId=(\d+)/)?.[1];
            if (!itemId) continue;
            group.items.push(await scrapeItem(page, id, itemId));
          }
        }
        console.log(`✔ ${group.groupTitle} – ${group.items.length} itemsidor`);
      } else {
        console.log(`✔ ${group.groupTitle} – ${group.sections.length} sektioner`);
      }
      results.push(group);
    }

    await writeFile(OUT_FILE, JSON.stringify(results, null, 2), 'utf8');
    console.log(`Sparat ${results.length} grupper till ${OUT_FILE}`);
  } finally {
    await browser.close();
  }
}

main().catch((err) => {
  console.error('Fel:', err.message);
  process.exit(1);
});