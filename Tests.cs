using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TabletSwitch
{
    // These tests never invoke SendInput or a real OpenTabletDriver installation.
    internal static class BehaviorTests
    {
        const string Tablet = "Test tablet (exact name)";
        const string Absolute = "OpenTabletDriver.Desktop.Output.AbsoluteMode";
        const string FakeModeVariable = "TABLET_SWITCH_TEST_FAKE_MODE";
        const string FakeDirectoryVariable = "TABLET_SWITCH_TEST_FAKE_DIRECTORY";
        static int passed, failed;

        static int Main(string[] args)
        {
            if (!String.IsNullOrEmpty(Environment.GetEnvironmentVariable(FakeModeVariable))) return FakeCliMain(args);
            if (args.Length > 0 && args[0] == "--echo-args")
            {
                Console.Write(Convert.ToBase64String(Encoding.UTF8.GetBytes(SettingsStore.Json().Serialize(args.Skip(1).ToArray()))));
                return 0;
            }
            string original = SettingsStore.DirectoryPath;
            string temp = Path.Combine(Path.GetTempPath(), "TabletSwitchTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            SettingsStore.DirectoryPath = temp;
            try
            {
                Test("virtual origin includes negative X/Y and half pixels", Geometry);
                Test("profile names match exactly and uniquely", ProfileNames);
                Test("malformed profile and display schemas fail closed", MalformedSchema);
                Test("display dimensions must be positive finite JSON numbers", InvalidNumbers);
                Test("relative output rejected, both known absolute outputs allowed", OutputModes);
                Test("Windows argument quoting survives real process parsing", Quoting);
                Test("process client rejects unsupported command help", ProcessUnsupportedHelp);
                Test("process client rejects absent snapshot despite zero exit", ProcessMissingSnapshot);
                Test("process client rejects stderr command error despite zero exit", ProcessZeroExitError);
                Test("process client rejects nonzero exit status", ProcessNonzeroExit);
                Test("process client supports real snapshot/write/readback roundtrip", ProcessRoundtrip);
                Test("shortcut validation rejects reserved and malformed shortcuts", Shortcuts);
                Test("OTD readback mismatch never reports success", ReadbackMismatch);
                Test("OTD readback exception never reports success", ReadbackException);
                Test("readback changing to relative output prevents success", ReadbackModeChange);
                Test("already selected target performs no mutation", NoOp);
                Test("already selected target still rejects topology races", NoOpTopologyRace);
                Test("cycle follows current external mapping", CycleUsesActual);
                Test("disconnected target performs no mutation", Disconnected);
                Test("stale rectangle performs no mutation", StaleBounds);
                Test("changed topology rejected until probe", ChangedTopology);
                Test("topology changing during snapshot cancels before mutation", TopologyDuringSnapshot);
                Test("topology changing during mutation prevents success", TopologyDuringSet);
                Test("restore before any switch performs no mutation", RestoreBeforeSwitch);
                Test("restore preserves externally edited mapping", RestoreExternalMapping);
                Test("restore refuses externally changed output mode", RestoreExternalOutputMode);
                Test("restore checks topology again before mutation", RestoreTopologyRace);
                Test("restore rejects readback changing to relative output", RestoreReadbackModeChange);
                Test("successful restore is verified and consumed", RestoreSuccess);
                Test("concurrent operation is rejected without another read/write", ConcurrentGate);
                Test("failed request releases gate for next request", GateReleasedAfterFailure);
            }
            finally
            {
                SettingsStore.DirectoryPath = original;
                // This process owns this uniquely named temporary directory only.
                if (Path.GetFullPath(temp).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                    && Path.GetFileName(temp).StartsWith("TabletSwitchTests-", StringComparison.Ordinal)) Directory.Delete(temp, true);
            }
            Console.WriteLine("Results: " + passed + " passed, " + failed + " failed.");
            return failed == 0 ? 0 : 1;
        }

        static void Test(string name, Action action)
        {
            try { action(); passed++; Console.WriteLine("PASS " + name); }
            catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e.GetType().Name + ": " + e.Message); }
        }
        static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
        static void Throws(Action action, string message)
        { try { action(); } catch { return; } throw new Exception(message); }
        static SwitchResult Wait(Task<SwitchResult> task) { return task.GetAwaiter().GetResult(); }
        static DisplayArea Area(double width, double height, double x, double y)
        { return new DisplayArea { Width = width, Height = height, X = x, Y = y }; }
        static DisplayArea Copy(DisplayArea area) { return Area(area.Width, area.Height, area.X, area.Y); }
        static List<DisplayTarget> Displays()
        {
            return new List<DisplayTarget> {
                new DisplayTarget { Id = "left", DeviceName = "DISPLAY1", Label = "Left", Bounds = new Rectangle(-1921, -1081, 1921, 1081) },
                new DisplayTarget { Id = "main", DeviceName = "DISPLAY2", Label = "Main", Bounds = new Rectangle(0, 0, 2560, 1440), Primary = true },
                new DisplayTarget { Id = "right", DeviceName = "DISPLAY3", Label = "Right", Bounds = new Rectangle(2560, 0, 1920, 1080) }
            };
        }
        static Dictionary<string, object> ProfileObject(DisplayArea area, string mode, string name)
        {
            return new Dictionary<string, object> {
                { "Tablet", name }, { "OutputMode", new Dictionary<string, object> { { "Path", mode } } },
                { "AbsoluteModeSettings", new Dictionary<string, object> { { "Display", new Dictionary<string, object> {
                    { "Width", area.Width }, { "Height", area.Height }, { "X", area.X }, { "Y", area.Y } } } } }
            };
        }
        static OtdSnapshot Snapshot(DisplayArea area, string mode)
        { return FromProfiles(ProfileObject(area, mode, Tablet)); }
        static OtdSnapshot FromProfiles(params object[] profiles)
        { return OtdSnapshot.Parse(SettingsStore.Json().Serialize(new Dictionary<string, object> { { "Profiles", profiles } })); }
        static SwitchSettings Config()
        { return new SwitchSettings { Backend = "otd", OtdCliPath = @"C:\not-a-real-otd\OpenTabletDriver.Console.exe", TabletName = Tablet }; }

        sealed class FakeClient : IOtdClient
        {
            public DisplayArea Current;
            public string Mode = Absolute;
            public int Sets, Reads, Checks;
            public bool IgnoreSet;
            public Action<int> OnRead, OnSet;
            public TaskCompletionSource<bool> ReadGate;
            public FakeClient(DisplayArea current) { Current = Copy(current); }
            public Task CheckAsync(string path) { Checks++; return Task.FromResult(true); }
            public async Task<OtdSnapshot> SnapshotAsync(string path)
            {
                Reads++;
                if (OnRead != null) OnRead(Reads);
                if (ReadGate != null) await ReadGate.Task;
                return Snapshot(Current, Mode);
            }
            public Task SetAreaAsync(string path, string tablet, DisplayArea area)
            {
                Assert(tablet == Tablet, "Wrong tablet sent to adapter.");
                Sets++;
                if (!IgnoreSet) Current = Copy(area);
                if (OnSet != null) OnSet(Sets);
                return Task.FromResult(true);
            }
        }

        static void Geometry()
        {
            var displays = Displays();
            Assert(SwitchService.AreaFor(displays[0], displays).Matches(Area(1921, 1081, 960.5, 540.5)), "Negative origin or odd-size center was rounded incorrectly.");
            Assert(SwitchService.AreaFor(displays[1], displays).Matches(Area(2560, 1440, 3201, 1801)), "Primary display is not measured from the virtual desktop origin.");
            Assert(SwitchService.AreaFor(displays[2], displays).Matches(Area(1920, 1080, 5441, 1621)), "Right display offset is incorrect.");
        }
        static void ProfileNames()
        {
            var profile = ProfileObject(Area(10, 10, 5, 5), Absolute, Tablet);
            var snapshot = FromProfiles(profile);
            Assert(snapshot.Profile(Tablet) != null, "Exact name should resolve.");
            Throws(() => snapshot.Profile(Tablet.ToLowerInvariant()), "Case-insensitive profile match is unsafe.");
            Throws(() => snapshot.Profile("Test tablet"), "Substring profile match is unsafe.");
            Throws(() => FromProfiles(profile, profile).Profile(Tablet), "Duplicate profiles must be rejected.");
        }
        static void MalformedSchema()
        {
            foreach (string json in new[] { "null", "[]", "{}", "{\"Profiles\":null}", "{\"Profiles\":42}", "{\"Profiles\":\"bad\"}", "{\"Profiles\":[{}]}", "{\"Profiles\":[{\"Tablet\":42}]}" })
                Throws(() => OtdSnapshot.Parse(json).Profile(Tablet), "Malformed schema was accepted: " + json);
            var profile = ProfileObject(Area(10, 10, 5, 5), Absolute, Tablet);
            profile["AbsoluteModeSettings"] = new Dictionary<string, object>();
            Throws(() => FromProfiles(profile).Area(Tablet), "Missing Display was accepted.");
        }
        static void InvalidNumbers()
        {
            foreach (object value in new object[] { null, true, "12", 0, -1 })
            {
                var profile = ProfileObject(Area(10, 10, 5, 5), Absolute, Tablet);
                var display = (Dictionary<string, object>)((Dictionary<string, object>)profile["AbsoluteModeSettings"])["Display"];
                display["Width"] = value;
                Throws(() => FromProfiles(profile).Area(Tablet), "Invalid display Width accepted: " + (value ?? "null"));
            }
        }
        static void OutputModes()
        {
            Snapshot(Area(10, 10, 5, 5), Absolute).RequireAbsolute(Tablet);
            Snapshot(Area(10, 10, 5, 5), "VoiDPlugins.OutputMode.WinInkAbsoluteMode").RequireAbsolute(Tablet);
            foreach (string mode in new[] { "OpenTabletDriver.Desktop.Output.RelativeMode", "", Absolute + "Unexpected" })
                Throws(() => Snapshot(Area(10, 10, 5, 5), mode).RequireAbsolute(Tablet), "Unknown/relative mode accepted: " + mode);
        }
        static void Quoting()
        {
            string[] expected = { "", "plain", "two words", "\"", "a\\\"b", @"C:\folder with spaces\", "\\\\\"", "a\"b\"c", "中文 数位板", "& | > < ^ %PATH% $(whoami) `x`", "a\tb", "line1\nline2" };
            var start = new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName,
                "--echo-args " + String.Join(" ", expected.Select(OtdClient.Quote))) {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true, RedirectStandardError = true };
            using (var process = Process.Start(start))
            {
                string encoded = process.StandardOutput.ReadToEnd();
                Assert(process.WaitForExit(5000), "Argument echo child timed out.");
                Assert(process.ExitCode == 0, "Argument echo child failed.");
                string[] actual = SettingsStore.Json().Deserialize<string[]>(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));
                Assert(expected.SequenceEqual(actual), "Windows process argument parsing did not preserve exact strings.");
            }
        }
        static void Shortcuts()
        {
            Assert(DriverShortcut.Parse("Ctrl+Alt+Shift+F12").SequenceEqual(new ushort[] { 0x11, 0x12, 0x10, 0x7B }), "Valid chord parsed incorrectly.");
            Assert(DriverShortcut.Parse("Ctrl+Alt+1").Last() == 0x31 && DriverShortcut.Parse("Alt+Ctrl+9").Last() == 0x39, "Digit chords must remain available for application shortcuts.");
            foreach (string hotkey in new[] { "Ctrl+Alt+F9", "Ctrl+Alt+F10", "", "Ctrl", "Shift+A", "A+Ctrl", "Ctrl+A+B", "Ctrl+Ctrl+A", "Ctrl++A", "Ctrl+Delete", "Ctrl+F25", "Ctrl+Tab" })
                Throws(() => DriverShortcut.Parse(hotkey), "Invalid/reserved shortcut accepted: " + hotkey);
        }

        // A child copy of this test executable is the only executable OtdClient starts.
        // Its protocol and state files exist exclusively inside the test-owned temp root.
        static int FakeCliMain(string[] args)
        {
            string mode = Environment.GetEnvironmentVariable(FakeModeVariable);
            string directory = Environment.GetEnvironmentVariable(FakeDirectoryVariable);
            string tempRoot = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (String.IsNullOrWhiteSpace(directory) || !Path.GetFullPath(directory).StartsWith(tempRoot, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(directory).StartsWith("TabletSwitchTests-", StringComparison.Ordinal)) return 80;
            if (mode == "zero-exit-error") { Console.Error.WriteLine("unrecognized command: requested operation"); return 0; }
            if (mode == "nonzero-exit") { Console.Error.WriteLine("fake daemon refused request"); return 7; }
            if (args.Length == 1 && args[0] == "--help")
            {
                Console.WriteLine(mode == "unsupported-help" ? "Available: detect list" : "Available: setdisplayarea savesettings");
                return 0;
            }
            string statePath = Path.Combine(directory, "fake-cli-state.json");
            if (args.Length == 2 && args[0] == "savesettings")
            {
                if (!String.Equals(Path.GetDirectoryName(Path.GetFullPath(args[1])), Path.GetFullPath(directory), StringComparison.OrdinalIgnoreCase)) return 81;
                if (mode != "missing-snapshot") File.WriteAllText(args[1], File.ReadAllText(statePath, Encoding.UTF8), new UTF8Encoding(false));
                return 0;
            }
            if (args.Length == 6 && args[0] == "setdisplayarea" && args[1] == Tablet)
            {
                var area = Area(Double.Parse(args[2], CultureInfo.InvariantCulture), Double.Parse(args[3], CultureInfo.InvariantCulture),
                    Double.Parse(args[4], CultureInfo.InvariantCulture), Double.Parse(args[5], CultureInfo.InvariantCulture));
                File.WriteAllText(statePath, Snapshot(area, Absolute).JsonText, new UTF8Encoding(false));
                return 0;
            }
            Console.Error.WriteLine("unrecognized command");
            return 9;
        }
        static void WithFakeCli(string mode, Action<OtdClient, string> action)
        {
            string previousMode = Environment.GetEnvironmentVariable(FakeModeVariable);
            string previousDirectory = Environment.GetEnvironmentVariable(FakeDirectoryVariable);
            try
            {
                Environment.SetEnvironmentVariable(FakeModeVariable, mode);
                Environment.SetEnvironmentVariable(FakeDirectoryVariable, SettingsStore.DirectoryPath);
                var displays = Displays();
                File.WriteAllText(Path.Combine(SettingsStore.DirectoryPath, "fake-cli-state.json"), Snapshot(SwitchService.AreaFor(displays[0], displays), Absolute).JsonText, new UTF8Encoding(false));
                action(new OtdClient(), Process.GetCurrentProcess().MainModule.FileName);
            }
            finally
            {
                Environment.SetEnvironmentVariable(FakeModeVariable, previousMode);
                Environment.SetEnvironmentVariable(FakeDirectoryVariable, previousDirectory);
            }
        }
        static void ThrowsMessage(Action action, string fragment)
        {
            try { action(); }
            catch (InvalidOperationException e)
            { Assert(e.Message.Contains(fragment), "Expected error containing '" + fragment + "', got: " + e.Message); return; }
            throw new Exception("Expected InvalidOperationException containing '" + fragment + "'.");
        }
        static void ProcessUnsupportedHelp()
        { WithFakeCli("unsupported-help", (client, executable) => ThrowsMessage(() => client.CheckAsync(executable).GetAwaiter().GetResult(), "命令不兼容")); }
        static void ProcessMissingSnapshot()
        {
            WithFakeCli("missing-snapshot", (client, executable) => {
                ThrowsMessage(() => client.SnapshotAsync(executable).GetAwaiter().GetResult(), "没有生成实时设置");
                Assert(Directory.GetFiles(SettingsStore.DirectoryPath, "snapshot-*.json").Length == 0, "Missing snapshot left stale temporary files.");
            });
        }
        static void ProcessZeroExitError()
        { WithFakeCli("zero-exit-error", (client, executable) => ThrowsMessage(() => client.CheckAsync(executable).GetAwaiter().GetResult(), "未接受请求")); }
        static void ProcessNonzeroExit()
        { WithFakeCli("nonzero-exit", (client, executable) => ThrowsMessage(() => client.CheckAsync(executable).GetAwaiter().GetResult(), "返回错误 7")); }
        static void ProcessRoundtrip()
        {
            WithFakeCli("roundtrip", (client, executable) => {
                var displays = Displays(); var cfg = Config(); cfg.OtdCliPath = executable;
                CultureInfo previousCulture = System.Threading.Thread.CurrentThread.CurrentCulture;
                try
                {
                    System.Threading.Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                    var service = new SwitchService(cfg, client, () => displays);
                    var probe = Wait(service.ProbeAsync());
                    Assert(probe.Success && probe.TargetId == "left", "Real process snapshot did not parse the initial mapping: " + probe.Message);
                    var switched = Wait(service.SelectAsync(displays[1]));
                    Assert(switched.Success && switched.Verified && switched.TargetId == "main", "Real process write/readback failed: " + switched.Message);
                    var restored = Wait(service.RestoreAsync());
                    Assert(restored.Success && restored.Verified, "Real process restore/readback failed: " + restored.Message);
                    var final = client.SnapshotAsync(executable).GetAwaiter().GetResult();
                    Assert(final.Area(Tablet).Matches(SwitchService.AreaFor(displays[0], displays)), "Fractional centers were corrupted by locale or process argument parsing.");
                    Assert(Directory.GetFiles(SettingsStore.DirectoryPath, "snapshot-*.json").Length == 0, "Completed process snapshots were not cleaned up.");
                }
                finally { System.Threading.Thread.CurrentThread.CurrentCulture = previousCulture; }
            });
        }
        static void ReadbackMismatch()
        {
            var displays = Displays(); var client = new FakeClient(SwitchService.AreaFor(displays[0], displays)) { IgnoreSet = true };
            var result = Wait(new SwitchService(Config(), client, () => displays).SelectAsync(displays[1]));
            Assert(!result.Success && !result.Verified && client.Sets == 1, "Unchanged readback was reported as a verified success or retried.");
        }
        static void ReadbackException()
        {
            var displays = Displays(); var client = new FakeClient(SwitchService.AreaFor(displays[0], displays));
            client.OnRead = n => { if (n == 2) throw new IOException("Readback unavailable"); };
            var result = Wait(new SwitchService(Config(), client, () => displays).SelectAsync(displays[1]));
            Assert(!result.Success && !result.Verified && client.Sets == 1, "Failed readback was reported as success or retried.");
        }
        static void NoOp()
        {
            var displays = Displays(); var client = new FakeClient(SwitchService.AreaFor(displays[1], displays));
            var result = Wait(new SwitchService(Config(), client, () => displays).SelectAsync(displays[1]));
            Assert(result.Success && result.Verified && result.TargetId == "main" && client.Sets == 0, "Already-selected display was unnecessarily mutated.");
        }
        static void ReadbackModeChange()
        {
            var displays = Displays(); var client = new FakeClient(SwitchService.AreaFor(displays[0], displays));
            var service = new SwitchService(Config(), client, () => displays);
            client.OnSet = n => { client.Mode = "OpenTabletDriver.Desktop.Output.RelativeMode"; };
            Assert(!Wait(service.SelectAsync(displays[1])).Success && client.Sets == 1, "Matching geometry was verified despite relative output on readback.");
        }
        static void NoOpTopologyRace()
        {
            var displays = Displays(); var client = new FakeClient(SwitchService.AreaFor(displays[1], displays));
            var service = new SwitchService(Config(), client, () => displays);
            client.OnRead = n => { displays[2].Bounds = new Rectangle(2700, 0, 1920, 1080); };
            Assert(!Wait(service.SelectAsync(displays[1])).Success && client.Sets == 0, "No-op shortcut verified a target after topology changed during read.");
        }
        static void CycleUsesActual()
        {
            var displays = Displays(); var client = new FakeClient(SwitchService.AreaFor(displays[0], displays));
            var service = new SwitchService(Config(), client, () => displays);
            Assert(Wait(service.CycleAsync(displays)).TargetId == "main", "First cycle chose wrong target.");
            client.Current = SwitchService.AreaFor(displays[2], displays); // External user/driver mapping change.
            Assert(Wait(service.CycleAsync(displays)).TargetId == "left", "Cycle used a stale private counter.");
        }
        static void Disconnected()
        {
            var displays = Displays(); var client = new FakeClient(SwitchService.AreaFor(displays[0], displays));
            var result = Wait(new SwitchService(Config(), client, () => displays).SelectAsync(new DisplayTarget { Id = "gone", Bounds = displays[0].Bounds }));
            Assert(!result.Success && client.Sets == 0, "Disconnected screen accepted.");
        }
        static void StaleBounds()
        {
            var displays = Displays(); var client = new FakeClient(SwitchService.AreaFor(displays[0], displays));
            var result = Wait(new SwitchService(Config(), client, () => displays).SelectAsync(new DisplayTarget { Id = "main", Bounds = new Rectangle(0, 0, 800, 600) }));
            Assert(!result.Success && client.Sets == 0, "Old screen geometry accepted.");
        }
        static void ChangedTopology()
        {
            var displays = Displays(); var client = new FakeClient(SwitchService.AreaFor(displays[0], displays));
            var service = new SwitchService(Config(), client, () => displays);
            displays[2].Bounds = new Rectangle(2700, 0, 1920, 1080);
            Assert(!Wait(service.SelectAsync(displays[1])).Success && client.Sets == 0, "New topology silently accepted.");
            Assert(Wait(service.ProbeAsync()).Success, "Probe could not revalidate topology.");
            Assert(Wait(service.SelectAsync(displays[1])).Success && client.Sets == 1, "Verified topology did not become usable.");
        }
        static void TopologyDuringSnapshot()
        {
            var displays = Displays(); var client = new FakeClient(SwitchService.AreaFor(displays[0], displays));
            var service = new SwitchService(Config(), client, () => displays);
            client.OnRead = n => { if (n == 1) displays[2].Bounds = new Rectangle(2700, 0, 1920, 1080); };
            Assert(!Wait(service.SelectAsync(displays[1])).Success && client.Sets == 0, "Topology changed during read, but stale geometry was sent.");
        }
        static void TopologyDuringSet()
        {
            var displays = Displays(); var client = new FakeClient(SwitchService.AreaFor(displays[0], displays));
            var service = new SwitchService(Config(), client, () => displays);
            client.OnSet = n => { displays[2].Bounds = new Rectangle(2700, 0, 1920, 1080); };
            Assert(!Wait(service.SelectAsync(displays[1])).Success && client.Sets == 1, "Topology changed during write, but result claimed success.");
        }
        static void RestoreBeforeSwitch()
        {
            var displays = Displays(); var client = new FakeClient(SwitchService.AreaFor(displays[0], displays));
            Assert(!Wait(new SwitchService(Config(), client, () => displays).RestoreAsync()).Success && client.Sets == 0, "Restore before switch mutated OTD.");
        }
        static void RestoreExternalMapping()
        {
            var displays = Displays(); var client = new FakeClient(SwitchService.AreaFor(displays[0], displays));
            var service = new SwitchService(Config(), client, () => displays);
            Assert(Wait(service.SelectAsync(displays[1])).Success, "Setup switch failed.");
            client.Current = Area(800, 600, 400, 300);
            Assert(!Wait(service.RestoreAsync()).Success && client.Sets == 1 && client.Current.Width == 800, "Restore overwrote external mapping change.");
        }
        static void RestoreExternalOutputMode()
        {
            var displays = Displays(); var client = new FakeClient(SwitchService.AreaFor(displays[0], displays));
            var service = new SwitchService(Config(), client, () => displays);
            Assert(Wait(service.SelectAsync(displays[1])).Success, "Setup switch failed.");
            client.Mode = "OpenTabletDriver.Desktop.Output.RelativeMode";
            Assert(!Wait(service.RestoreAsync()).Success && client.Sets == 1, "Restore mutated absolute mapping after user changed to relative output mode.");
        }
        static void RestoreTopologyRace()
        {
            var displays = Displays(); var client = new FakeClient(SwitchService.AreaFor(displays[0], displays));
            var service = new SwitchService(Config(), client, () => displays);
            Assert(Wait(service.SelectAsync(displays[1])).Success, "Setup switch failed.");
            client.OnRead = n => { displays[2].Bounds = new Rectangle(2700, 0, 1920, 1080); };
            Assert(!Wait(service.RestoreAsync()).Success && client.Sets == 1, "Restore did not recheck topology after asynchronous snapshot and sent stale geometry.");
        }
        static void RestoreSuccess()
        {
            var displays = Displays(); var original = SwitchService.AreaFor(displays[0], displays); var client = new FakeClient(original);
            var service = new SwitchService(Config(), client, () => displays);
            Assert(Wait(service.SelectAsync(displays[1])).Success, "Setup switch failed.");
            var restored = Wait(service.RestoreAsync());
            Assert(restored.Success && restored.Verified && client.Current.Matches(original), "Restore failed to return to saved mapping.");
            Assert(!Wait(service.RestoreAsync()).Success && client.Sets == 2, "Consumed undo was applied again.");
        }
        static void RestoreReadbackModeChange()
        {
            var displays = Displays(); var client = new FakeClient(SwitchService.AreaFor(displays[0], displays));
            var service = new SwitchService(Config(), client, () => displays);
            Assert(Wait(service.SelectAsync(displays[1])).Success, "Setup switch failed.");
            client.OnSet = n => { client.Mode = "OpenTabletDriver.Desktop.Output.RelativeMode"; };
            Assert(!Wait(service.RestoreAsync()).Success && client.Sets == 2, "Restore verified geometry despite relative output on readback.");
        }
        static void ConcurrentGate()
        {
            var displays = Displays(); var client = new FakeClient(SwitchService.AreaFor(displays[0], displays)) { ReadGate = new TaskCompletionSource<bool>() };
            var service = new SwitchService(Config(), client, () => displays);
            var first = service.SelectAsync(displays[1]);
            Assert(!first.IsCompleted && client.Reads == 1, "First operation did not reach blocked read.");
            var second = Wait(service.SelectAsync(displays[2]));
            Assert(!second.Success && client.Reads == 1 && client.Sets == 0, "Concurrent operation bypassed gate.");
            client.ReadGate.SetResult(true);
            Assert(Wait(first).Success && client.Sets == 1, "First operation failed after releasing fake read.");
        }
        static void GateReleasedAfterFailure()
        {
            var displays = Displays(); var client = new FakeClient(SwitchService.AreaFor(displays[0], displays));
            var service = new SwitchService(Config(), client, () => displays);
            client.OnRead = n => { throw new IOException("Temporary failure"); };
            Assert(!Wait(service.SelectAsync(displays[1])).Success, "Expected fake read failure.");
            client.OnRead = null;
            Assert(Wait(service.SelectAsync(displays[1])).Success, "Semaphore leaked after exception.");
        }
    }
}
