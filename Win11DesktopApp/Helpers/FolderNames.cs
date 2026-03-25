namespace Win11DesktopApp.Helpers
{
    /// <summary>
    /// Provides localized folder names based on the document language.
    /// Also provides fallback names to search for folders created in another language.
    /// </summary>
    public static class FolderNames
    {
        // Ukrainian folder names
        private const string Employees_UK = "Працівники";
        private const string Templates_UK = "Шаблони";
        private const string Archive_UK = "Архів";
        private const string Payment_UK = "Виплата";
        private const string Candidates_UK = "Кандидати";
        private const string RecentlyDeleted_UK = "Недавно видалені";

        // English folder names
        private const string Employees_EN = "Employees";
        private const string Templates_EN = "Templates";
        private const string Archive_EN = "Archive";
        private const string Payment_EN = "Payment";
        private const string Candidates_EN = "Candidates";
        private const string RecentlyDeleted_EN = "Recently Deleted";

        // Czech folder names
        private const string Employees_CS = "Zaměstnanci";
        private const string Templates_CS = "Šablony";
        private const string Archive_CS = "Archiv";
        private const string Payment_CS = "Platba";
        private const string Candidates_CS = "Kandidáti";
        private const string RecentlyDeleted_CS = "Nedávno smazané";

        // Russian folder names
        private const string Employees_RU = "Сотрудники";
        private const string Templates_RU = "Шаблоны";
        private const string Archive_RU = "Архив";
        private const string Payment_RU = "Выплата";
        private const string Candidates_RU = "Кандидаты";
        private const string RecentlyDeleted_RU = "Недавно удаленные";

        public static string GetEmployeesFolder(string langCode) => langCode switch
        {
            "uk" => Employees_UK,
            "cs" => Employees_CS,
            "ru" => Employees_RU,
            _ => Employees_EN
        };

        public static string GetTemplatesFolder(string langCode) => langCode switch
        {
            "uk" => Templates_UK,
            "cs" => Templates_CS,
            "ru" => Templates_RU,
            _ => Templates_EN
        };

        public static string GetArchiveFolder(string langCode) => langCode switch
        {
            "uk" => Archive_UK,
            "cs" => Archive_CS,
            "ru" => Archive_RU,
            _ => Archive_EN
        };

        public static string GetPaymentFolder(string langCode) => langCode switch
        {
            "uk" => Payment_UK,
            "cs" => Payment_CS,
            "ru" => Payment_RU,
            _ => Payment_EN
        };

        public static string GetCandidatesFolder(string langCode) => langCode switch
        {
            "uk" => Candidates_UK,
            "cs" => Candidates_CS,
            "ru" => Candidates_RU,
            _ => Candidates_EN
        };

        public static string GetRecentlyDeletedFolder(string langCode) => langCode switch
        {
            "uk" => RecentlyDeleted_UK,
            "cs" => RecentlyDeleted_CS,
            "ru" => RecentlyDeleted_RU,
            _ => RecentlyDeleted_EN
        };

        public static string[] AllEmployeesFolderNames => new[] { Employees_UK, Employees_EN, Employees_CS, Employees_RU };
        public static string[] AllTemplatesFolderNames => new[] { Templates_UK, Templates_EN, Templates_CS, Templates_RU };
        public static string[] AllArchiveFolderNames => new[] { Archive_UK, Archive_EN, Archive_CS, Archive_RU };
        public static string[] AllPaymentFolderNames => new[] { Payment_UK, Payment_EN, Payment_CS, Payment_RU };
        public static string[] AllCandidatesFolderNames => new[] { Candidates_UK, Candidates_EN, Candidates_CS, Candidates_RU };
        public static string[] AllRecentlyDeletedFolderNames => new[] { RecentlyDeleted_UK, RecentlyDeleted_EN, RecentlyDeleted_CS, RecentlyDeleted_RU };
    }
}
