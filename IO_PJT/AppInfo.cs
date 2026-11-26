namespace IO_PJT
{
    /// <summary>Program metadata (task 19.11.2026, item 6): current version is shown
    /// in the Help - About dialog.</summary>
    public static class AppInfo
    {
        /// <summary>Current program version. Versioning restarted at 1.0 (previously F-numbering,
        /// last F100); the change log lives in Work/ТЗ.md ("Перечень изменений").
        /// 1.1 = F105 unbound bit object removal (data templates) + F102/F103 master
        /// object name mapping fixes; smoke suite Test 11.
        /// 1.2 = F106: dot "." is a normal character in names/marks (never replaced
        /// with "_") + F13 window overlap fix (master objects group); smoke Test 12.
        /// 1.3 = F107: dataout template falls back to SCADA_DATAOUT_TEMPLATES when
        /// the configured file path is missing (DB moved to another computer);
        /// smoke Test 13.
        /// 1.4 = F108: templates live only in the DB/editor — file mode and all
        /// external path settings removed from F13 (no file paths in the DB);
        /// smoke Test 13 (templates from DB).</summary>
        public const string Version = "1.4";
    }
}
