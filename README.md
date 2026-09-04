# NET25: Valfri arkitektur

> EPA - Edugrade Personal Assistant

Ett eventdrivet arkitekturprojekt som läser av Edugrades läroplattform och håller koll på när utbildaren publicerar nya 
uppgifter. Programmet använder sig av en microservice som scrapear en given grupp på Learnpoint, returnerar det till 
kärnprogrammet som lagrar det i en databas.

Vid händelse av en uppgift i en grupp där eleven ännu inte fått betyg eller en AI-summering av innehållet, 
så skickas ämnet och kursen till en LLM som returnerar en kort beskrivning om vad veckan handlar om. 
När svaret har returnerats till kärnan så skickas det ut ett nytt event som triggar tre händelser parallellt;
en filskrivning till disk, ett sms-utskick till de användare som prenumererar på händelser samt ett anrop till 
Buggernaut som genererar tre extra övningsuppgifter på temat.

Tanken med programmet var att det skulle fungera med polling, men för presentationssyfte så anropas tre `trigger`-endpoints.
Detta för att jag ens ska kunna visa programmet när det kör.

Min grundtanke var att ha många microservices som körde någon sekventiellt, men det slutade med ett eventdrivet program som använde sig av en microservice.
