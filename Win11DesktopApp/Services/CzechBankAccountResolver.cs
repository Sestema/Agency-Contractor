using System;
using System.Collections.Generic;
using System.Linq;

namespace Win11DesktopApp.Services
{
    public static class CzechBankAccountResolver
    {
        private static readonly Dictionary<string, string> _bankNamesByCode = new(StringComparer.Ordinal)
        {
            ["0100"] = "Komerční banka, a.s.",
            ["0300"] = "Československá obchodní banka, a. s.",
            ["0600"] = "MONETA Money Bank, a.s.",
            ["0710"] = "ČESKÁ NÁRODNÍ BANKA",
            ["0800"] = "Česká spořitelna, a.s.",
            ["2010"] = "Fio banka, a.s.",
            ["2060"] = "Citfin, spořitelní družstvo",
            ["2070"] = "TRINITY BANK a.s.",
            ["2100"] = "ČSOB Hypoteční banka, a.s.",
            ["2200"] = "Peněžní dům, spořitelní družstvo",
            ["2220"] = "Artesa, spořitelní družstvo",
            ["2250"] = "Banka CREDITAS a.s.",
            ["2260"] = "NEY spořitelní družstvo",
            ["2600"] = "Citibank Europe plc, organizační složka",
            ["2700"] = "UniCredit Bank Czech Republic and Slovakia, a.s.",
            ["3030"] = "Air Bank a.s.",
            ["3060"] = "PKO BP S.A., Czech Branch",
            ["3500"] = "ING Bank N.V.",
            ["4300"] = "Národní rozvojová banka, a.s.",
            ["5500"] = "Raiffeisenbank a.s.",
            ["5800"] = "J&T BANKA, a.s.",
            ["6000"] = "PPF banka a.s.",
            ["6200"] = "COMMERZBANK Aktiengesellschaft, pobočka Praha",
            ["6210"] = "mBank S.A., organizační složka",
            ["6300"] = "BNP Paribas S.A., pobočka Česká republika",
            ["6363"] = "Partners Banka, a.s.",
            ["6600"] = "Banking Circle S.A., Czech Republic",
            ["6700"] = "Všeobecná úverová banka a.s., pobočka Praha",
            ["6800"] = "Sberbank CZ, a.s. v likvidaci",
            ["7910"] = "Deutsche Bank Aktiengesellschaft Filiale Prag, organizační složka",
            ["7950"] = "Raiffeisen stavební spořitelna a.s.",
            ["7960"] = "ČSOB Stavební spořitelna, a.s.",
            ["7970"] = "MONETA Stavební Spořitelna, a.s.",
            ["7990"] = "Modrá pyramida stavební spořitelna, a.s.",
            ["8030"] = "Volksbank Raiffeisenbank Nordoberpfalz eG pobočka Cheb",
            ["8040"] = "Oberbank AG pobočka Česká republika",
            ["8060"] = "Stavební spořitelna České spořitelny, a.s.",
            ["8090"] = "Česká exportní banka, a.s.",
            ["8150"] = "HSBC Continental Europe, Czech Republic",
            ["8190"] = "Sparkasse Oberlausitz-Niederschlesien",
            ["8198"] = "FAS finance company s.r.o.",
            ["8220"] = "Payment execution s.r.o.",
            ["8250"] = "Bank of China (CEE) Ltd. Prague Branch",
            ["8255"] = "Bank of Communications Co., Ltd., Prague Branch odštěpný závod",
            ["8265"] = "Industrial and Commercial Bank of China Limited, Prague Branch, odštěpný závod",
            ["8500"] = "Multitude Bank p.l.c.",
            ["8610"] = "Devizová burza a.s.",
            ["8660"] = "PAYMONT, UAB"
        };

        public static string ExtractBankCode(string? accountNumber)
        {
            if (string.IsNullOrWhiteSpace(accountNumber))
                return string.Empty;

            var slashIndex = accountNumber.LastIndexOf('/');
            if (slashIndex < 0 || slashIndex >= accountNumber.Length - 1)
                return string.Empty;

            var code = NormalizeDigits(accountNumber[(slashIndex + 1)..]);
            return code.Length == 4 ? code : string.Empty;
        }

        public static bool TryResolveBankName(string? accountNumber, out string bankName)
        {
            var code = ExtractBankCode(accountNumber);
            if (!string.IsNullOrEmpty(code) && _bankNamesByCode.TryGetValue(code, out var resolved))
            {
                bankName = resolved;
                return true;
            }

            bankName = string.Empty;
            return false;
        }

        private static string NormalizeDigits(string? value)
            => new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
    }
}
