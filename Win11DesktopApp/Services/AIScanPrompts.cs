using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Win11DesktopApp.Services
{
    public static class AIScanPrompts
    {
        public static string GetPrompt(string docKey)
        {
            return docKey switch
            {
                "passport" => @"Read THIS passport photo. Ignore any previous documents. ONLY Latin alphabet, NEVER Cyrillic.

STEP 1: Find the MRZ — two lines of CAPITAL letters/numbers at the very bottom of the passport page.
MRZ Line 1 format: P<COUNTRYCODESURNAME<<FIRSTNAME<<<<<<<<<
MRZ Line 2 format: PASSPORTNOCHECKNATBIRTHDATESEXEXPIRY

STEP 2: Parse the MRZ of THIS document:
- SURNAME is between P<COUNTRYCODE and << (the double angle brackets). Example: P<UKRTARNAVSKYI<<RUSLAN — SURNAME=TARNAVSKYI
- FIRSTNAME is after << (double angle brackets) until < padding. Example: P<UKRTARNAVSKYI<<RUSLAN — FIRSTNAME=RUSLAN
- PASSPORT NUMBER is the first 9 characters of MRZ line 2 (may contain letters AND digits). Example: GB780524<9 — number is GB780524. This is NOT the 4-digit authority number printed elsewhere on the page!
- BIRTHDATE is 6 digits YYMMDD starting at position 14 of line 2. Convert to DD.MM.YYYY.
- EXPIRY is 6 digits YYMMDD starting at position 22 of line 2. Convert to DD.MM.YYYY.

STEP 3: Find place of birth (printed text, NOT from MRZ). Look for the field labeled ""Place of birth"" or ""Місце народження"". Read the REGION/OBLAST name (e.g. ОДЕСЬКА ОБЛ. = Odesa, КИЇВ = Kyiv, ЛЬВІВСЬКА ОБЛ. = Lviv). Output ONLY the city name in Latin, never the oblast/region word.

Return ONLY this JSON (FirstName=given name, LastName=surname):
{""FirstName"":"""",""LastName"":"""",""BirthDate"":"""",""PassportNumber"":"""",""PassportCity"":"""",""PassportCountry"":"""",""PassportExpiry"":""""}",

                "insurance" => @"Read this insurance card/document photo. CRITICAL: ALL output must be in Latin alphabet ONLY, never Cyrillic.

This is a Czech health insurance card (průkaz pojištěnce). Extract these fields:
- InsuranceCompanyShort: short name of the insurance company (e.g. VZP CR, ZPMV, OZP, CPZP)
- InsuranceNumber: the field labeled ""Číslo pojištěnce"" (personal insurance number, usually 10 digits like birth number). This is NOT ""Číslo průkazu"" (card number which is much longer). Look specifically for ""Číslo pojištěnce"" or ""Cislo pojistence"".
- InsuranceExpiry: the ""Do:"" date (expiry) in DD.MM.YYYY format

Return ONLY valid JSON, no other text:
{""InsuranceCompanyShort"":"""",""InsuranceNumber"":"""",""InsuranceExpiry"":""""}",

                "visa" => @"Read this Czech visa/residence permit document photo. CRITICAL: ALL output in Latin alphabet ONLY, never Cyrillic.

IMPORTANT: The photo may contain MULTIPLE documents on one page. You MUST identify the correct one:

PRIORITY RULE: IGNORE any ""Osvědčení o registraci"" (EU registration certificate) — it is NOT the residence permit.
INSTEAD, look for one of these:

TYPE A — VISA STICKER (for Ukrainian refugees):
A colorful sticker with ""Číslo víza"", ""Druh víza"", hologram, MRZ code at bottom.
- VisaNumber: 9-digit visa number near ""Číslo víza""
- VisaType: FULL code with slashes like D/DO/667, D/DO/668, D/DO/669, D/DO/767-769, D/DO/867-869, D/VS/91, D/SD/91. NOT just ""D"".
- VisaExpiry: ""Do/To"" date in DD.MM.YYYY
- WorkPermitName: If D/DO/ → ""Dočasná ochrana"", if D/VS/ or D/SD/ → ""Strpění""

TYPE B — RESIDENCE PERMIT STAMP from MV ČR OAMP (for EU citizens):
A rectangular official stamp with ""MV ČR OAMP"" and header text ""POVOLENÍ K ... POBYTU NA ÚZEMÍ"".
This stamp is the MOST IMPORTANT document on the page. Read its header line carefully:
- VisaNumber: the permit/registration number (alphanumeric code, e.g. ""VB 027159"", may appear on the stamp or on the registration certificate above)
- VisaType: leave empty """"
- VisaExpiry: the validity date in DD.MM.YYYY from the stamp (look for ""do"" or a date written by hand)
- WorkPermitName: ONLY based on the MV ČR OAMP stamp header text:
  If stamp says ""PŘECHODNÉMU POBYTU"" → output exactly ""Přechodný pobyt""
  If stamp says ""TRVALÉMU POBYTU"" → output exactly ""Trvalý pobyt""
  NEVER output ""Osvědčení o registraci"" — that is wrong.

Return ONLY valid JSON, no other text:
{""VisaNumber"":"""",""VisaType"":"""",""VisaExpiry"":"""",""WorkPermitName"":""""}",

                "permit" => @"Read this Czech work permit document (Povolení k zaměstnání / ROZHODNUTÍ). CRITICAL: ALL output must be in Latin alphabet ONLY, never Cyrillic. Read ALL pages of the document.

This is typically a multi-page official document (ROZHODNUTÍ) issued by Úřad práce České republiky (Czech Labour Office).

STEP 1 — Find the permit reference number (Číslo jednací / Č.j.):
Look near the top for ""Č.j."" or a reference code like ""ROA-B3025-za"", ""UPA-xxx"" etc.
This is WorkPermitNumber.

STEP 2 — Find the permit type:
Usually stated as ""povolení k zaměstnání"" in the body text.
Look for ""vydává povolení k zaměstnání"".
WorkPermitType = ""Povolení k zaměstnání""

STEP 3 — Find the validity period:
Look for ""Povolení k zaměstnání se vydává na dobu od DD.MM.YYYY do DD.MM.YYYY""
WorkPermitIssueDate = the ""od"" (from) date in DD.MM.YYYY format
WorkPermitExpiry = the ""do"" (to) date in DD.MM.YYYY format

STEP 4 — Find the issuing authority:
Look at the document header: ""Úřad práce České republiky - krajská pobočka v [City]""
WorkPermitAuthority = full authority name

STEP 5 — Permit title:
WorkPermitName = ""Povolení k zaměstnání""

Return ONLY valid JSON, no other text:
{""WorkPermitName"":"""",""WorkPermitNumber"":"""",""WorkPermitType"":"""",""WorkPermitIssueDate"":"""",""WorkPermitExpiry"":"""",""WorkPermitAuthority"":""""}",

                "id_card" => @"Read this EU national ID card (Carte de Identitate, Personalausweis, Občanský průkaz, etc.). CRITICAL: ALL output must be in Latin alphabet ONLY, never Cyrillic.

STEP 1: Find the card holder's personal data:
- Last name (Nume/Name/Příjmení): the surname field
- First name (Prenume/Vorname/Jméno): the given name field
- Birth date: convert to DD.MM.YYYY format
- Sex: M or F

STEP 2: Find the document number:
- Look for SERIA + NR (e.g. 'SERIA ZT NR 106848' → number is 'ZT106848')
- Or look for the document number field (Číslo dokladu, Ausweisnummer, etc.)
- This may also appear in the MRZ at the bottom

STEP 3: Find validity/expiry:
- Look for 'Valabilitate/Validity' or 'Platnost' field with a date range (e.g. '08.02.24-03.08.2031')
- Extract the END date and convert to DD.MM.YYYY format (e.g. '03.08.2031')

STEP 4: Find place of birth:
- Look for 'Loc naștere/Place of birth' or 'Místo narození'
- Output ONLY the city/locality name in Latin

STEP 5: Find nationality/country:
- Look for 'Cetățenie/Nationality' (e.g. 'Română' → 'Romania')
- Or derive from the country code (ROU → Romania, CZE → Czech Republic, DEU → Germany, etc.)

Return ONLY this JSON (PassportExpiry = card expiry date):
{""FirstName"":"""",""LastName"":"""",""BirthDate"":"""",""PassportNumber"":"""",""PassportCity"":"""",""PassportCountry"":"""",""PassportExpiry"":""""}",

                "passport2" => @"Read this passport second page or EU residence permit photo. CRITICAL: ALL output must be in Latin alphabet ONLY, never Cyrillic.

Extract:
- WorkPermitName: Look for the field 'DRUH POBYTU NA ÚZEMÍ' or 'DRUH POVOLENÍ' on the document. Common values: 'Přechodný pobyt', 'Trvalý pobyt', 'Osvědčení o registraci občana EU'. Return the EXACT Czech text from the document.
- VisaNumber: document number (look for a number like 'VB 027159' etc.)
- VisaExpiry: expiry date in DD.MM.YYYY format (look for 'PLATNOST DO')

Return ONLY valid JSON, no other text:
{""WorkPermitName"":"""",""VisaNumber"":"""",""VisaExpiry"":""""}",

                _ => "Describe what you see in this document image."
            };
        }

        public static Dictionary<string, string> ParseResponse(string response)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var jsonMatch = Regex.Match(response, @"\{[^{}]*\}", RegexOptions.Singleline);
                if (!jsonMatch.Success) return result;

                using var doc = JsonDocument.Parse(jsonMatch.Value);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    var val = prop.Value.GetString()?.Trim();
                    if (!string.IsNullOrEmpty(val) && val != "N/A" && val != "n/a" && val != "unknown")
                        result[prop.Name] = val;
                }
            }
            catch (Exception ex)
            {
                LoggingService.LogError("AIScanPrompts.ParseResponse", ex);
            }
            return result;
        }
    }
}
