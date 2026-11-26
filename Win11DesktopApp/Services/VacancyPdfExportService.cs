using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Win11DesktopApp.Models;

namespace Win11DesktopApp.Services
{
    /// <summary>Words used in the PDF, in the current language of the app.</summary>
    public sealed class VacancyPdfLabels
    {
        public string Urgent { get; init; } = "ТЕРМІНОВО";
        public string FromFmt { get; init; } = "з {0}";
        public string MenFmt { get; init; } = "{0} чол.";
        public string WomenFmt { get; init; } = "{0} жін.";
        public string CouplesFmt { get; init; } = "{0} пари";
        public string AnyoneFmt { get; init; } = "{0} будь-хто";
        public string ListTitle { get; init; } = "Вільні вакансії";
        public string NeedFmt { get; init; } = "Потрібно: {0}";
        public string Conditions { get; init; } = "Умови";
        public string Contact { get; init; } = "Контакт";
    }

    /// <summary>One position as job seekers see it: no firm name, no private notes.</summary>
    public sealed class VacancyPdfItem
    {
        /// <summary>Shown only in the list of all positions; the poster leaves it out.</summary>
        public string FirmName { get; init; } = string.Empty;
        public string Position { get; init; } = string.Empty;
        public string City { get; init; } = string.Empty;
        public string Pay { get; init; } = string.Empty;
        public string NeededFrom { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public IReadOnlyList<string> Conditions { get; init; } = Array.Empty<string>();
        public int Men { get; init; }
        public int Women { get; init; }
        public int Couples { get; init; }
        public int Anyone { get; init; }
    }

    public static class VacancyPdfExportService
    {
        private const string Green = "#1D9E75";
        private const string GreenDark = "#0F6E56";
        private const string Ink = "#2C2C2A";
        private const string Muted = "#5F5E5A";
        private const string Line = "#D3D1C7";
        private const string BlueBg = "#E6F1FB";
        private const string BlueText = "#0C447C";
        private const string PinkBg = "#FBEAF0";
        private const string PinkText = "#72243E";
        private const string PurpleBg = "#EEEDFE";
        private const string PurpleText = "#3C3489";
        private const string GreyBg = "#F1EFE8";
        private const string GreenBg = "#E1F5EE";
        private const string GreenText = "#085041";

