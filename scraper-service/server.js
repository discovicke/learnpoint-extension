import dotenv from 'dotenv';
import express from 'express';
import { getRegisteredGroupIds, scrapeGroups } from './scraper.js';

dotenv.config();

const PORT = Number(process.env.PORT || 5001);
const app = express();
app.use(express.json());

// Hälsokoll för coreservice
app.get('/health', (_req, res) => res.send('OK'));

// Vilka grupper scrapern känner till från .env (coreservice använder detta
// för POST /api/trigger/all - core läser aldrig scraperns .env direkt).
app.get('/groups', (_req, res) => {
  res.json({ groupIds: getRegisteredGroupIds() });
});

// Deep-scrapear alltid allt för de begärda grupperna.
// Stateless: inget sparas mellan anrop, diffen ligger i coreservice.
// Body: { "groupIds": ["1601", 1603] }
app.post('/scrape', async (req, res) => {
  const groupIds = req.body?.groupIds;
  if (!Array.isArray(groupIds) || groupIds.length === 0) {
    return res.status(400).json({ message: 'Body måste innehålla groupIds: [int].' });
  }

  try {
    const courses = await scrapeGroups(groupIds);
    res.json(courses);
  } catch (err) {
    console.error('Scrape fel:', err.message);
    res.status(500).json({ message: err.message ?? 'Scraping misslyckades.' });
  }
});

app.listen(PORT, () => console.log(`scraper-service lyssnar på http://localhost:${PORT}`));
