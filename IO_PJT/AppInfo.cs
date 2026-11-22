namespace IO_PJT
{
    /// <summary>Program metadata (task 19.11.2026, item 6): current version is shown
    /// in the Help - About dialog.</summary>
    public static class AppInfo
    {
        /// <summary>Current program version. Bump after every code change (F79-F99).
        /// F99 = rules linking deduplication: TagPattern/ValuePattern dropped (mask only,
        /// physical DROP COLUMN migration), single ModbusAddressing window replaces
        /// 4 menu items + 2 toolbar buttons, best-match (Score) + partial link state.</summary>
        public const string Version = "F99";
    }
}
