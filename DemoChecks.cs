using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TabletSwitch
{
    internal static class DemoChecks
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static int checks;
        private static Exception failure;

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks++;
        }

        private static T Field<T>(object instance, string name)
        {
            FieldInfo field = instance.GetType().GetField(name, PrivateInstance);
            if (field == null) throw new MissingFieldException(instance.GetType().FullName, name);
            return (T)field.GetValue(instance);
        }

        private static object Invoke(MainForm form, string name, params object[] arguments)
        {
            MethodInfo method = typeof(MainForm).GetMethod(name, PrivateInstance);
            if (method == null) throw new MissingMethodException(typeof(MainForm).FullName, name);
            return method.Invoke(form, arguments);
        }

        private static void ResetDebounce(MainForm form)
        {
            typeof(MainForm).GetField("lastOperation", PrivateInstance).SetValue(form, DateTime.MinValue);
        }

        private static Task Operate(MainForm form, string action)
        {
            ResetDebounce(form);
            return (Task)Invoke(form, "RunOperation", action, null, true);
        }

        private static async Task<DisplayArea> CurrentArea(DemoClient client)
        {
            return (await client.SnapshotAsync("DEMO")).Area(DemoEnvironment.TabletName);
        }

        private static bool IsTarget(DisplayArea area, int oneBased)
        {
            List<DisplayTarget> displays = DemoEnvironment.GetDisplays();
            return area.Matches(SwitchService.AreaFor(displays[oneBased - 1], displays));
        }

        private static async Task RunChecks(MainForm form, SwitchSettings settings, string markerPath, string marker)
        {
            // Allow OnShown to finish its initial in-memory probe, with a live UI message loop.
            await Task.Delay(550);
            Check(settings.Backend == "otd" && settings.OtdCliPath == "DEMO" && settings.TabletName == DemoEnvironment.TabletName,
                "Demo constructor must replace a supplied driver backend with demo OTD settings.");
            Check(File.ReadAllText(markerPath) == marker, "Construction must not alter or load the malformed settings marker.");
            Check(Field<NotifyIcon>(form, "tray") == null, "Demo must not create a tray icon.");
            Check(!Field<bool>(form, "eventsAttached"), "Demo must not subscribe to real display changes.");
            Check(Field<List<int>>(form, "registeredHotkeys").Count == 0, "Demo must not register global shortcuts.");
            Check(!Field<ComboBox>(form, "backend").Enabled, "Demo backend selector must remain disabled.");
            SwitchService service = Field<SwitchService>(form, "service");
            IOtdClient adapter = Field<IOtdClient>(service, "client");
            Check(adapter is DemoClient, "Demo must use the in-memory adapter before exercising any operation.");
            DemoClient client = (DemoClient)adapter;
            Check(Field<List<DisplayTarget>>(form, "displays").Count == 3, "Demo must show three simulated displays.");
            Check(IsTarget(await CurrentArea(client), 2), "Demo must begin on the simulated primary display.");

            ResetDebounce(form);
            await form.DemoSelectAsync(1);
            Check(IsTarget(await CurrentArea(client), 1), "Selecting the left display must update the simulated mapping.");
            Check(Field<MonitorLayoutView>(form, "monitorMap").VerifiedTargetId == "demo-left", "Selected left display must be highlighted.");
            await Operate(form, "undo");
            Check(IsTarget(await CurrentArea(client), 2), "Undo must restore the previous simulated primary mapping.");
            Check(Field<MonitorLayoutView>(form, "monitorMap").VerifiedTargetId == "demo-main", "Undo must restore the previous display highlight.");

            // The confirmation flag deliberately stays false: even a broken backend guard must not emit native input.
            settings.Backend = "driver";
            settings.DriverShortcutConfirmed = false;
            settings.OtdCliPath = "C:\\does-not-exist\\OpenTabletDriver.Console.exe";
            settings.TabletName = "different tablet";
            await Operate(form, "cycle");
            Check(settings.Backend == "otd" && settings.OtdCliPath == "DEMO" && settings.TabletName == DemoEnvironment.TabletName,
                "Every operation must restore the demo-only backend before dispatch.");
            Check(IsTarget(await CurrentArea(client), 3), "Cycle must change the fake mapping after backend poisoning.");
            Check(Field<MonitorLayoutView>(form, "monitorMap").VerifiedTargetId == "demo-portrait", "Portrait mapping must be highlighted.");

            Invoke(form, "RefreshDisplays", true);
            List<DisplayTarget> refreshed = Field<List<DisplayTarget>>(form, "displays");
            Check(refreshed.Count == 3 && refreshed[0].Id == "demo-left" && refreshed[2].Id == "demo-portrait",
                "Refresh must retain the simulated catalog.");
            Check(Field<MonitorLayoutView>(form, "monitorMap").VerifiedTargetId == "demo-portrait", "Refresh must retain the simulated mapping highlight.");
            Check(IsTarget(await CurrentArea(client), 3), "Refresh must preserve the in-memory area.");

            Invoke(form, "HandleShortcut", 100);
            Check(IsTarget(await CurrentArea(client), 3), "Synthetic shortcut dispatch must be ignored in demo mode.");
            Check((bool)Invoke(form, "SaveFields", true) && File.ReadAllText(markerPath) == marker,
                "SaveFields must bypass persistent settings in demo mode.");
            Check(!File.Exists(markerPath + ".bak"), "Demo operations must not create a settings backup.");
            form.DemoMovePen(-1F, 2F);
            PointF point = Field<DemoTabletView>(form, "demoTablet").PenPosition;
            Check(point.X == 0 && point.Y == 1 && Field<MonitorLayoutView>(form, "monitorMap").DemoPenPosition == point,
                "The simulated pen must clamp to tablet bounds and update the selected screen.");

            bool rejected = false;
            try { await client.CheckAsync("anything-other-than-DEMO"); }
            catch (InvalidOperationException) { rejected = true; }
            Check(rejected, "The demo adapter must reject an arbitrary executable path.");
            bool closed = false;
            form.FormClosed += delegate { closed = true; };
            form.Close();
            Check(closed && form.IsDisposed, "Closing the demo window must exit instead of hiding to the tray.");
        }

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length != 1) { Console.Error.WriteLine("A dedicated temporary test directory is required."); return 2; }
            SettingsStore.DirectoryPath = Path.GetFullPath(args[0]);
            Directory.CreateDirectory(SettingsStore.DirectoryPath);
            string markerPath = Path.Combine(SettingsStore.DirectoryPath, "settings.json");
            const string marker = "not-valid-json: demo must not load or overwrite this marker";
            File.WriteAllText(markerPath, marker);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                // A null-settings demo constructor must succeed even while stored settings are deliberately invalid.
                using (var emptyDemo = new MainForm(null, true))
                    Check(Field<SwitchSettings>(emptyDemo, "settings").Backend == "otd", "Null-settings demo must bypass SettingsStore.Load.");

                var settings = new SwitchSettings { Backend = "driver", DriverShortcutConfirmed = false };
                using (var form = new MainForm(settings, true))
                using (var watchdog = new System.Windows.Forms.Timer { Interval = 15000 })
                {
                    watchdog.Tick += delegate
                    {
                        failure = new TimeoutException("Demo checks did not finish within 15 seconds.");
                        form.ExitApplication();
                    };
                    form.Shown += async delegate
                    {
                        try { await RunChecks(form, settings, markerPath, marker); }
                        catch (Exception error) { failure = error; }
                        finally { watchdog.Stop(); if (!form.IsDisposed) form.ExitApplication(); }
                    };
                    watchdog.Start();
                    Application.Run(form);
                }
                if (failure != null) throw failure;
                Console.WriteLine("PASS: " + checks + " demo behavior and isolation checks.");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine("FAIL after " + checks + " checks: " + error);
                return 1;
            }
        }
    }
}
