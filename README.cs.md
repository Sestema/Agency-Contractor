# Agency Contractor

[🇺🇦 Українська](README.md) | 🇨🇿 Česky

**Systém správy zaměstnanců a dokumentů pro personální agentury**

Agency Contractor je profesionální desktopová WPF aplikace pro personální (pracovní) agentury, která automatizuje celý pracovní cyklus: od registrace zaměstnance po generování dokumentů, kontrolu termínů a finanční evidenci.

---

## Hlavní funkce

### Správa zaměstnanců
- Průvodce přidáním zaměstnance krok za krokem (fotografie, pas, vízum, pojištění, pracovní povolení)
- Automatické oříznutí a zpracování fotografií dokumentů
- Podrobný profil zaměstnance s historií změn
- Archivace zaměstnanců se zachováním všech dat
- Kontrola platnosti dokumentů (pas, vízum, pojištění, pracovní povolení)
- Automatická archivace starých dokumentů při aktualizaci

### Správa firem
- Přidávání a nastavení firem-odběratelů
- Drag & drop řazení firem v postranním menu
- Možnost skrýt neaktivní firmy
- Barevné označení firem

### Šablony dokumentů
- Editor DOCX šablon s podporou tagů `${TAG_NAME}`
- Editor XLSX šablon
- Editor PDF
- Katalog dostupných tagů s vyhledáváním
- **AI automatické vkládání tagů** (Google Gemini) — AI analyzuje text dokumentu a automaticky vkládá potřebné tagy na správná místa

### Generování dokumentů
- Automatické generování DOCX, PDF, XLSX ze šablon
- Vyplnění tagů daty zaměstnance a firmy
- Podpora českých pracovních smluv se správným mapováním polí

### Finance a výplaty
- Evidence mezd, záloh, příplatků po měsících
- Poznámky s automatickým přenosem do následujících měsíců
- Uložení šířky sloupců
- Export finančních sestav

### Sestavy
- Sestava zaměstnanců (PDF, XLSX)
- Sestava firem (PDF, XLSX)
- Zvýraznění archivovaných zaměstnanců barvou
- Zarovnání a formátování sloupců

### Kandidáti
- Databáze potenciálních zaměstnanců
- Samostatná sekce pro správu kandidátů

### Dashboard
- Hlavní panel se statistikami
- Přehled firem a zaměstnanců
- Upozornění na vypršení platnosti dokumentů

### AI integrace (Google Gemini)
- AI chat pro konzultace
- Automatické vkládání tagů do šablon dokumentů
- Rozpoznávání polí českých pracovních smluv

### Protokol aktivit
- Logování všech akcí v systému
- Historie změn

---

## Nastavení

- **4 jazyky rozhraní:** Українська, Čeština, English, Русский
- **4 motivy vzhledu:** Light, Dark, Dark Word, Custom
- **Velikost rozhraní:** Small, Medium, Large, Extra Large
- **Velikost textu:** Small, Medium, Large, Extra Large
- **Jazyk dokumentů:** nastavení nezávisle na rozhraní
- **Viditelnost firem:** skrytí neaktivních firem
- **Viditelnost tagů:** nastavení zobrazení tagů v katalogu

---

## Bezpečnost a licencování

- **Licenční systém** s vazbou na počítač (Machine ID)
- **HMAC-SHA256 podpis** licenčního souboru
- **DPAPI šifrování** licenčních dat
- **Anti-rollback ochrana** (detekce změny systémového data)
- Podpora termínů: 30, 90, 180, 362 dní nebo neomezeně
- Aktivace prostřednictvím souboru `activator.key`

---

## Automatické aktualizace

Aplikace podporuje automatické aktualizace prostřednictvím GitHub Releases:
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
- **OpenXML SDK** — práce s DOCX/XLSX
- **PDFsharp** — generování PDF
- **ClosedXML** — práce s Excelem
- **OpenCvSharp** — zpracování obrázků
- **Google Gemini API** — AI funkce
- **Velopack** — automatické aktualizace
- **DPAPI** — šifrování licencí

---

## Struktura projektu

```
Win11DesktopApp/
├── Converters/          # WPF konvertory
├── Helpers/             # Pomocné třídy
├── Models/              # Datové modely
├── Resources/           # Zdroje (ikony, motivy, jazyky)
│   ├── Languages/       # Lokalizace (uk, cs, en, ru)
│   └── Themes/          # Motivy vzhledu
├── Services/            # Obchodní logika
├── ViewModels/          # MVVM ViewModels
└── Views/               # WPF Views (XAML)
```

---

## Autor

**Oleksandr Kachalin**

---

## Licence

Tento software je vlastnictvím autora. Neoprávněné kopírování, šíření nebo úpravy jsou přísně zakázány.

Podrobnosti v souboru [LICENSE](LICENSE).

© 2026 Oleksandr Kachalin. Všechna práva vyhrazena.
