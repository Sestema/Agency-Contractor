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
- CRITICAL: If the MRZ is blurry, cut off, partially hidden, or unreadable, return PassportNumber as an empty string. NEVER guess.
- CRITICAL: Do NOT take PassportNumber from any visa, permit, insurance card, or any other document that may appear on the same image.
- If both the printed passport number and the MRZ passport number are clearly visible, prefer the value that matches both. If they conflict, return empty instead of guessing.

STEP 3: Find place of birth (printed text, NOT from MRZ). Look for the field labeled ""Place of birth"" or ""Місце народження"". Read the REGION/OBLAST name (e.g. ОДЕСЬКА ОБЛ. = Odesa, КИЇВ = Kyiv, ЛЬВІВСЬКА ОБЛ. = Lviv). Output ONLY the city name in Latin, never the oblast/region word.

STEP 4: Find issuing authority / document issuer. Look for labels like ""Authority"", ""Orgán vydávající doklad"", ""Орган, що видав"", ""Орган выдачи"". If the document shows only a numeric code (for example 5142), return that code exactly. Do NOT invent a full institution name.

Return ONLY this JSON (FirstName=given name, LastName=surname):
{""FirstName"":"""",""LastName"":"""",""BirthDate"":"""",""PassportNumber"":"""",""PassportAuthority"":"""",""PassportCity"":"""",""PassportCountry"":"""",""PassportExpiry"":""""}",

                "insurance" => @"Read this insurance card/document photo. CRITICAL: ALL output must be in Latin alphabet ONLY, never Cyrillic.

This is a Czech health insurance card (průkaz pojištěnce). Extract these fields:
- FirstName: holder's first/given name if it is clearly printed on the card. If not clearly visible, leave empty.
- LastName: holder's last name/surname if it is clearly printed on the card. If not clearly visible, leave empty.
- BirthDate: holder's birth date in DD.MM.YYYY ONLY if it is explicitly printed on the card as a date. If it is NOT explicitly printed, leave empty. Do NOT derive it from the insurance number.
- InsuranceCompanyShort: short name of the insurance company (e.g. VZP CR, ZPMV, OZP, CPZP)
- InsuranceNumber: the field labeled ""Číslo pojištěnce"" (personal insurance number, usually 10 digits like birth number). This is NOT ""Číslo průkazu"" (card number which is much longer). Look specifically for ""Číslo pojištěnce"" or ""Cislo pojistence"".
- InsuranceExpiry: the ""Do:"" date (expiry) in DD.MM.YYYY format

Return ONLY valid JSON, no other text:
{""FirstName"":"""",""LastName"":"""",""BirthDate"":"""",""InsuranceCompanyShort"":"""",""InsuranceNumber"":"""",""InsuranceExpiry"":""""}",

                "visa" => @"Read this Czech visa/residence permit document photo. CRITICAL: ALL output in Latin alphabet ONLY, never Cyrillic.

IMPORTANT: The photo may contain MULTIPLE documents on one page. You MUST identify the correct one:

PRIORITY RULE: IGNORE any ""Osvědčení o registraci"" (EU registration certificate) — it is NOT the residence permit.
INSTEAD, look for one of these:

TYPE A — VISA STICKER (for Ukrainian refugees):
A colorful sticker with ""Číslo víza"", ""Druh víza"", hologram, MRZ code at bottom.
- FirstName: visa holder's given name in Latin alphabet if clearly visible
- LastName: visa holder's surname in Latin alphabet if clearly visible
- BirthDate: holder's birth date in DD.MM.YYYY if clearly visible
- PassportNumber: passport number shown on the visa sticker or in the MRZ if clearly visible
- VisaNumber: 9-digit visa number near ""Číslo víza""
- VisaAuthority: if a clear issuing authority is visible, extract it. If you can clearly read ""MV ČR OAMP"" anywhere on the visa or related authority text, return exactly ""MV ČR OAMP"". Otherwise leave empty.
- VisaType: FULL code with slashes like D/DO/667, D/DO/668, D/DO/669, D/DO/767-769, D/DO/867-869, D/VS/91, D/SD/91. NOT just ""D"".
- VisaExpiry: ""Do/To"" date in DD.MM.YYYY
- WorkPermitName: If D/DO/ → ""Dočasná ochrana"", if D/VS/ or D/SD/ → ""Strpění""

TYPE B — RESIDENCE PERMIT STAMP from MV ČR OAMP (for EU citizens):
A rectangular official stamp with ""MV ČR OAMP"" and header text ""POVOLENÍ K ... POBYTU NA ÚZEMÍ"".
This stamp is the MOST IMPORTANT document on the page. Read its header line carefully:
- FirstName: holder's given name in Latin alphabet if clearly visible
- LastName: holder's surname in Latin alphabet if clearly visible
- BirthDate: holder's birth date in DD.MM.YYYY if clearly visible
- PassportNumber: passport/document number linked to this permit if clearly visible
- VisaNumber: the permit/registration number (alphanumeric code, e.g. ""VB 027159"", may appear on the stamp or on the registration certificate above)
- VisaAuthority: if the stamp/authority text shows ""MV ČR OAMP"", return exactly ""MV ČR OAMP"". Otherwise return the clearly visible issuing authority text if present.
- VisaType: leave empty """"
- VisaExpiry: the validity date in DD.MM.YYYY from the stamp (look for ""do"" or a date written by hand)
- WorkPermitName: ONLY based on the MV ČR OAMP stamp header text:
  If stamp says ""PŘECHODNÉMU POBYTU"" → output exactly ""Přechodný pobyt""
  If stamp says ""TRVALÉMU POBYTU"" → output exactly ""Trvalý pobyt""
  NEVER output ""Osvědčení o registraci"" — that is wrong.

Return ONLY valid JSON, no other text:
{""FirstName"":"""",""LastName"":"""",""BirthDate"":"""",""PassportNumber"":"""",""VisaNumber"":"""",""VisaAuthority"":"""",""VisaType"":"""",""VisaExpiry"":"""",""WorkPermitName"":""""}",

                "permit" => @"Read this Czech work permit document (Povolení k zaměstnání / ROZHODNUTÍ). CRITICAL: ALL output must be in Latin alphabet ONLY, never Cyrillic. Read ALL pages of the document.

This is typically a multi-page official document (ROZHODNUTÍ) issued by Úřad práce České republiky (Czech Labour Office).

STEP 0 — Find the employee identity if clearly visible:
- FirstName: employee's given name in Latin alphabet if clearly visible
- LastName: employee's surname in Latin alphabet if clearly visible
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

Return ONLY valid JSON, no other text:
{""FirstName"":"""",""LastName"":"""",""BirthDate"":"""",""WorkPermitName"":"""",""WorkPermitNumber"":"""",""WorkPermitType"":"""",""WorkPermitIssueDate"":"""",""WorkPermitExpiry"":"""",""WorkPermitAuthority"":""""}",

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

STEP 6: Find issuing authority / document issuer. Look for labels like 'Authority', 'Orgán vydávající doklad', 'Vydal', 'Eliberată de', or a dedicated issuer code. If the card shows only a code, return the code exactly.

Return ONLY this JSON (PassportExpiry = card expiry date):
{""FirstName"":"""",""LastName"":"""",""BirthDate"":"""",""PassportNumber"":"""",""PassportAuthority"":"""",""PassportCity"":"""",""PassportCountry"":"""",""PassportExpiry"":""""}",

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
