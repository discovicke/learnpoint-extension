# Learnpoint Extension (EPA - Edugrade Personal Assistant)

> Skolprojekt i utbildningssyfte. Studiebevakare som hämtar kurser från Learnpoint, sammanfattar nya veckor med AI och skickar notiser via SMS plus genererar övningsuppgifter.

![.NET](https://img.shields.io/badge/.NET-10-512BD4)
![C#](https://img.shields.io/badge/C%23-239120?logo=csharp&logoColor=white)
![Node.js](https://img.shields.io/badge/Node.js-20-339933?logo=node.js&logoColor=white)
![Puppeteer](https://img.shields.io/badge/Puppeteer-scraper-40B5A4)
![SQLite](https://img.shields.io/badge/SQLite-EF_Core-003B57)
![LLM Integration](https://img.shields.io/badge/LLM_Integration-Gemini-8E75B2)
![Status](https://img.shields.io/badge/Status-Skolprojekt-yellow)

## Vad är detta

Skolprojekt utvecklat i utbildningssyfte för att öva arkitektur i praktiken. Programmet bevakar kurser i Learnpoint och hjälper till med studierna genom att sammanfatta nytt innehåll per vecka, skicka SMS-teasers och skapa övningsuppgifter.

En vecka eller ett tema är en `TrackedSection`, till exempel `Vecka 1 - Klassiska designmönster`. AI sammanfattar per vecka, aldrig per delmoment.

## Så fungerar det

Systemet är händelsedrivet. Tre domänhändelser kedjar flödet:

- `NewContentUploadedEvent` - hel hämtad kurs, hanteras av `CourseSyncHandler` som sparar mot databasen med upsert och skickar nästa event vid behov
- `SectionRegisteredEvent` - ny vecka i en kurs utan betyg, hanteras av `AiSummarizeHandler` med ett AI-anrop per vecka som sparar `AiSummary`
- `WeekSummarizedEvent` - sparad sammanfattning, hanteras parallellt av tre handlers: `SummaryFileHandler` som skriver en markdown-fil, `SmsHandler` via 46elks och `BuggernautHandler` som skapar övningsuppgifter

```text
POST /api/trigger/all (eller /api/trigger/{groupId})
  -> scraper-service hämtar kurser med innehåll per teori och uppgift
  -> NewContentUploadedEvent per kurs
  -> CourseSyncHandler sparar kurs, sektion och item i SQLite
      +-- ny sektion och kursen saknar betyg ger SectionRegisteredEvent
      +-- annars inget, betygsatt kurs räknas som avslutad
  -> AiSummarizeHandler gör ett Gemini-anrop per vecka och sparar AiSummary
  -> WeekSummarizedEvent
      +-- SummaryFileHandler skriver summaries/{kurs}/{vecka}.md
      +-- SmsHandler skickar teaser på max 160 tecken till alla Subscribers
      +-- BuggernautHandler gör 3 anrop (Easy, Medium, Hard)
```

AI-anrop skickas bara medan kursen pågår, alltså utan betyg. När betyget väl är satt behövs inga fler sammanfattningar eller notiser. Ändrat innehåll i en vecka nollställer dess `AiSummary`, och en omsammanfattning kan alltid tvingas fram med `POST /api/sections/{id}/summarize`.

## Delar i projektet

Eventbussen i `coreservice/Events/EventBus.cs` är egen och körs i minnet utan externa beroenden. Varje eventtyp har `IEventHandler<T>`-implementationer som registreras som singletons och prenumereras i `Program.cs`. Det är ett medvetet val utan extern broker som RabbitMQ eller Kafka, eftersom systemet körs lokalt av en person och händelserna är få och billiga att tappa. En ny körning av en trigger återskapar allt.

Utanför coreservice finns en fristående microservice: `scraper-service` byggd med `Node.js` och `Puppeteer` med eget HTTP-API på port 5001. Coreservice anropar det via `HttpScraperAdapter` (`IScraperService`) och läser aldrig scraperns `.env` direkt. Scrapern är stateless och minns inget mellan anrop. All jämförelse av nytt och ändrat innehåll ligger i coreservice, som är single source of truth.

`console-client` är en läs- och adminklient mot coreservice REST-API, ingen del av eventkedjan.

## AI och Buggernaut

Sammanfattningarna görs med Gemini via `coreservice/Infrastructure/Ai/GeminiSummarizeAdapter.cs`. Nyckeln läses från `GoogleAi:ApiKey` och modellen från `GoogleAi:Model` med `gemini-3.5-flash` som standard.

Övningsuppgifterna genereras med [Buggernaut](https://github.com/discovicke/Buggernaut), ett CLI-verktyg som skapar C#-övningar med buggar via valfri LLM. Integrationen sker via `coreservice/Infrastructure/Buggernaut/ProcessBuggernautAdapter.cs` och `Handlers/BuggernautHandler.cs`, som skapar en uppgift per nivå i `Easy`, `Medium` och `Hard` utifrån veckans titel och sammanfattning.

## Kom igång

Kräver `.NET 10`, `Node 20+` och `dotnet tool restore` för Buggernaut, se `.config/dotnet-tools.json`. Lägg Learnpoint-inloggning i `scraper-service/.env`, se `.env.example`. Hemligheter till coreservice sätts via user-secrets eller env:

| Nyckel | Syfte |
|--------|-------|
| `GoogleAi:ApiKey` och `GoogleAi:Model` | AI-sammanfattning med Gemini |
| `Elks:Username`, `Elks:Password` och `Elks:From` | SMS via 46elks, hoppas över om de saknas |
| `Buggernaut:*` | Kategori, timeout och LLM-nycklar till verktygsprocessen |
| `Scraper:BaseUrl` | Adress till scraper-service, standard `http://localhost:5001` |

Starta båda tjänsterna från rotmappen, coreservice på port 5000 och scraper på port 5001:

```bash
npm run dev
```

Console-klienten för läsning, trigger, sammanfattning och prenumeranter:

```bash
dotnet run --project console-client
```

Hälsokontroll:

```bash
curl http://localhost:5000/health
```
