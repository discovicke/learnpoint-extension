import dotenv from 'dotenv';
import { writeFile } from 'fs/promises';
import path from 'path';
import { fileURLToPath } from 'url';
import { getRegisteredGroupIds, scrapeGroups } from './scraper.js';

dotenv.config();

const OUT_FILE =
  process.env.OUT_FILE ||
  path.join(path.dirname(fileURLToPath(import.meta.url)), '..', 'output', 'learnpoint-content.json');

async function main() {
  // CLI: scrapear grupperna från .env (LEARNPOINT_GROUP_IDS).
  // HTTP-läget (server.js) återanvänder scrapeGroups() - ingen duplicerad logik.
  const ids = getRegisteredGroupIds();
  if (ids.length === 0) {
    throw new Error('Sätt LEARNPOINT_GROUP_IDS i .env');
  }

  const results = await scrapeGroups(ids);
  await writeFile(OUT_FILE, JSON.stringify(results, null, 2), 'utf8');
  console.log(`Sparat ${results.length} grupper till ${OUT_FILE}`);
}

main().catch((err) => {
  console.error('Fel:', err.message);
  process.exit(1);
});
