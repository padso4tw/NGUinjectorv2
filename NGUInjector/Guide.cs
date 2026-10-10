using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using SimpleJSON;

namespace NGUInjector
{
    // Built-in progression guide. Phases live in guide.json (desktop folder), each phase is done
    // once all of its "Until" conditions hold. The first unfinished phase is the current one.
    internal static class Guide
    {
        private const string ResourcePrefix = "NGUInjector.Guide.";
        private static readonly Regex CondRegex = new Regex(@"^\s*([\w.]+)\s*(>=|<=|==|>|<)\s*([-+\d.eE]+)\s*$");

        private static string _path;
        private static string _profilesDir;
        private static DateTime _loadedAt;
        private static JSONNode _data;
        private static string _lastPhase;
        private static readonly HashSet<string> Warned = new HashSet<string>();

        internal static string OverlayTitle = "";
        internal static string OverlayGoal = "";

        // Writes guide.json and the guide-* starter profiles if they don't exist yet. Never overwrites user edits.
        internal static void Init(string dir, string profilesDir)
        {
            _path = Path.Combine(dir, "guide.json");
            _profilesDir = profilesDir;
            var asm = Assembly.GetExecutingAssembly();
            foreach (var name in asm.GetManifestResourceNames().Where(n => n.StartsWith(ResourcePrefix)))
            {
                var file = name.Substring(ResourcePrefix.Length);
                var target = file == "guide.json"
                    ? _path
                    : Path.Combine(profilesDir, file.Substring("profiles.".Length));
                if (File.Exists(target)) continue;
                using (var reader = new StreamReader(asm.GetManifestResourceStream(name)))
                    File.WriteAllText(target, reader.ReadToEnd());
            }
        }

        internal static void Update(bool automationEnabled)
        {
            try
            {
                if (_path == null || !File.Exists(_path))
                {
                    OverlayTitle = OverlayGoal = "";
                    return;
                }

                var mtime = File.GetLastWriteTime(_path);
                if (_data == null || mtime != _loadedAt)
                {
                    _loadedAt = mtime;
                    _data = JSON.Parse(File.ReadAllText(_path));
                    _lastPhase = null;
                    Warned.Clear();
                }

                var enabled = _data["Enabled"];
                if (enabled.Value != "" && !enabled.AsBool) // missing key = enabled
                {
                    OverlayTitle = OverlayGoal = "";
                    return;
                }

                var phases = _data["Phases"].Children.ToList();
                if (phases.Count == 0) return;
                var index = phases.FindIndex(p => !IsDone(p));
                if (index < 0) index = phases.Count - 1;
                var phase = phases[index];

                var name = phase["Name"].Value;
                OverlayTitle = $"Guide - {name} ({index + 1}/{phases.Count})";
                OverlayGoal = phase["Goal"].Value;

                if (name != _lastPhase)
                {
                    _lastPhase = name;
                    var next = string.Join(", ", phase["Until"].Children.Select(c => c.Value).ToArray());
                    Main.Log($"Guide phase: {name} - {OverlayGoal} (done when: {next}) {phase["Url"].Value}");
                }

                var profile = phase["Profile"].Value;
                if (!automationEnabled || !_data["AutoSwitchProfiles"].AsBool || string.IsNullOrEmpty(profile) ||
                    profile == Main.Settings.AllocationFile)
                    return;

                if (!File.Exists(Path.Combine(_profilesDir, profile + ".json")))
                {
                    WarnOnce($"Guide: profile '{profile}' for phase '{name}' not found in profiles folder");
                    return;
                }

                Main.Log($"Guide: switching allocation profile {Main.Settings.AllocationFile} -> {profile}");
                Main.Settings.AllocationFile = profile;
                Main.LoadAllocation();
            }
            catch (Exception e)
            {
                OverlayTitle = "Guide - guide.json could not be read, see inject.log";
                OverlayGoal = "";
                WarnOnce($"Guide: {e.Message}");
            }
        }

        // A phase without conditions is never done (final phase).
        private static bool IsDone(JSONNode phase)
        {
            var conds = phase["Until"].Children.Select(c => c.Value).ToList();
            return conds.Count > 0 && conds.All(c => Check(Main.Character, c));
        }

        // "field>=value", field is a dotted field path on root, e.g. highestBoss>=58 or adventure.titan1Kills>=1
        internal static bool Check(object root, string cond)
        {
            var m = CondRegex.Match(cond);
            if (!m.Success)
            {
                WarnOnce($"Guide: invalid condition '{cond}'");
                return false;
            }

            var obj = root;
            foreach (var part in m.Groups[1].Value.Split('.'))
            {
                var field = obj?.GetType().GetField(part, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field == null)
                {
                    WarnOnce($"Guide: unknown field '{m.Groups[1].Value}' in condition '{cond}'");
                    return false;
                }

                obj = field.GetValue(obj);
            }

            var actual = Convert.ToDouble(obj, CultureInfo.InvariantCulture);
            var target = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            switch (m.Groups[2].Value)
            {
                case ">=": return actual >= target;
                case "<=": return actual <= target;
                case ">": return actual > target;
                case "<": return actual < target;
                default: return Math.Abs(actual - target) < 1e-9;
            }
        }

        private static void WarnOnce(string msg)
        {
            if (Warned.Add(msg)) Main.Log(msg);
        }
    }
}
