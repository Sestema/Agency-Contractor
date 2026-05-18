# Agency Contractor

[🇺🇦 Українська](README.md) | 🇨🇿 Česky

**Systém pro správu zaměstnanců, dokumentů, mezd a evidence pro personální agentury**

**Poslední stabilní verze:** `0.1.72`  
**PostgreSQL multi-PC:** dostupné ve stabilním kanálu  
**Stáhnout:** [GitHub Releases](https://github.com/Sestema/Agency-Contractor/releases/latest)

Agency Contractor je profesionální desktopová WPF aplikace pro personální a pracovní agentury. Automatizuje celý pracovní cyklus: evidenci zaměstnanců, dokumenty, firmy, agentury, generování smluv, mzdy, reporty, faktury, AI asistenta, Telegram bota a práci více počítačů s jednou společnou databází.

---

## Hlavní funkce

### Evidence zaměstnanců
- Průvodce přidáním zaměstnance krok za krokem (fotografie, pas/ID, vízum/karta pobytu, pojištění, pracovní povolení)
- Detailní karta zaměstnance se záložkami: dokumenty, profil, historie, mzdy
- Osobní údaje, kontakty, adresa, občanství, místo narození
- Údaje o dokumentech: číslo víza, datum začátku/konce víza, vydávající orgán víza
- Přiřazení k firmě, agentuře, pozici, datu nástupu a ukončení
- Aktivní, archivovaní, obnovení a nedávno smazaní zaměstnanci
- Vyhledávání, filtry, seskupení podle firem, 4 režimy zobrazení
- Automatické ořezání a zpracování fotografií dokumentů
- Historie změn v profilu zaměstnance

### Dokumenty a kontrola platnosti
- Kontrola platnosti pasu, víza/karty pobytu, pojištění a pracovního povolení
- Podpora vlastních/uživatelských dokumentů
- Upozornění Warning / Critical / Expired
- Snooze problémů dokumentů na 7/14/30/60/90 dní
- Výměna dokumentů s automatickou archivací starých souborů
- Rychlé otevření složky zaměstnance se všemi soubory
- PDF reporty problémových dokumentů

### Správa firem, agentur a kandidátů
- Přidávání a nastavení firem-odběratelů
- IČO, sídlo, pracovní adresy, pozice, sazby, pracovní doba
- Přiřazení agentury ke každé firmě
- Drag & drop řazení firem v postranním menu
- Možnost skrýt neaktivní firmy bez smazání
- Barevné označení firem
- Samostatná databáze kandidátů s dokumenty, fotografií a vyhledáváním

### Šablony dokumentů
- Editor DOCX šablon s podporou tagů `${TAG_NAME}`
- Editor XLSX šablon
- PDF editor s umístěním tagů
- Katalog dostupných tagů s vyhledáváním
- Startovní šablony pro rychlý začátek
- Kopírování šablon mezi firmami
- **AI automatické vkládání tagů** (Google Gemini) — AI analyzuje dokument a vloží potřebné proměnné

### Generování dokumentů
- Automatické generování DOCX, PDF a XLSX ze šablon
- Vyplnění tagů daty zaměstnance, firmy, agentury, dokumentů a mzdy
- Podpora českých pracovních smluv se správným mapováním polí
- Hromadné generování dokumentů pro více zaměstnanců
- Generování dokumentů do jedné vybrané složky
- Detailní výsledek generování pro každého zaměstnance

### Finance a mzdy
- Měsíční evidence mezd, hodin, sazeb, hrubé/čisté mzdy, záloh a příplatků
- Stav výplaty: vyplaceno / nevyplaceno
- Vlastní sloupce ve mzdové tabulce
- Výdaje firem
- Poznámky s automatickým přenosem do dalších měsíců
- Přenos dluhů/zůstatků mezi měsíci
- Historie mezd v kartě zaměstnance
- Ochrana proti duplicitní historii výplat
- Práce v jednom měsíci pro různé firmy s menším rizikem konfliktů
- Podpisové archy, PDF a Excel export mezd

### Reporty a analytika
- Reporty zaměstnanců, firem, agentur a archivu
- Detailní seznam zaměstnanců a souhrnné přehledy
- Volba sloupců v reportech
- Sloupce: číslo víza, vydávající orgán víza, občanství, platnost dokumentů, firma, agentura, stav
- Filtry podle dat, firem a agentur
- Barevné zvýraznění archivovaných zaměstnanců
- Grafy a statistiky
- Export do PDF a XLSX

### Dashboard
- Hlavní panel se statistikami
- Přehled firem, zaměstnanců, dokumentů a mezd
- Upozornění na končící platnost dokumentů
- Pohyb zaměstnanců za měsíc: noví, ukončení, archivovaní, obnovení
- Mzdové souhrny po měsících
- Statistiky podle firem
- AI report o stavu agentury

### Faktury
- Faktury, cenové nabídky, objednávky, pokladní doklady
- ARES integrace podle IČO
- QR platby
- PDF šablony faktur
- Měny CZK, EUR, USD, PLN
- Katalog klientů, dodavatelů a položek

### AI integrace (Google Gemini)
- AI chat pro konzultace
- AI skenování dokumentů
- AI vyhledávání zaměstnanců přirozeným jazykem
- Automatické vkládání tagů do šablon dokumentů
- Rozpoznávání polí českých pracovních smluv
- AI report Dashboardu
- AI překlad a analýza HR novinek

### Telegram bot
- Vyhledávání zaměstnanců z telefonu
- Dotazy na dokumenty, mzdy, firmy a reporty
- Hlasové zprávy a textové otázky
- Kontext konverzace: bot si pamatuje posledního nalezeného zaměstnance
- Mzda za měsíc, stav výplaty, zálohy
- Dokumenty s končící platností
- Analytika zaměstnanců a firem

### PostgreSQL multi-PC
- SQLite režim pro jeden počítač
- PostgreSQL režim pro 3–5+ uživatelů
- Migrace SQLite → PostgreSQL
- Připojení dalších PC ke společné databázi
- Kopírování údajů pro připojení druhého PC
- Seznam připojených PC/uživatelů
- Live sync přes PostgreSQL LISTEN/NOTIFY
- Automatické opětovné připojení po výpadku sítě, VPN nebo uspání PC
- Práce přes lokální síť nebo Tailscale
- PostgreSQL může běžet na hlavním PC nebo serveru

### Sdílená složka dokumentů
- Databáze je uložena v PostgreSQL
- Fotografie, skeny, šablony a složky zaměstnanců zůstávají ve sdílené kořenové složce
- Složku zaměstnance lze otevřít jako běžnou složku Windows
- Vhodné pro SMB/NAS/serverovou složku nebo sdílený disk

### Backup
- Automatický denní backup do `Backup\YYYY-MM-DD`
- Jeden backup denně pro všechna PC
- V PostgreSQL režimu se nejdříve aktualizuje SQLite kopie z PostgreSQL, poté se vytvoří backup
- Uchovává se posledních 7 dní
- Lock chrání před současným backupem z více počítačů
- SQLite backup umožňuje rychle se vrátit do starého režimu nebo přenést složku

### Protokol aktivit
- Logování akcí v systému
- Historie změn zaměstnanců
- Historie archivace, obnovení, dokumentů a profilu
- Lokalizované zobrazení záznamů protokolu
- Export protokolu do Excelu

---

## Nastavení

- **4 jazyky rozhraní:** Українська, Čeština, English, Русский
- **4 motivy vzhledu:** Light, Dark, Dark Word, Custom
- **Velikost rozhraní:** Small, Medium, Large, Extra Large
- **Velikost textu:** Small, Medium, Large, Extra Large
- **Jazyk dokumentů:** nastavení nezávisle na jazyku rozhraní
- **Viditelnost firem:** skrytí neaktivních firem
- **Viditelnost tagů:** nastavení zobrazení tagů v katalogu
- **Nastavení serverů:** PostgreSQL, Telegram, Web panel
- **Nastavení AI:** klíč Gemini, model, AI funkce

---

## Bezpečnost a licencování

- **Licenční systém** s vazbou na počítač (Machine ID)
- **HMAC-SHA256 podpis** licenčního souboru
- **DPAPI pro tajné údaje a hesla**
- **PostgreSQL přístup přes uživatelské jméno a heslo**
- **Anti-rollback ochrana** (detekce změny systémového data)
- **Potvrzení pro nebezpečné akce**
- Dokumenty zůstávají ve vaší složce pod vaší kontrolou
- Podpora období: 30, 90, 180, 362 dní nebo neomezeně
- Aktivace prostřednictvím souboru `activator.key`

---

## Automatické aktualizace

Aplikace podporuje automatické aktualizace přes GitHub Releases a Velopack:
1. Otevřete **Nastavení** → **Aktualizace aplikace**
2. Klikněte na **"Zkontrolovat aktualizace"**
3. Pokud je k dispozici nová verze — klikněte na **"Instalovat"**
4. Aplikace stáhne aktualizaci a restartuje se

---

## Systémové požadavky

- **OS:** Windows 10 / 11 (x64)
- **Runtime:** .NET 8.0 Desktop Runtime (součástí Setup.exe)
- **Místo na disku:** ~200 MB
- **Operační paměť:** 4 GB (doporučeno 8 GB)
- **Pro multi-PC:** PostgreSQL a sdílená kořenová složka dokumentů
- **Pro vzdálenou práci:** lokální síť, VPN nebo Tailscale

---

## Instalace

### Varianta 1: Instalátor (doporučeno)
1. Stáhněte `AgencyContractor-win-Setup.exe` z [Releases](https://github.com/Sestema/Agency-Contractor/releases)
2. Spusťte a počkejte na instalaci
3. Aplikace se nainstaluje do `%LocalAppData%\AgencyContractor`

### Varianta 2: Přenosná verze
1. Stáhněte `AgencyContractor-win-Portable.zip` z [Releases](https://github.com/Sestema/Agency-Contractor/releases)
2. Rozbalte do libovolné složky
3. Spusťte `Win11DesktopApp.exe`

---

## Technologie

- **WPF** (.NET 8.0) — rozhraní
- **MVVM** — architektura
- **SQLite** — lokální režim pro jeden počítač
- **PostgreSQL** — multi-PC režim
- **Npgsql** — práce s PostgreSQL
- **OpenXML SDK** — práce s DOCX/XLSX
- **PDFsharp** — generování PDF
- **ClosedXML** — práce s Excelem
- **OpenCvSharp** — zpracování obrázků
- **Google Gemini API** — AI funkce
- **Telegram Bot API** — mobilní bot
- **Velopack** — automatické aktualizace
- **DPAPI** — ochrana tajných údajů

---

## Autor

**Oleksandr Kachalin**

---

## Licence

Tento software je vlastnictvím autora. Neoprávněné kopírování, šíření nebo úpravy jsou přísně zakázány.

Podrobnosti v souboru [LICENSE](LICENSE).

© 2026 Oleksandr Kachalin. Všechna práva vyhrazena.
