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
        /// smoke Test 13 (templates from DB).
        /// 1.5 = F109: master object card carries the base name only (no member):
        /// SCADA composes the tag as UPPER(card.Info) + T11Text, so a card named
        /// with the full member name imported as "_X.BUSY.Busy"; master and its
        /// .Busy/.Error members now share one card (as in the SCADA template),
        /// F101 "ending" mapping syncs the member T11Text, TCP/RTU merge keeps
        /// the shared cardId; smoke Tests 8/9/10 extended (F109 invariants).
        /// 1.6 = F110: F105 now deletes unbound BIT objects even when their mapping
        /// row contains {M_Marka} or a literal; only Address mode keeps them. This
        /// includes var objects, receivers and write/simple template roots. The
        /// removal decision is made before card creation; smoke Test 11 extended.</summary>
        public const string Version = "1.6";
    }
}
