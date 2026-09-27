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
