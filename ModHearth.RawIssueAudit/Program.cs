namespace ModHearth.RawIssueAudit
{
    internal static class Program
    {
        private const int ExitNoMismatches = 0;
        private const int ExitMismatches = 1;
        private const int ExitInvalidInput = 2;

        private const string Usage = """
            ModHearth.RawIssueAudit

            Compares ModHearth's raw scan and ModPossibleIssueAnalyzer predictions with the
            reference errors Dwarf Fortress wrote to errorlog.txt. Development tool only: it never
            writes config.json or the raw dependency cache. It is read-only unless --automate is
            used.

            Usage:
              dotnet run --project ModHearth.RawIssueAudit -- [options]

            Options:
              --df <folder>          Dwarf Fortress folder (auto-detected from ModHearth config.json)
              --dfhack <folder>      DFHack folder used for the dfhack-run fallback
              --errorlog <path>      errorlog.txt (default: <df>/errorlog.txt)
              --cache <path>         mod_raw_dependency_cache.json (default: newest app cache)
              --vanilla <path>       vanilla raws (default: <df>/data/vanilla)
              --modlist <path>       DFHack mod-manager.json (default: <df>/dfhack-config/mod-manager.json)
              --config <path>        ModHearth config.json used to locate Dwarf Fortress
              --all-mods             audit every cached mod instead of the active modpack
              --automate             drive DF through a short worldgen to regenerate errorlog.txt
              --automation-timeout <seconds>
                                     maximum worldgen automation time (default: 180, range: 30-3600)
              --keep-df-running      leave DF running if the audit launched it
              --json <path>          write the complete report as JSON
              --show-matches         list matched predictions in the text report
              --max-details <n>      maximum entries per section in the text report (default: 40)
              -h, --help             show this help

            Automation safety:
              --automate backs up the current log to <df>/errorlog.audit-backup.txt and clears it,
              then creates a Pocket-sized world with 5 years of history so DF loads and finalizes
              every active raw. It lets worldgen finish, returns to the title screen, and removes
              the generated save folder(s) under <df>/save/regionN. It refuses to run while a saved
              game or another worldgen is loaded, aborts on a failed mod preset, launches DF if
              necessary, and closes a DF instance it launched unless --keep-df-running is set.
              --automate cannot be combined with --all-mods.

            Exit codes:
              0  no mismatches
              1  mismatches found
              2  invalid input or audit failure

            Examples:
              dotnet run --project ModHearth.RawIssueAudit
              dotnet run --project ModHearth.RawIssueAudit -- --automate
              dotnet run --project ModHearth.RawIssueAudit -- --json artifacts/raw-audit.json
              dotnet run --project ModHearth.RawIssueAudit -- --df "C:\...\Dwarf Fortress" --all-mods
            """;

        public static int Main(string[] args)
        {
            AuditOptions options = AuditOptions.Parse(args, out string? error);
            if (!string.IsNullOrWhiteSpace(error))
            {
                Console.Error.WriteLine(error);
                Console.Error.WriteLine();
                Console.Error.WriteLine(Usage);
                return ExitInvalidInput;
            }

            if (options.HelpRequested)
            {
                Console.Out.WriteLine(Usage);
                return ExitNoMismatches;
            }

            try
            {
                AuditReport report = AuditRunner.Run(options);
                AuditTextWriter.Write(report, options, Console.Out);

                if (!string.IsNullOrWhiteSpace(options.JsonOutputPath))
                {
                    AuditJsonWriter.Write(report, options.JsonOutputPath);
                    Console.Out.WriteLine($"JSON report written to: {Path.GetFullPath(options.JsonOutputPath)}");
                }

                return report.Summary.MismatchCount == 0
                    ? ExitNoMismatches
                    : ExitMismatches;
            }
            catch (AuditInputException ex)
            {
                Console.Error.WriteLine($"Audit input error: {ex.Message}");
                return ExitInvalidInput;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Audit failed: {ex}");
                return ExitInvalidInput;
            }
        }
    }
}
