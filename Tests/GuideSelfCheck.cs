// Standalone self-check for NGUInjector/Guide.cs, runs without the game. Use Tests/run.ps1.
// Main/Character/Settings are tiny stubs so Guide.cs + SimpleJson.cs compile on their own.
using System;
using System.IO;

namespace NGUInjector
{
    class Adventure { public int titan1Kills; }
    class FakeCharacter { public int highestBoss; public int highestHardBoss; public Adventure adventure = new Adventure(); }
    class FakeSettings { public string AllocationFile = "default"; }

    static class Main
    {
        internal static FakeCharacter Character = new FakeCharacter();
        internal static FakeSettings Settings = new FakeSettings();
        internal static int Loads;
        internal static void Log(string msg) => Console.WriteLine("  log: " + msg);
        internal static void LoadAllocation() => Loads++;
    }

    static class GuideSelfCheck
    {
        static void Assert(bool ok, string what)
        {
            if (!ok) throw new Exception("FAILED: " + what);
            Console.WriteLine("ok   " + what);
        }

        static int Main()
        {
            var c = NGUInjector.Main.Character;
            var settings = NGUInjector.Main.Settings;
            c.highestBoss = 58;
            Assert(Guide.Check(c, "highestBoss>=58"), ">= on int field");
            Assert(!Guide.Check(c, "highestBoss>58"), "> on int field");
            Assert(Guide.Check(c, "adventure.titan1Kills==0"), "dotted path");
            Assert(Guide.Check(c, "highestBoss < 1e3"), "spaces + exponent");
            Assert(!Guide.Check(c, "nope>=1"), "unknown field is never true");
            Assert(!Guide.Check(c, "highestBoss=>1"), "invalid syntax is never true");

            // Phase selection + auto switch against a temp guide.json and profiles folder.
            var dir = Path.Combine(Path.GetTempPath(), "nguguide-" + Guid.NewGuid().ToString("N"));
            var profiles = Path.Combine(dir, "profiles");
            Directory.CreateDirectory(profiles);
            Guide.Init(dir, profiles);
            Assert(File.Exists(Path.Combine(dir, "guide.json")), "Init writes embedded guide.json");
            Assert(File.Exists(Path.Combine(profiles, "guide-30m.json")), "Init writes embedded guide profiles");

            c.highestBoss = 0;
            Guide.Update(false);
            Assert(Guide.OverlayTitle.StartsWith("Guide - Ch1: Start"), "shipped guide.json parses, starts at Ch1");

            File.WriteAllText(Path.Combine(dir, "guide.json"), @"{ ""AutoSwitchProfiles"": true, ""Phases"": [
                { ""Name"": ""A"", ""Until"": [""highestBoss>=58""], ""Profile"": ""p1"" },
                { ""Name"": ""B"", ""Until"": [""adventure.titan1Kills>=1"", ""highestHardBoss>=1""], ""Profile"": ""p2"" },
                { ""Name"": ""C"", ""Until"": [], ""Profile"": """" } ] }");
            File.SetLastWriteTime(Path.Combine(dir, "guide.json"), DateTime.Now.AddMinutes(1));
            File.WriteAllText(Path.Combine(profiles, "p2.json"), "{}");

            c.highestBoss = 10;
            Guide.Update(true);
            Assert(Guide.OverlayTitle == "Guide - A (1/3)", "edited file reloaded, first unfinished phase shown");
            Assert(settings.AllocationFile == "default" && NGUInjector.Main.Loads == 0, "missing profile is not loaded");

            c.highestBoss = 58;
            c.adventure.titan1Kills = 1;
            Guide.Update(false);
            Assert(Guide.OverlayTitle == "Guide - B (2/3)", "phase needs ALL conditions");
            Assert(NGUInjector.Main.Loads == 0, "no switch while automation is disabled");

            Guide.Update(true);
            Assert(settings.AllocationFile == "p2" && NGUInjector.Main.Loads == 1, "auto switch to phase profile");
            Guide.Update(true);
            Assert(NGUInjector.Main.Loads == 1, "no reload when profile already active");

            c.highestHardBoss = 1;
            Guide.Update(true);
            Assert(Guide.OverlayTitle == "Guide - C (3/3)", "final phase without conditions stays");

            File.WriteAllText(Path.Combine(dir, "guide.json"), @"{ ""Enabled"": false, ""Phases"": [ { ""Name"": ""A"" } ] }");
            File.SetLastWriteTime(Path.Combine(dir, "guide.json"), DateTime.Now.AddMinutes(2));
            Guide.Update(true);
            Assert(Guide.OverlayTitle == "", "Enabled:false hides the guide");

            Directory.Delete(dir, true);
            Console.WriteLine("all guide checks passed");
            return 0;
        }
    }
}
