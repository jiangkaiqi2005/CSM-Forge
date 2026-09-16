using ColossalFramework;

namespace CsmForge.Runtime.Cities1
{
    public sealed class ForgeSettings
    {
        public const string SettingsFileName = "CSMForgeV3";

        public ForgeSettings()
        {
            GameSettings.AddSettingsFile(new SettingsFile { fileName = SettingsFileName });
        }

        public readonly SavedString DisplayName =
            new SavedString("DisplayName", SettingsFileName, "Player", true);
        public readonly SavedString HostAddress =
            new SavedString("HostAddress", SettingsFileName, "127.0.0.1", true);
        public readonly SavedInt Port =
            new SavedInt("Port", SettingsFileName, 4230, true);
    }
}
