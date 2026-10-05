using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using iTextSharp.text;
using iTextSharp.text.pdf;
using Win11DesktopApp.Helpers;
using Win11DesktopApp.Models;
using Win11DesktopApp.Services;

namespace Win11DesktopApp.Tests
{
    public class ITextFormHelperTests : IDisposable
    {
        private readonly string _root;

        public ITextFormHelperTests()
        {
            _root = Path.Combine(Path.GetTempPath(), "ITextFormTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [Fact]
        public void ReadAndFill_WritesCzechTextAndCheckboxIntoAcroFormFields()
        {
            var templatePath = Path.Combine(_root, "form.pdf");
            CreateSampleForm(templatePath);
            var originalAppearance = ReadDefaultAppearance(templatePath, "Textové pole");

            var bindings = ITextFormHelper.ReadFieldBindings(templatePath);
            var nameField = Assert.Single(bindings, field => field.FieldType == "text");
            var checkField = Assert.Single(bindings, field => field.FieldType == "checkbox");

            Assert.Equal("Textové pole", nameField.FieldName);
            Assert.Equal(0, nameField.Page);
            Assert.True(nameField.Width > 0);
            Assert.True(nameField.Height > 0);
            Assert.Equal("Souhlas", checkField.FieldName);

            var encodedName = "Textov#C3#A9 pole";
            var outputPath = Path.Combine(_root, "filled.pdf");
            var result = ITextFormHelper.TryFillFormFields(
                templatePath,
                outputPath,
                new[]
                {
                    new PdfFormFieldBinding
                    {
                        FieldName = encodedName,
                        FieldType = "text",
                        TemplateText = "${FullName}"
                    },
                    new PdfFormFieldBinding
                    {
                        FieldName = checkField.FieldName,
                        FieldType = "checkbox",
                        TemplateText = "ano"
                    }
                },
                new Dictionary<string, string> { ["FullName"] = "Jan Novák" });

            Assert.True(result.Success);
            Assert.Empty(result.FailedFields);

            var reader = new PdfReader(outputPath);
            try
            {
                Assert.Equal("Jan Novák", reader.AcroFields.GetField(nameField.FieldName));
                Assert.Equal(originalAppearance, ReadDefaultAppearance(reader, nameField.FieldName));
                var checkValue = reader.AcroFields.GetField(checkField.FieldName);
                Assert.False(string.IsNullOrWhiteSpace(checkValue));
                Assert.NotEqual("Off", checkValue);
            }
            finally
            {
                reader.Close();
            }
        }

        [Fact]
        public void FillChoice_SelectsExistingOptionWithoutReplacingListFont()
        {
            var templatePath = Path.Combine(_root, "choice.pdf");
            CreateChoiceForm(templatePath);
            var originalAppearance = ReadDefaultAppearance(templatePath, "Pohlavi");

            var outputPath = Path.Combine(_root, "choice-filled.pdf");
            var result = ITextFormHelper.TryFillFormFields(
                templatePath,
                outputPath,
                new[]
                {
                    new PdfFormFieldBinding
                    {
                        FieldName = "Pohlavi",
                        FieldType = "choice",
                        TemplateText = "muž"
                    }
                },
                new Dictionary<string, string>());

            Assert.True(result.Success);
            Assert.Empty(result.FailedFields);

            var reader = new PdfReader(outputPath);
            try
            {
                Assert.Equal("Muž", reader.AcroFields.GetField("Pohlavi"));
                Assert.Equal(new[] { "Muž", "Žena", "Jiné" }, reader.AcroFields.GetListOptionDisplay("Pohlavi"));
                Assert.Equal(originalAppearance, ReadDefaultAppearance(reader, "Pohlavi"));
            }
            finally
            {
                reader.Close();
            }
        }

        [Fact]
        public void GeneratePdf_OverlayWritesMovableAdobeTextOnTheTag()
        {
            var templatePath = Path.Combine(_root, "overlay.pdf");
            CreateBlankPage(templatePath);
            var tagMapPath = Path.ChangeExtension(templatePath, ".tags.json");
            File.WriteAllText(tagMapPath, JsonSerializer.Serialize(new PdfTagMap
            {
                Mode = "overlay",
                Placements = new List<PdfTagPlacement>
                {
                    new()
                    {
                        Tag = "FullName",
                        Page = 0,
                        X = 0.2,
                        Y = 0.3,
                        FontSize = 10
                    }
                }
            }));

            var outputPath = Path.Combine(_root, "overlay-filled.pdf");
            new DocumentGenerationService().GeneratePdf(
                templatePath,
                outputPath,
                new Dictionary<string, string> { ["FullName"] = "Jan Novák" });

            var reader = new PdfReader(outputPath);
            try
            {
                Assert.Null(reader.AcroFields.GetField("WnLine1"));
                var annots = reader.GetPageN(1).GetAsArray(PdfName.Annots);
                Assert.NotNull(annots);
                Assert.True(annots.Size > 0);
                var annot = (PdfDictionary)PdfReader.GetPdfObject(annots[0]);
                Assert.Equal(PdfName.Freetext, annot.GetAsName(PdfName.Subtype));
                Assert.Equal("Jan Novák", annot.GetAsString(PdfName.Contents).ToUnicodeString());
                var box = annot.GetAsArray(PdfName.Rect);
                var page = reader.GetPageSize(1);
                var llx = box.GetAsNumber(0).FloatValue;
                var lly = box.GetAsNumber(1).FloatValue;
                var urx = box.GetAsNumber(2).FloatValue;
                var ury = box.GetAsNumber(3).FloatValue;
                Assert.InRange(llx, page.Width * 0.2f - 1f, page.Width * 0.2f + 1f);
                Assert.InRange(ury, page.Height * 0.7f - 1f, page.Height * 0.7f + 1f);
                var (_, cellHeight) = ITextFormHelper.MeasureEditorGlyphCell("Arial", 10);
                Assert.InRange(ury - lly, (float)cellHeight - 0.2f, (float)cellHeight + 0.2f);
                var fontPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf");
                var font = File.Exists(fontPath)
                    ? BaseFont.CreateFont(fontPath, BaseFont.CP1250, BaseFont.EMBEDDED)
                    : BaseFont.CreateFont(BaseFont.HELVETICA, BaseFont.WINANSI, BaseFont.NOT_EMBEDDED);
                var expectedWidth = font.GetWidthPoint("Jan Novák", 10);
                Assert.InRange(urx - llx, expectedWidth - 0.5f, expectedWidth + 1.5f);
                var padding = annot.GetAsArray(new PdfName("RD"));
                Assert.NotNull(padding);
                Assert.Equal(4, padding.Size);
                for (var i = 0; i < padding.Size; i++)
                    Assert.Equal(0f, padding.GetAsNumber(i).FloatValue);
            }
            finally
            {
                reader.Close();
            }
        }

        [Fact]
        public void GeneratePdf_FormModeUsesFieldValues()
        {
            var templatePath = Path.Combine(_root, "template.pdf");
            CreateSampleForm(templatePath);
            var tagMapPath = Path.ChangeExtension(templatePath, ".tags.json");
            File.WriteAllText(tagMapPath, JsonSerializer.Serialize(new PdfTagMap
            {
                Mode = "form",
                FormFields = new List<PdfFormFieldBinding>
                {
                    new()
                    {
                        FieldName = "Textové pole",
                        FieldType = "text",
                        TemplateText = "${City}"
                    }
                }
            }));

            var outputPath = Path.Combine(_root, "generated.pdf");
            new DocumentGenerationService().GeneratePdf(
                templatePath,
                outputPath,
                new Dictionary<string, string> { ["City"] = "Brno" });

            var reader = new PdfReader(outputPath);
            try
            {
                Assert.Equal("Brno", reader.AcroFields.GetField("Textové pole"));
            }
            finally
            {
                reader.Close();
            }
        }

        private static void CreateBlankPage(string path)
        {
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            var document = new Document(PageSize.A4);
            var writer = PdfWriter.GetInstance(document, stream);
            document.Open();
            document.Add(new Paragraph(" "));
            document.Close();
            writer.Close();
        }

        private static void CreateSampleForm(string path)
        {
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            var document = new Document(PageSize.A4);
            var writer = PdfWriter.GetInstance(document, stream);
            document.Open();
            document.Add(new Paragraph("Jméno"));

            var text = new TextField(writer, new Rectangle(120, 760, 320, 790), "Textové pole");
            writer.AddAnnotation(text.GetTextField());

            var check = new RadioCheckField(writer, new Rectangle(50, 720, 68, 738), "Souhlas", "Yes");
            check.CheckType = RadioCheckField.TYPE_CHECK;
            writer.AddAnnotation(check.CheckField);

            document.Close();
        }

        private static void CreateChoiceForm(string path)
        {
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            var document = new Document(PageSize.A4);
            var writer = PdfWriter.GetInstance(document, stream);
            document.Open();

            var combo = new TextField(writer, new Rectangle(120, 700, 280, 720), "Pohlavi");
            combo.Choices = new[] { "Muž", "Žena", "Jiné" };
            combo.ChoiceExports = new[] { "Muž", "Žena", "Jiné" };
            writer.AddAnnotation(combo.GetComboField());

            document.Close();
        }

        private static string ReadDefaultAppearance(string path, string fieldName)
        {
            var reader = new PdfReader(path);
            try
            {
                return ReadDefaultAppearance(reader, fieldName);
            }
            finally
            {
                reader.Close();
            }
        }

        private static string ReadDefaultAppearance(PdfReader reader, string fieldName)
        {
            var item = reader.AcroFields.GetFieldItem(fieldName);
            return item.GetMerged(0).GetAsString(PdfName.Da)?.ToUnicodeString() ?? string.Empty;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root))
                    Directory.Delete(_root, true);
            }
            catch
            {
            }
        }
    }
}
