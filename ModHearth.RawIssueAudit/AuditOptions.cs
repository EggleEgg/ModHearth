namespace ModHearth.RawIssueAudit
{
    /// <summary>
    /// Command-line options for the raw issue audit. Paths are optional: when omitted the tool
    /// discovers ModHearth's config and the local Dwarf Fortress install automatically.
    /// </summary>
    internal sealed class AuditOptions
    {
        public string? ErrorLogPath { get; private set; }
        public string? CachePath { get; private set; }
        public string? VanillaPath { get; private set; }
        public string? ModListPath { get; private set; }
        public string? ConfigPath { get; private set; }
        public string? DwarfFortressPath { get; private set; }
        public string? DfHackPath { get; private set; }
        public string? JsonOutputPath { get; private set; }
        public bool AllMods { get; private set; }
        public bool ShowMatches { get; private set; }
        public bool HelpRequested { get; private set; }
        public bool AutomateWorldgen { get; private set; }
        public bool KeepDwarfFortressRunning { get; private set; }
        public int MaxDetails { get; private set; } = 40;
        public int AutomationTimeoutSeconds { get; private set; } = 180;

        public static AuditOptions Parse(string[] args, out string? error)
        {
            AuditOptions options = new();
            error = null;
            bool automationTimeoutSpecified = false;

            for (int i = 0; i < args.Length; i++)
            {
                string argument = args[i];
                switch (argument)
                {
                    case "-h":
                    case "--help":
                        options.HelpRequested = true;
                        return options;

                    case "--all-mods":
                        options.AllMods = true;
                        break;

                    case "--show-matches":
                        options.ShowMatches = true;
                        break;

                    case "--automate":
                    case "--automate-worldgen":
                        options.AutomateWorldgen = true;
                        break;

                    case "--keep-df-running":
                        options.KeepDwarfFortressRunning = true;
                        break;

                    case "--errorlog":
                    case "--log":
                        if (!TryTakeValue(args, ref i, argument, out string? errorLog, out error))
                            return options;
                        options.ErrorLogPath = errorLog;
                        break;

                    case "--cache":
                        if (!TryTakeValue(args, ref i, argument, out string? cache, out error))
                            return options;
                        options.CachePath = cache;
                        break;

                    case "--vanilla":
                        if (!TryTakeValue(args, ref i, argument, out string? vanilla, out error))
                            return options;
                        options.VanillaPath = vanilla;
                        break;

                    case "--modlist":
                        if (!TryTakeValue(args, ref i, argument, out string? modlist, out error))
                            return options;
                        options.ModListPath = modlist;
                        break;

                    case "--config":
                        if (!TryTakeValue(args, ref i, argument, out string? config, out error))
                            return options;
                        options.ConfigPath = config;
                        break;

                    case "--df":
                        if (!TryTakeValue(args, ref i, argument, out string? df, out error))
                            return options;
                        options.DwarfFortressPath = df;
                        break;

                    case "--dfhack":
                        if (!TryTakeValue(args, ref i, argument, out string? dfHack, out error))
                            return options;
                        options.DfHackPath = dfHack;
                        break;

                    case "--json":
                        if (!TryTakeValue(args, ref i, argument, out string? json, out error))
                            return options;
                        options.JsonOutputPath = json;
                        break;

                    case "--max-details":
                        if (!TryTakeValue(args, ref i, argument, out string? maxDetails, out error))
                            return options;
                        if (!int.TryParse(maxDetails, out int parsedMax) || parsedMax <= 0)
                        {
                            error = $"{argument} expects a positive whole number.";
                            return options;
                        }
                        options.MaxDetails = parsedMax;
                        break;

                    case "--automation-timeout":
                        if (!TryTakeValue(args, ref i, argument, out string? timeout, out error))
                            return options;
                        if (!int.TryParse(timeout, out int parsedTimeout)
                            || parsedTimeout < 30
                            || parsedTimeout > 3600)
                        {
                            error = $"{argument} expects a whole number of seconds between 30 and 3600.";
                            return options;
                        }
                        options.AutomationTimeoutSeconds = parsedTimeout;
                        automationTimeoutSpecified = true;
                        break;

                    default:
                        error = $"Unknown option '{argument}'.";
                        return options;
                }
            }

            if (!options.AutomateWorldgen
                && (options.KeepDwarfFortressRunning || automationTimeoutSpecified))
            {
                error = "--keep-df-running and --automation-timeout require --automate.";
                return options;
            }

            if (options.AutomateWorldgen && options.AllMods)
            {
                error = "--automate cannot be combined with --all-mods; automation always uses " +
                    "Dwarf Fortress's active mod preset.";
                return options;
            }

            return options;
        }

        private static bool TryTakeValue(
            string[] args,
            ref int index,
            string option,
            out string? value,
            out string? error)
        {
            if (index + 1 >= args.Length)
            {
                value = null;
                error = $"{option} expects a value.";
                return false;
            }

            value = args[++index];
            error = null;
            return true;
        }
    }
}