        /// <summary>A bright A4 poster for one position.</summary>
        public static void GeneratePoster(string outputPath, VacancyPdfItem item, VacancyPageSettings contact, VacancyPdfLabels labels)
        {
            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(0);
                    page.DefaultTextStyle(style => style.FontFamily("Segoe UI").FontSize(14).FontColor(Ink));

                    page.Content().Column(column =>
                    {
                        column.Item().Background(Green).PaddingHorizontal(40).PaddingVertical(34).Column(header =>
                        {
                            var top = string.IsNullOrWhiteSpace(item.NeededFrom)
                                ? labels.Urgent
                                : $"{labels.Urgent} · {string.Format(CultureInfo.CurrentCulture, labels.FromFmt, item.NeededFrom)}";
                            header.Item().Text(top).FontSize(13).Bold().FontColor(Colors.White).LetterSpacing(0.05f);
                            header.Item().PaddingTop(6).Text(item.Position).FontSize(36).Bold().FontColor(Colors.White);
                            if (!string.IsNullOrWhiteSpace(item.City))
                                header.Item().PaddingTop(4).Text(item.City).FontSize(18).FontColor(Colors.White);
                        });

                        column.Item().PaddingHorizontal(40).PaddingTop(28).Column(body =>
                        {
                            if (!string.IsNullOrWhiteSpace(item.Pay))
                                body.Item().Text(item.Pay).FontSize(40).Bold().FontColor(GreenDark);

                            body.Item().PaddingTop(12).Row(row =>
                            {
                                foreach (var (text, bg, fg) in Chips(item, labels))
                                    row.AutoItem().PaddingRight(8).Background(bg).PaddingHorizontal(14).PaddingVertical(6)
                                        .Text(text).FontSize(16).Bold().FontColor(fg);
                            });

                            if (!string.IsNullOrWhiteSpace(item.Description))
                                body.Item().PaddingTop(22).Text(item.Description).FontSize(15).LineHeight(1.35f);

                            if (item.Conditions.Count > 0)
                            {
                                body.Item().PaddingTop(22).Text(labels.Conditions).FontSize(13).Bold().FontColor(Muted);
                                foreach (var condition in item.Conditions)
                                {
                                    body.Item().PaddingTop(8).Row(row =>
                                    {
                                        row.ConstantItem(26).Text("•").FontSize(20).Bold().FontColor(Green);
                                        row.RelativeItem().Text(condition).FontSize(17);
                                    });
                                }
                            }
                        });
                    });

                    page.Footer().PaddingHorizontal(40).PaddingBottom(36).Column(footer =>
                    {
                        footer.Item().LineHorizontal(1).LineColor(Line);
                        footer.Item().PaddingTop(14).Row(row =>
                        {
                            row.RelativeItem().Column(left =>
                            {
                                if (!string.IsNullOrWhiteSpace(contact.ContactPhone))
                                    left.Item().Text(contact.ContactPhone).FontSize(28).Bold();
                                if (!string.IsNullOrWhiteSpace(contact.ContactNote))
                                    left.Item().Text(contact.ContactNote).FontSize(14).FontColor(Muted);
                            });
                            if (!string.IsNullOrWhiteSpace(contact.AgencyName))
                                row.AutoItem().AlignBottom().Text(contact.AgencyName).FontSize(13).FontColor(Muted);
                        });
                    });
                });
            }).GeneratePdf(outputPath);
        }

        /// <summary>
        /// All given positions on A4 pages, one card per position: firm and pay on top, then position and
        /// city, how many people are still needed and a few words about the work. A card never splits.
        /// </summary>
        public static void GenerateList(string outputPath, IReadOnlyList<VacancyPdfItem> items, VacancyPageSettings contact, VacancyPdfLabels labels, string subtitle)
        {
            Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(0);
                    page.DefaultTextStyle(style => style.FontFamily("Segoe UI").FontSize(12).FontColor(Ink));

                    page.Header().Background(Green).PaddingHorizontal(36).PaddingVertical(22).Column(header =>
                    {
                        header.Item().Text(labels.ListTitle).FontSize(28).Bold().FontColor(Colors.White);
                        var sub = string.Join(" · ", new[] { subtitle, contact.AgencyName }.Where(text => !string.IsNullOrWhiteSpace(text)));
                        if (sub.Length > 0)
                            header.Item().Text(sub).FontSize(13).FontColor(Colors.White);
                    });

                    page.Content().PaddingHorizontal(36).PaddingVertical(18).Column(column =>
                    {
                        column.Spacing(12);
                        foreach (var item in items)
                            column.Item().ShowEntire().Element(card => Card(card, item, labels));
                    });

                    page.Footer().Background(GreenBg).PaddingHorizontal(36).PaddingVertical(12).Row(row =>
                    {
                        row.RelativeItem().AlignMiddle().Text(text =>
                        {
                            if (!string.IsNullOrWhiteSpace(contact.ContactPhone))
                                text.Span(contact.ContactPhone).FontSize(18).Bold().FontColor(GreenText);
                            if (!string.IsNullOrWhiteSpace(contact.ContactNote))
                                text.Span("   " + contact.ContactNote).FontSize(12).FontColor(GreenDark);
                        });
                        row.AutoItem().AlignMiddle().Text(text =>
                        {
                            text.CurrentPageNumber().FontSize(10).FontColor(GreenDark);
                            text.Span(" / ").FontSize(10).FontColor(GreenDark);
                            text.TotalPages().FontSize(10).FontColor(GreenDark);
                        });
                    });
                });
            }).GeneratePdf(outputPath);
        }

        private static void Card(IContainer container, VacancyPdfItem item, VacancyPdfLabels labels)
        {
            container.Border(1).BorderColor(Line).Background(Colors.White).PaddingHorizontal(16).PaddingVertical(12).Column(card =>
            {
                card.Item().Row(row =>
                {
                    var firm = string.IsNullOrWhiteSpace(item.FirmName) ? item.Position : item.FirmName;
                    row.RelativeItem().AlignMiddle().Text(firm).FontSize(17).Bold();
                    if (!string.IsNullOrWhiteSpace(item.Pay))
                        row.AutoItem().AlignMiddle().PaddingLeft(10).Background(GreenBg).PaddingHorizontal(10).PaddingVertical(4)
                            .Text(item.Pay).FontSize(15).Bold().FontColor(GreenText);
                });

                card.Item().PaddingTop(4).Text(text =>
                {
                    text.Span(item.Position).FontSize(13);
                    if (!string.IsNullOrWhiteSpace(item.City))
                    {
                        text.Span("  ·  ").FontSize(13).FontColor(Muted);
                        text.Span(item.City).FontSize(13).Bold();
                    }
                    if (!string.IsNullOrWhiteSpace(item.NeededFrom))
                    {
                        text.Span("  ·  ").FontSize(13).FontColor(Muted);
                        text.Span(string.Format(CultureInfo.CurrentCulture, labels.FromFmt, item.NeededFrom)).FontSize(13).FontColor(Muted);
                    }
                });

                var people = string.Join(", ", Chips(item, labels).Select(chip => chip.Text));
                if (people.Length > 0)
                    card.Item().PaddingTop(7).Row(row =>
                        row.AutoItem().Background(BlueBg).PaddingHorizontal(9).PaddingVertical(3)
                            .Text(string.Format(CultureInfo.CurrentCulture, labels.NeedFmt, people)).FontSize(13).Bold().FontColor(BlueText));

                var about = AboutText(item);
                if (about.Length > 0)
                    card.Item().PaddingTop(7).Text(about).FontSize(12).FontColor(Muted).LineHeight(1.3f);
            });
        }

        /// <summary>A few words about the work: the description, or the conditions when there is none.</summary>
        internal static string AboutText(VacancyPdfItem item)
        {
            if (!string.IsNullOrWhiteSpace(item.Description))
                return item.Description.Trim();
            return string.Join(" · ", item.Conditions.Where(line => !string.IsNullOrWhiteSpace(line)).Select(line => line.Trim()));
        }

        private static IEnumerable<(string Text, string Bg, string Fg)> Chips(VacancyPdfItem item, VacancyPdfLabels labels)
        {
            if (item.Men > 0) yield return (string.Format(CultureInfo.CurrentCulture, labels.MenFmt, item.Men), BlueBg, BlueText);
            if (item.Women > 0) yield return (string.Format(CultureInfo.CurrentCulture, labels.WomenFmt, item.Women), PinkBg, PinkText);
            if (item.Couples > 0) yield return (string.Format(CultureInfo.CurrentCulture, labels.CouplesFmt, item.Couples), PurpleBg, PurpleText);
            if (item.Anyone > 0) yield return (string.Format(CultureInfo.CurrentCulture, labels.AnyoneFmt, item.Anyone), GreyBg, Ink);
        }
    }
}
