using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    public static class AIScanPrompts
    {
        public const string DocumentKindKey = "__document_kind";
        public const string OverallConfidenceKey = "__confidence";
        public const string ConfidencePrefix = "__confidence_";
        public const string SourcePrefix = "__source_";

        public static string GetPrompt(string docKey)
        {
            var prompt = docKey switch
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
- CRITICAL: If the MRZ is blurry, cut off, partially hidden, or unreadable, return PassportNumber as an empty string. NEVER guess.
- CRITICAL: Do NOT take PassportNumber from any visa, permit, insurance card, or any other document that may appear on the same image.
- If both the printed passport number and the MRZ passport number are clearly visible, prefer the value that matches both. If they conflict, return empty instead of guessing.

STEP 3: Find place of birth (printed text, NOT from MRZ). Look for the field labeled ""Place of birth"" or ""Місце народження"". Read the REGION/OBLAST name (e.g. ОДЕСЬКА ОБЛ. = Odesa, КИЇВ = Kyiv, ЛЬВІВСЬКА ОБЛ. = Lviv). Output ONLY the city name in Latin, never the oblast/region word.

STEP 4: Find issuing authority / document issuer (PassportAuthority). This is the field for ""Ким виданий паспорт"".
Look for labels like ""Authority"", ""Issued by"", ""Issuing authority"", ""Орган, що видав"", ""Орган видачі"", ""Орган выдачи"", ""Виданий"", ""Vydal"", ""Orgán vydávající doklad"".
For Ukrainian passports/ID cards this can be a 4-digit numeric authority code such as 5142. If only a numeric authority code is visible, return that code exactly.
Do NOT use PassportNumber, MRZ, place of birth, citizenship, or country as PassportAuthority. Do NOT invent a full institution name if only a code is visible.

STEP 5: Find nationality/citizenship and issuing country:
- Citizenship: look for labels such as ""Nationality"", ""Citizenship"", ""Státní občanství"", ""Громадянство"". Normalize common variants like UKR / Ukraina / Ukraine to a sensible Latin country name.
- IssuingCountry: country that issued the passport or ID card. Prefer the country name/code printed in the document header, coat of arms area, passport code, or issuer section (e.g. UKR -> Ukraine, CZE -> Czech Republic, ROU -> Romania). This is NOT the place of birth and NOT necessarily the citizenship if they differ.

Return ONLY this JSON (FirstName=given name, LastName=surname):
{""FirstName"":"""",""LastName"":"""",""BirthDate"":"""",""Sex"":"""",""PassportNumber"":"""",""PassportAuthority"":"""",""PassportCity"":"""",""PassportCountry"":"""",""Citizenship"":"""",""IssuingCountry"":"""",""PassportExpiry"":""""}",

                "insurance" => @"Read this insurance card/document photo. CRITICAL: ALL output must be in Latin alphabet ONLY, never Cyrillic.

This is a Czech health insurance card (prukaz pojistence). Extract these fields:
- FirstName: holder's first/given name if it is clearly printed on the card. If not clearly visible, leave empty.
- LastName: holder's last name/surname if it is clearly printed on the card. If not clearly visible, leave empty.
- BirthDate: holder's birth date in DD.MM.YYYY ONLY if it is explicitly printed on the card as a date. If it is NOT explicitly printed, leave empty. Do NOT derive it from the insurance number.
- InsuranceCompanyCode: 3-digit insurance company code if visible (111, 201, 205, 207, 209, 211, 213)
- InsuranceCompanyShort: short insurance company name if visible (e.g. VZP, ZPMV, OZP, CPZP, RBP)
- InsuranceCompanyFull: full insurance company name if visible
- InsuranceCompanyRaw: the exact insurance company text as seen on the card if code/short/full are unclear
- InsuranceNumber: the field labeled ""Číslo pojištěnce"" (personal insurance number, usually 10 digits like birth number). This is NOT ""Číslo průkazu"" (card number which is much longer). Look specifically for ""Číslo pojištěnce"" or ""Cislo pojistence"".
- InsuranceExpiry: the ""Do:"" date (expiry) in DD.MM.YYYY format

Return ONLY valid JSON, no other text:
{""FirstName"":"""",""LastName"":"""",""BirthDate"":"""",""InsuranceCompanyCode"":"""",""InsuranceCompanyShort"":"""",""InsuranceCompanyFull"":"""",""InsuranceCompanyRaw"":"""",""InsuranceNumber"":"""",""InsuranceExpiry"":""""}",

                "visa" => @"Read this Czech visa/residence permit document photo. CRITICAL: ALL output in Latin alphabet ONLY, never Cyrillic.

IMPORTANT: The photo may contain MULTIPLE documents on one page. You MUST identify the correct one:

PRIORITY RULE: IGNORE any ""Osvědčení o registraci"" (EU registration certificate) — it is NOT the residence permit.
INSTEAD, look for one of these:

TYPE A — VISA STICKER (for Ukrainian refugees):
A colorful sticker with ""Číslo víza"", ""Druh víza"", hologram, MRZ code at bottom.
- FirstName: visa holder's given name in Latin alphabet if clearly visible
- LastName: visa holder's surname in Latin alphabet if clearly visible
- BirthDate: holder's birth date in DD.MM.YYYY if clearly visible
- PassportNumber: passport/travel document number ONLY from the field labeled ""Číslo cestovního dokladu"", ""Passport No"", ""Cestovní doklad"", or similar. It is usually letters+digits like FP181622. NEVER use the 9-digit visa number as PassportNumber. NEVER use the large top visa number or MRZ visa serial as PassportNumber.
- VisaNumber: 9-digit visa number near ""Číslo víza""
- VisaAuthority: if a clear issuing authority is visible, extract it. If you can clearly read ""MV ČR OAMP"" anywhere on the visa or related authority text, return exactly ""MV ČR OAMP"". Otherwise leave empty.
- VisaType: FULL code with slashes like D/DO/667, D/DO/668, D/DO/669, D/DO/767-769, D/DO/867-869, D/VS/91, D/SD/91. NOT just ""D"".
- VisaStartDate: ONLY the date from the label ""Platí od"", ""Valid from"", ""Od"", or ""From"". On Czech visa stickers there are usually two dates together: first is VisaStartDate, second is VisaExpiry. NEVER use MRZ dates, birth date, passport expiry, document number dates, or unrelated right-side service dates as VisaStartDate.
- VisaExpiry: ONLY the ""Platí do"", ""Valid until"", ""Do"", or ""To"" date in DD.MM.YYYY.
- If you are not sure that VisaStartDate comes from ""Platí od / Valid from / Od"", leave VisaStartDate empty instead of guessing.
- WorkPermitName: If D/DO/ → ""Dočasná ochrana"", if D/VS/ or D/SD/ → ""Strpění""

TYPE B — RESIDENCE PERMIT STAMP from MV ČR OAMP (for EU citizens):
A rectangular official stamp with ""MV ČR OAMP"" and header text ""POVOLENÍ K ... POBYTU NA ÚZEMÍ"".
This stamp is the MOST IMPORTANT document on the page. Read its header line carefully:
- FirstName: holder's given name in Latin alphabet if clearly visible
- LastName: holder's surname in Latin alphabet if clearly visible
- BirthDate: holder's birth date in DD.MM.YYYY if clearly visible
- PassportNumber: passport/travel document number only if it is clearly labeled as passport/document number. Do not copy the permit/registration number into PassportNumber.
- VisaNumber: the permit/registration number (alphanumeric code, e.g. ""VB 027159"", may appear on the stamp or on the registration certificate above)
- VisaAuthority: if the stamp/authority text shows ""MV ČR OAMP"", return exactly ""MV ČR OAMP"". Otherwise return the clearly visible issuing authority text if present.
- VisaType: leave empty """"
- VisaStartDate: start/valid-from date in DD.MM.YYYY if clearly visible (look for ""od"", ""platnost od"", ""valid from"")
- VisaExpiry: the validity date in DD.MM.YYYY from the stamp (look for ""do"" or a date written by hand)
- WorkPermitName: ONLY based on the MV ČR OAMP stamp header text:
  If stamp says ""PŘECHODNÉMU POBYTU"" → output exactly ""Přechodný pobyt""
  If stamp says ""TRVALÉMU POBYTU"" → output exactly ""Trvalý pobyt""
  NEVER output ""Osvědčení o registraci"" — that is wrong.

Also extract if clearly visible:
- Sex: M or F
- Citizenship: nationality/citizenship of the holder
- IssuingCountry: country that issued this visa/residence document

Return ONLY valid JSON, no other text:
{""FirstName"":"""",""LastName"":"""",""BirthDate"":"""",""Sex"":"""",""PassportNumber"":"""",""VisaNumber"":"""",""VisaAuthority"":"""",""VisaType"":"""",""VisaStartDate"":"""",""VisaExpiry"":"""",""WorkPermitName"":"""",""Citizenship"":"""",""IssuingCountry"":""""}",

                "permit" => @"Read this Czech work permit document (Povolení k zaměstnání / ROZHODNUTÍ). CRITICAL: ALL output must be in Latin alphabet ONLY, never Cyrillic. Read ALL pages of the document.

This is typically a multi-page official document (ROZHODNUTÍ) issued by Úřad práce České republiky (Czech Labour Office).

STEP 0 — Find the employee identity if clearly visible:
- FirstName: employee's given name in Latin alphabet if clearly visible
- LastName: employee's surname in Latin alphabet if clearly visible
- CRITICAL Czech document name order: near labels like ""cizinci:"", ""účastník:"", or in the address block, Czech official documents often print the person as SURNAME FIRSTNAME, for example ""Yuryk Ihor"" means LastName=""Yuryk"", FirstName=""Ihor"". Do NOT swap it into FirstName=""Yuryk"".
- If both a profile-style line and separate labels are visible, prefer the explicit labels; otherwise treat the first word in ""cizinci:"" line as LastName and the second word as FirstName.
- BirthDate: employee's birth date in DD.MM.YYYY if clearly visible

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

Also extract if clearly visible:
- Sex: M or F
- Citizenship: nationality/citizenship of the employee
- IssuingCountry: country issuing this permit

Return ONLY valid JSON, no other text:
{""FirstName"":"""",""LastName"":"""",""BirthDate"":"""",""Sex"":"""",""WorkPermitName"":"""",""WorkPermitNumber"":"""",""WorkPermitType"":"""",""WorkPermitIssueDate"":"""",""WorkPermitExpiry"":"""",""WorkPermitAuthority"":"""",""Citizenship"":"""",""IssuingCountry"":""""}",

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
- Citizenship = nationality of the holder
- IssuingCountry = country that issued the ID card

STEP 6: Find residence status / permit name if clearly visible:
- Look for fields like 'DRUH POVOLENÍ', 'DRUH POBYTU', 'Karta trvalého pobytu'
- Return the exact Czech status if clearly visible, for example 'Trvalý pobyt' or 'Přechodný pobyt'

STEP 7: Find issuing authority / document issuer (PassportAuthority). Look for labels like 'Authority', 'Issued by', 'Orgán vydávající doklad', 'Vydal', 'Eliberată de', 'Орган видачі', or a dedicated issuer code. If the card shows only a code, return the code exactly. Do NOT use document number as authority.

Return ONLY this JSON (PassportExpiry = card expiry date):
{""FirstName"":"""",""LastName"":"""",""BirthDate"":"""",""Sex"":"""",""PassportNumber"":"""",""PassportAuthority"":"""",""PassportCity"":"""",""PassportCountry"":"""",""Citizenship"":"""",""IssuingCountry"":"""",""PassportExpiry"":"""",""WorkPermitName"":""""}",

                "passport2" => @"Read this passport second page or EU residence permit photo. CRITICAL: ALL output must be in Latin alphabet ONLY, never Cyrillic.

If this is the SECOND SIDE of a Czech residence ID card, extract these fields:
- WorkPermitName: look for the field 'DRUH POBYTU NA ÚZEMÍ' or 'DRUH POVOLENÍ'. Common values: 'Přechodný pobyt', 'Trvalý pobyt', 'Osvědčení o registraci občana EU'. Return the EXACT Czech text from the document if clearly visible.
- VisaNumber: document/card number shown on the card.
- VisaStartDate: issue / valid-from date in DD.MM.YYYY format if clearly visible (look for 'PLATNOST OD', 'DATUM VYDÁNÍ')
- VisaExpiry: expiry date in DD.MM.YYYY format (look for 'PLATNOST DO').
- VisaAuthority: issuing authority / place of issue from fields like 'DATUM VYDÁNÍ - MÍSTO VYDÁNÍ'. Example: 'MV ČR PLZEŇ'.
- PassportCity: birth city from fields like 'MÍSTO NAROZENÍ'. If the value contains city + country like 'DIBROVA UKR', return ONLY the city part 'DIBROVA'.
- PassportCountry: birth country from the same birth-place line. If the country is shown as a code like 'UKR', return the normalized Latin country name like 'Ukraine'.

If this is NOT that document type, still extract the fields that are clearly visible and leave the others empty.

Return ONLY valid JSON, no other text:
{""WorkPermitName"":"""",""VisaNumber"":"""",""VisaStartDate"":"""",""VisaExpiry"":"""",""VisaAuthority"":"""",""PassportCity"":"""",""PassportCountry"":""""}",

                "id_card_back" => @"Read ONLY the BACK / SECOND SIDE of this EU national ID card. CRITICAL: ALL output must be in Latin alphabet ONLY, never Cyrillic.

This prompt is ONLY for the second side of a 2-sided EU ID card. Give priority to data printed on THIS side, even if similar data may exist on the front side.

Extract these fields:
- WorkPermitName: look for fields like 'DRUH POBYTU NA ÚZEMÍ', 'DRUH POVOLENÍ', residence status or card status. Common values: 'Přechodný pobyt', 'Trvalý pobyt', 'Osvědčení o registraci občana EU'. Return the exact Czech text if clearly visible.
- VisaNumber: document/card number if clearly visible on this side.
- VisaStartDate: issue / valid-from date in DD.MM.YYYY format if clearly visible.
- VisaExpiry: expiry / validity-until date in DD.MM.YYYY format if clearly visible on this side.
- PassportAuthority: issuing authority / place of issue from fields like 'DATUM VYDÁNÍ - MÍSTO VYDÁNÍ', 'Orgán vydávající doklad', 'Vydal', 'Místo vydání'. If you can clearly read 'MV ČR OAMP', return exactly 'MV ČR OAMP'.
- PassportCity: birth city from fields like 'MÍSTO NAROZENÍ'. If the value contains city + country, return ONLY the city/locality part.
- PassportCountry: birth country from the same birth-place line. If the country is shown as a code like 'UKR', normalize it to a Latin country name like 'Ukraine'.

If a field is not clearly visible on THIS side, leave it empty. NEVER invent values.

Return ONLY valid JSON, no other text:
{""WorkPermitName"":"""",""VisaNumber"":"""",""VisaStartDate"":"""",""VisaExpiry"":"""",""PassportAuthority"":"""",""PassportCity"":"""",""PassportCountry"":""""}",

                "visa2" => @"Read this second-side visa / residence ID card / residence permit photo. CRITICAL: ALL output must be in Latin alphabet ONLY, never Cyrillic.

If this is the BACK SIDE of a residence or ID-style document, extract these fields when visible:
- WorkPermitName: residence status / permit label if clearly visible, such as 'Přechodný pobyt', 'Trvalý pobyt', 'Dočasná ochrana', 'Strpění'. Return the exact Czech status when visible.
- VisaNumber: document/card/permit number shown on the card or back side.
- VisaStartDate: issue / valid-from date in DD.MM.YYYY format if clearly visible.
- VisaExpiry: expiry / validity-until date in DD.MM.YYYY format.
- VisaAuthority: issuing authority / place of issue / institution if clearly visible. If you can clearly read 'MV ČR OAMP', return exactly 'MV ČR OAMP'.
- VisaType: if the document clearly contains a visa/residence type code, return it; otherwise leave empty.
- FirstName: holder first name if clearly visible
- LastName: holder last name if clearly visible
- BirthDate: holder birth date in DD.MM.YYYY if clearly visible

If this is not that document type, still extract the clearly visible fields and leave the rest empty.

Return ONLY valid JSON, no other text:
{""FirstName"":"""",""LastName"":"""",""BirthDate"":"""",""VisaNumber"":"""",""VisaAuthority"":"""",""VisaType"":"""",""VisaStartDate"":"""",""VisaExpiry"":"""",""WorkPermitName"":""""}",

                _ => "Describe what you see in this document image."
            };

            return prompt + @"

OPTIONAL ACCURACY FORMAT:
If possible, return the same fields in this richer JSON format. The app also supports the old flat JSON format, so do not omit the field names listed above.
{
  ""document_kind"": ""passport | visa_sticker | eu_id_card_front | eu_id_card_back | insurance_card | work_permit | residence_permit | unknown"",
  ""confidence"": 0.0,
  ""fields"": {
    ""FieldName"": { ""value"": """", ""confidence"": 0.0, ""source_label"": ""exact visible label near the value, e.g. Platí od / Valid from"" }
  }
}
Use confidence 0.0-1.0. If a value is unclear, leave it empty or use confidence below 0.60.";
        }

        public static Dictionary<string, string> ParseResponse(string response)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var json = ExtractJsonObject(response);
                if (string.IsNullOrWhiteSpace(json)) return result;

                using var doc = JsonDocument.Parse(json);
                ParseJsonObject(doc.RootElement, result);
            }
            catch (Exception ex)
            {
                LoggingService.LogError("AIScanPrompts.ParseResponse", ex);
            }
            return result;
        }

        public static string GetDocumentKind(IReadOnlyDictionary<string, string> parsed)
        {
            return parsed.TryGetValue(DocumentKindKey, out var kind) ? kind.Trim() : string.Empty;
        }

        public static bool IsDocumentKindCompatible(string expectedDocKey, IReadOnlyDictionary<string, string> parsed)
        {
            var kind = GetDocumentKind(parsed);
            if (string.IsNullOrWhiteSpace(kind) || string.Equals(kind, "unknown", StringComparison.OrdinalIgnoreCase))
                return true;

            kind = kind.Trim().ToLowerInvariant();
            expectedDocKey = (expectedDocKey ?? string.Empty).Trim().ToLowerInvariant();

            return expectedDocKey switch
            {
                "passport" => kind is "passport" or "eu_id_card_front" or "id_card_front",
                "id_card" => kind is "eu_id_card_front" or "id_card_front",
                "passport2" => kind is "passport_page2" or "eu_id_card_back" or "id_card_back" or "residence_permit",
                "id_card_back" => kind is "eu_id_card_back" or "id_card_back" or "residence_permit",
                "visa" => kind is "visa_sticker" or "visa" or "residence_permit",
                "visa2" => kind is "visa_sticker" or "visa" or "residence_permit" or "eu_id_card_back" or "id_card_back",
                "insurance" => kind is "insurance_card" or "insurance",
                "permit" => kind is "work_permit" or "permit",
                _ => true
            };
        }

        public static bool TryGetFieldConfidence(IReadOnlyDictionary<string, string> parsed, string fieldKey, out double confidence)
        {
            confidence = 1.0;
            if (!parsed.TryGetValue(ConfidencePrefix + fieldKey, out var raw) || string.IsNullOrWhiteSpace(raw))
                return false;

            raw = raw.Trim().Replace(',', '.');
            return double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out confidence);
        }

        public static bool IsLowConfidenceField(IReadOnlyDictionary<string, string> parsed, string fieldKey, double threshold = 0.60)
        {
            return TryGetFieldConfidence(parsed, fieldKey, out var confidence) && confidence < threshold;
        }

        public static bool IsSuspiciousFieldValue(
            IReadOnlyDictionary<string, string> parsed,
            string fieldKey,
            string value,
            string? currentValue = null)
        {
            if (IsSuspiciousDateValue(fieldKey, value, currentValue))
                return true;

            if (IsSuspiciousVisaStartDate(parsed, fieldKey, value))
                return true;

            if (LooksLikeVisaNumberUsedAsPassportNumber(parsed, fieldKey, value))
                return true;

            return false;
        }

        private static bool IsSuspiciousDateValue(string fieldKey, string value, string? currentValue)
        {
            if (!IsDateField(fieldKey) || string.IsNullOrWhiteSpace(value))
                return false;

            var parsedDate = DateParsingHelper.TryParseDate(value);
            if (parsedDate == null)
                return true;

            if (string.Equals(fieldKey, "BirthDate", StringComparison.OrdinalIgnoreCase))
                return parsedDate.Value.Date > DateTime.Today || parsedDate.Value.Year < 1900;

            if (fieldKey.EndsWith("Expiry", StringComparison.OrdinalIgnoreCase))
            {
                if (parsedDate.Value.Year < 2000)
                    return true;

                var currentDate = DateParsingHelper.TryParseDate(currentValue ?? string.Empty);
                if (currentDate != null && currentDate.Value.Date > DateTime.Today && parsedDate.Value.Date < DateTime.Today)
                    return true;
            }

            return false;
        }

        private static bool IsDateField(string fieldKey)
        {
            return fieldKey.EndsWith("Expiry", StringComparison.OrdinalIgnoreCase)
                || fieldKey.EndsWith("IssueDate", StringComparison.OrdinalIgnoreCase)
                || fieldKey.EndsWith("StartDate", StringComparison.OrdinalIgnoreCase)
                || string.Equals(fieldKey, "BirthDate", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsSuspiciousVisaStartDate(
            IReadOnlyDictionary<string, string> parsed,
            string fieldKey,
            string value)
        {
            if (!string.Equals(fieldKey, "VisaStartDate", StringComparison.OrdinalIgnoreCase))
                return false;

            var start = DateParsingHelper.TryParseDate(value);
            if (start == null)
                return true;

            var kind = GetDocumentKind(parsed);
            var isVisaSticker = string.IsNullOrWhiteSpace(kind)
                || kind.Contains("visa", StringComparison.OrdinalIgnoreCase)
                || kind.Contains("sticker", StringComparison.OrdinalIgnoreCase);
            var oldestReasonableYear = isVisaSticker ? 2010 : 1990;

            if (start.Value.Year < oldestReasonableYear || start.Value.Date > DateTime.Today.AddYears(2))
                return true;

            if (parsed.TryGetValue("VisaExpiry", out var expiryValue))
            {
                var expiry = DateParsingHelper.TryParseDate(expiryValue);
                if (expiry != null)
                {
                    if (start.Value.Date > expiry.Value.Date)
                        return true;

                    var maxDays = isVisaSticker ? 3650 : 36500;
                    if ((expiry.Value.Date - start.Value.Date).TotalDays > maxDays)
                        return true;
                }
            }

            if (parsed.TryGetValue(SourcePrefix + "VisaStartDate", out var sourceLabel))
            {
                var source = sourceLabel.Trim().ToLowerInvariant();
                if (source.Contains("mrz", StringComparison.OrdinalIgnoreCase)
                    || source.Contains("birth", StringComparison.OrdinalIgnoreCase)
                    || source.Contains("datum naro", StringComparison.OrdinalIgnoreCase)
                    || source.Contains("číslo", StringComparison.OrdinalIgnoreCase)
                    || source.Contains("cislo", StringComparison.OrdinalIgnoreCase)
                    || source.Contains("passport", StringComparison.OrdinalIgnoreCase)
                    || source.Contains("doclad", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static bool LooksLikeVisaNumberUsedAsPassportNumber(
            IReadOnlyDictionary<string, string> parsed,
            string fieldKey,
            string value)
        {
            if (!string.Equals(fieldKey, "PassportNumber", StringComparison.OrdinalIgnoreCase))
                return false;

            var normalizedValue = NormalizeDocumentNumber(value);
            if (string.IsNullOrWhiteSpace(normalizedValue))
                return false;

            if (parsed.TryGetValue("VisaNumber", out var visaNumber)
                && string.Equals(normalizedValue, NormalizeDocumentNumber(visaNumber), StringComparison.OrdinalIgnoreCase))
                return true;

            return Regex.IsMatch(normalizedValue, @"^\d{9}$");
        }

        private static string NormalizeDocumentNumber(string value)
        {
            return Regex.Replace(value.Trim().ToUpperInvariant(), @"[^A-Z0-9]+", string.Empty);
        }

        private static string ExtractJsonObject(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
                return string.Empty;

            var start = response.IndexOf('{');
            var end = response.LastIndexOf('}');
            return start >= 0 && end > start
                ? response[start..(end + 1)]
                : string.Empty;
        }

        private static void ParseJsonObject(JsonElement root, Dictionary<string, string> result)
        {
            if (root.ValueKind != JsonValueKind.Object)
                return;

            foreach (var prop in root.EnumerateObject())
            {
                if (string.Equals(prop.Name, "fields", StringComparison.OrdinalIgnoreCase)
                    && prop.Value.ValueKind == JsonValueKind.Object)
                {
                    ParseFieldsObject(prop.Value, result);
                    continue;
                }

                if (string.Equals(prop.Name, "document_kind", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(prop.Name, "DocumentKind", StringComparison.OrdinalIgnoreCase))
                {
                    AddIfUseful(result, DocumentKindKey, GetJsonValueAsString(prop.Value));
                    continue;
                }

                if (string.Equals(prop.Name, "confidence", StringComparison.OrdinalIgnoreCase))
                {
                    AddIfUseful(result, OverallConfidenceKey, GetJsonValueAsString(prop.Value));
                    continue;
                }

                if (prop.Value.ValueKind == JsonValueKind.Object)
                    AddFieldObject(prop.Name, prop.Value, result);
                else
                    AddIfUseful(result, prop.Name, GetJsonValueAsString(prop.Value));
            }
        }

        private static void ParseFieldsObject(JsonElement fields, Dictionary<string, string> result)
        {
            foreach (var field in fields.EnumerateObject())
            {
                if (field.Value.ValueKind == JsonValueKind.Object)
                    AddFieldObject(field.Name, field.Value, result);
                else
                    AddIfUseful(result, field.Name, GetJsonValueAsString(field.Value));
            }
        }

        private static void AddFieldObject(string fieldName, JsonElement fieldObject, Dictionary<string, string> result)
        {
            foreach (var part in fieldObject.EnumerateObject())
            {
                if (string.Equals(part.Name, "value", StringComparison.OrdinalIgnoreCase))
                    AddIfUseful(result, fieldName, GetJsonValueAsString(part.Value));
                else if (string.Equals(part.Name, "confidence", StringComparison.OrdinalIgnoreCase))
                    AddIfUseful(result, ConfidencePrefix + fieldName, GetJsonValueAsString(part.Value));
                else if (string.Equals(part.Name, "source_label", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(part.Name, "source", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(part.Name, "label", StringComparison.OrdinalIgnoreCase))
                    AddIfUseful(result, SourcePrefix + fieldName, GetJsonValueAsString(part.Value));
            }
        }

        private static string GetJsonValueAsString(JsonElement value)
        {
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString()?.Trim() ?? string.Empty,
                JsonValueKind.Number => value.GetRawText().Trim(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                _ => string.Empty
            };
        }

        private static void AddIfUseful(Dictionary<string, string> result, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            var normalized = value.Trim();
            if (string.Equals(normalized, "N/A", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "unknown", StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, "null", StringComparison.OrdinalIgnoreCase))
                return;

            result[key] = normalized;
        }

        public static string GetPdfFieldMappingPrompt(
            IEnumerable<PdfFormFieldBinding> fields,
            IEnumerable<TagEntry> tags)
        {
            var fieldList = fields
                .Where(f => !string.IsNullOrWhiteSpace(f.FieldName) || !string.IsNullOrWhiteSpace(f.DecodedFieldName) || !string.IsNullOrWhiteSpace(f.NearbyText))
                .OrderBy(f => f.Page)
                .ThenBy(f => f.Y)
                .ThenBy(f => f.X)
                .ToList();

            var tagList = tags
                .Where(t => !string.IsNullOrWhiteSpace(t.Tag))
                .OrderBy(t => t.Category)
                .ThenBy(t => t.Tag, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var prompt = new StringBuilder();
            prompt.AppendLine("You are a PDF form field mapping assistant for a Czech employment agency.");
            prompt.AppendLine("You analyze PDF form fields and suggest ONLY template text built from the provided tag catalog.");
            prompt.AppendLine();
            prompt.AppendLine("STRICT RULES:");
            prompt.AppendLine("- Use ONLY tags from the provided catalog.");
            prompt.AppendLine("- NEVER invent new tag names.");
            prompt.AppendLine("- Suggest mappings ONLY for empty fields (fields with no current template text).");
            prompt.AppendLine("- Field names are often Czech or Slovak. Translate mentally before matching.");
            prompt.AppendLine("- Prefer decoded_field_name and nearby_text over raw technical field_name if raw name looks broken or encoded.");
            prompt.AppendLine("- You MAY compose multiple tags into one field if the field logically requires multiple data parts.");
            prompt.AppendLine("- Prefer the SMALLEST correct data unit. Do NOT default to full address if the field asks only for street, city, ZIP, country, passport number, etc.");
            prompt.AppendLine("- If a field asks for passport number and issuing country, suggest those two parts only.");
            prompt.AppendLine("- If a match is weak but still useful, return it with confidence = low instead of omitting it.");
            prompt.AppendLine("- Omit the field only if there is truly no sensible suggestion.");
            prompt.AppendLine("- confidence must be one of: high, medium, low.");
            prompt.AppendLine();
            prompt.AppendLine("FORM FIELDS (from PDF):");
            for (var i = 0; i < fieldList.Count; i++)
            {
                var field = fieldList[i];
                var currentText = string.IsNullOrWhiteSpace(field.TemplateText) ? "currently empty" : $"currently: {field.TemplateText}";
                prompt.AppendLine($"{i + 1}. raw_field_name: \"{field.FieldName}\"");
                prompt.AppendLine($"   decoded_field_name: \"{field.DecodedFieldName}\"");
                prompt.AppendLine($"   nearby_text: \"{field.NearbyText}\"");
                prompt.AppendLine($"   meta: ({field.FieldType}, P{Math.Max(1, field.Page + 1)} X:{Math.Round(field.X)} Y:{Math.Round(field.Y)}) - {currentText}");
            }

            prompt.AppendLine();
            prompt.AppendLine("AVAILABLE TAGS:");
            foreach (var tag in tagList)
                prompt.AppendLine($"- ${{{tag.Tag}}} — {tag.Description}");

            prompt.AppendLine();
            prompt.AppendLine("Return ONLY a valid JSON array. Each element must have:");
            prompt.AppendLine("  field_name       - exact PDF field name");
            prompt.AppendLine("  suggested_text   - exact template text to insert, may contain one OR multiple tags");
            prompt.AppendLine("  tags_used        - array of tag names used inside suggested_text");
            prompt.AppendLine("  reason           - short human explanation");
            prompt.AppendLine("  confidence       - high / medium / low");
            prompt.AppendLine();
            prompt.AppendLine("Example:");
            prompt.AppendLine("[");
            prompt.AppendLine("  {");
            prompt.AppendLine("    \"field_name\": \"Jméno\",");
            prompt.AppendLine("    \"suggested_text\": \"${EMPLOYEE_FirstName}\",");
            prompt.AppendLine("    \"tags_used\": [\"EMPLOYEE_FirstName\"],");
            prompt.AppendLine("    \"reason\": \"Field label Jméno means first name.\",");
            prompt.AppendLine("    \"confidence\": \"high\"");
            prompt.AppendLine("  },");
            prompt.AppendLine("  {");
            prompt.AppendLine("    \"field_name\": \"Ulice a číslo\",");
            prompt.AppendLine("    \"suggested_text\": \"${EMPLOYEE_LocalAddress_Street} ${EMPLOYEE_LocalAddress_Number}\",");
            prompt.AppendLine("    \"tags_used\": [\"EMPLOYEE_LocalAddress_Street\", \"EMPLOYEE_LocalAddress_Number\"],");
            prompt.AppendLine("    \"reason\": \"Field asks for street and number, not full address.\",");
            prompt.AppendLine("    \"confidence\": \"high\"");
            prompt.AppendLine("  }");
            prompt.AppendLine("]");

            return prompt.ToString();
        }
    }
}
