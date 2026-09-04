# NET25: Valfri arkitektur

> EPA - Edugrade Personal Assistant

Studiebevakare som scrapear kurser från Learnpoint, sammanfattar nya veckor med AI
och skickar notiser via SMS plus genererar övningsuppgifter. En vecka/tema är en
`TrackedSection` (t.ex. "Vecka 1 - Klassiska designmönster"). AI sammanfattar per
vecka, aldrig per delmoment.

## Arkitekturval: EDA i modulär monolit + ett fristående microservice

Systemet är händelsedrivet. Tre domänhändelser kedjar flödet:

- `NewContentUploadedEvent` - hel scrapad kurs, hanteras av `CourseSyncHandler`
  (upsert mot DB, publicerar nästa event vid behov)
- `SectionRegisteredEvent` - ny vecka i en obetygsatt kurs, hanteras av
  `AiSummarizeHandler` (ett AI-anrop per vecka, sparar `AiSummary`)
- `WeekSummarizedEvent` - sparad sammanfattning, hanteras parallellt och
  oberoende av tre slutpunktshandlers: `SummaryFileHandler` (markdown-fil),
  `SmsHandler` (46elks) och `BuggernautHandler` (övningsuppgifter)

Eventbussen (`coreservice/Events/EventBus.cs`) är egen och in-memory, utan externa
beroenden. Varje event-typ har `IEventHandler<T>`-implementationer som registreras
som singletons och prenumereras i `Program.cs`. Det är ett medvetet val: ingen
extern broker (RabbitMQ/Kafka) eftersom systemet körs lokalt av en person och
händelserna är få och billiga att tappa (en omkörning av trigger återskapar allt).

Utanför coreservice finns en fristående microservice: `scraper-service`
(Node.js + Puppeteer) med eget HTTP-API på port 5001. Coreservice anropar det via
`HttpScraperAdapter` (`IScraperService`) och läser aldrig scraperns `.env` direkt.
Scrapern är stateless och minns inget mellan anrop. All diff (nytt/ändrat
innehåll) ligger i coreservice, som är single source of truth.

`console-client` är en läs- och admin-klient mot coreservice REST-API, ingen del
av eventkedjan.

## Så fungerar applikationen

```
POST /api/trigger/all (eller /api/trigger/{groupId})
  -> scraper-service deep-scrapear kurser (inkl. Content per teori/uppgift)
  -> NewContentUploadedEvent per kurs
  -> CourseSyncHandler: upsert kurs/sektion/item i SQLite
      +-- ny sektion och kursen saknar betyg --> SectionRegisteredEvent
      +-- annars: inget (betygsatt kurs räknas som avslutad)
  -> AiSummarizeHandler: ett Gemini-anrop per vecka, sparar AiSummary
  -> WeekSummarizedEvent
      +-- SummaryFileHandler: summaries/{kurs}/{vecka}.md (fulltext)
      +-- SmsHandler: teaser på max 160 tecken till alla Subscribers
      +-- BuggernautHandler: 3 anrop (Easy/Medium/Hard) via dotnet-tool Buggernaut
```

AI-event skickas bara medan kursen pågår (utan betyg).
När betyget väl är satt behövs inga fler sammanfattningar eller notiser. Ändrat
innehåll i en vecka nollställer dess `AiSummary`, och man kan alltid tvinga fram
en omsammanfattning med `POST /api/sections/{id}/summarize` (ignorerar grinden).

## Kom igång

Kräver .NET 10, Node 20+ och `dotnet tool restore` (Buggernaut, se
`.config/dotnet-tools.json`). Lägg Learnpoint-inloggning i
`scraper-service/.env` (se `.env.example`). Hemligheter till coreservice via
user-secrets eller env:

| Nyckel | Syfte |
|--------|-------|
| `GoogleAi:ApiKey`, `GoogleAi:Model` | AI-sammanfattning (Gemini) |
| `Elks:Username`, `Elks:Password`, `Elks:From` | 46elks SMS (hoppas över om de saknas) |
| `Buggernaut:*` | Kategori, timeout, LLM-nycklar till verktygsprocessen |
| `Scraper:BaseUrl` | scraper-service, default `http://localhost:5001` |

Starta båda tjänsterna från rotmappen (coreservice :5000, scraper :5001):

```bash
npm run dev
```

Console-klienten (läsning, trigger, sammanfattning, prenumeranter):

```bash
dotnet run --project console-client
```
