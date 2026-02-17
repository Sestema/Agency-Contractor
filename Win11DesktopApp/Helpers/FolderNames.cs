namespace Win11DesktopApp.Helpers
{
    /// <summary>
    /// Provides localized folder names based on the application language.
    /// Also provides fallback names to search for folders created in another language.
    /// </summary>
    public static class FolderNames
    {
        // Ukrainian folder names
        private const string Employees_UK = "Працівники";
        private const string Templates_UK = "Шаблони";
        private const string Archive_UK = "Архів";

        // English folder names
        private const string Employees_EN = "Employees";
        private const string Templates_EN = "Templates";
        private const string Archive_EN = "Archive";

        /// <summary>
        /// Get the employees subfolder name for the given language code.
        /// </summary>
        public static string GetEmployeesFolder(string langCode)
            => langCode == "uk" ? Employees_UK : Employees_EN;

        /// <summary>
        /// Get the templates subfolder name for the given language code.
        /// </summary>
        public static string GetTemplatesFolder(string langCode)
            => langCode == "uk" ? Templates_UK : Templates_EN;

        /// <summary>
        /// Get the archive folder name for the given language code.
        /// </summary>
        public static string GetArchiveFolder(string langCode)
            => langCode == "uk" ? Archive_UK : Archive_EN;

        /// <summary>
        /// Returns all known employees folder names (for fallback search).
        /// </summary>
        public static string[] AllEmployeesFolderNames => new[] { Employees_UK, Employees_EN };

        /// <summary>
        /// Returns all known templates folder names (for fallback search).
        /// </summary>
        public static string[] AllTemplatesFolderNames => new[] { Templates_UK, Templates_EN };

        /// <summary>
        /// Returns all known archive folder names (for fallback search).
        /// </summary>
        public static string[] AllArchiveFolderNames => new[] { Archive_UK, Archive_EN };
    }
}
