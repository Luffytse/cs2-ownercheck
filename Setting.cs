using System.Collections.Generic;
using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;

namespace OwnerCheck
{
    [FileLocation(nameof(OwnerCheck))]
    public class Setting : ModSetting
    {
        public Setting(IMod mod) : base(mod)
        {
            SetDefaults();
        }

        /// Detach dangling Owner references so the game's job skips them instead of crashing.
        public bool Guard { get; set; }

        /// Log one line per affected object instead of one summary line per event.
        public bool VerboseLog { get; set; }

        public override void SetDefaults()
        {
            Guard = true;
            VerboseLog = false;
        }
    }

    public class LocaleEN : IDictionarySource
    {
        private readonly Setting m_Setting;

        public LocaleEN(Setting setting)
        {
            m_Setting = setting;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "OwnerCheck" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.Guard)), "Crash guard" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.Guard)),
                    "When an object still points at an owner that no longer exists (seen after adding or removing port / oil-rig sub-buildings), " +
                    "detach that broken reference so the game does not crash. Recommended: on." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.VerboseLog)), "Verbose log" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.VerboseLog)),
                    "Write one line per affected object (prefab, position, flags) to Logs/OwnerCheck.log instead of one summary line per event. " +
                    "Useful for bug reports; the log can get large." },
            };
        }

        public void Unload()
        {
        }
    }
}
