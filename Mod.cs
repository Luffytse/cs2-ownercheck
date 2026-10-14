using Colossal.IO.AssetDatabase;
using Game;
using Game.Modding;
using Game.SceneFlow;

namespace OwnerCheck
{
    public class Mod : IMod
    {
        public static Setting Settings { get; private set; }

        public void OnLoad(UpdateSystem updateSystem)
        {
            OCLog.Init();

            Settings = new Setting(this);
            Settings.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(Settings));
            AssetDatabase.global.LoadSettings(nameof(OwnerCheck), Settings, new Setting(this));

            OCLog.Line(string.Format("OwnerCheck {0} loaded. Guard {1}, verbose log {2}.",
                typeof(Mod).Assembly.GetName().Version, Settings.Guard ? "ON" : "OFF", Settings.VerboseLog ? "ON" : "OFF"));

            updateSystem.UpdateAt<OwnerScanSystem>(SystemUpdatePhase.MainLoop);
            updateSystem.UpdateAt<WatchApplyTool>(SystemUpdatePhase.ApplyTool);
            updateSystem.UpdateAt<WatchModification1>(SystemUpdatePhase.Modification1);
            updateSystem.UpdateAt<WatchModification2>(SystemUpdatePhase.Modification2);
            updateSystem.UpdateAt<WatchModification3>(SystemUpdatePhase.Modification3);
            updateSystem.UpdateAt<WatchModification4>(SystemUpdatePhase.Modification4);
            updateSystem.UpdateAt<WatchModification5>(SystemUpdatePhase.Modification5);
            updateSystem.UpdateAt<WatchModificationEnd>(SystemUpdatePhase.ModificationEnd);
        }

        public void OnDispose()
        {
            OCLog.Line("OwnerCheck disposed.");
            if (Settings != null)
            {
                Settings.UnregisterInOptionsUI();
                Settings = null;
            }
            OCLog.Close();
        }
    }
}
