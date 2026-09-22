using System.IO;
using HarmonyLib;
using UnityEngine;

namespace NormalGolfMultiplayer
{
    /// <summary>
    /// Redirects every SaveManager file path into a separate profile folder when the game is
    /// launched with <c>-ngmp-profile NAME</c>. Lets two copies of the game run on one PC for
    /// testing without both writing to (or corrupting) the player's real save.
    /// </summary>
    internal static class SaveSandbox
    {
        private static readonly string[] SeedFiles =
        {
            "run2.txt", "run2_backUp.txt", "actuallyNormal.txt", "scoreCard.txt", "unlocks.txt", "config.txt",
        };

        private static string _dir;

        public static bool Active => _dir != null;

        public static void Apply(Harmony harmony)
        {
            if (string.IsNullOrEmpty(DevTools.Profile))
                return;

            string realDir = Application.persistentDataPath;
            _dir = Path.Combine(realDir, "ngmp_profiles", DevTools.Profile);
            if (!Directory.Exists(_dir))
            {
                Directory.CreateDirectory(_dir);
                foreach (string file in SeedFiles)
                {
                    string src = Path.Combine(realDir, file);
                    if (File.Exists(src))
                        File.Copy(src, Path.Combine(_dir, file));
                }
                Plugin.Log.LogInfo($"Save sandbox created and seeded from real saves: {_dir}");
            }
            else
            {
                Plugin.Log.LogInfo($"Using save sandbox: {_dir}");
            }

            var postfix = new HarmonyMethod(typeof(SaveSandbox), nameof(RedirectPath));
            foreach (string prop in new[]
                     {
                         "m_runSavePath", "m_runSavePathBackUp", "m_normalGolfSavePath", "m_scorecardSavePath",
                         "m_unlocksSavePath", "m_backupSavePath", "m_settingsSavePath",
                     })
            {
                harmony.Patch(AccessTools.PropertyGetter(typeof(SaveManager), prop), postfix: postfix);
            }
        }

        private static void RedirectPath(ref string __result)
        {
            // Originals are persistentDataPath + "/file" (or + "/" for the backup folder).
            string file = __result.Substring(Application.persistentDataPath.Length).TrimStart('/', '\\');
            __result = file.Length == 0 ? _dir + "/" : Path.Combine(_dir, file);
        }
    }
}
