using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace TabletSwitch
{
    public class DisplayTarget
    {
        public string Id, DeviceName, Label;
        public Rectangle Bounds;
        public bool Primary;
    }

    public static class DisplayCatalog
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct DisplayDevice
        {
            public int Size;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern bool EnumDisplayDevices(string device, uint index, ref DisplayDevice result, uint flags);
        public static List<DisplayTarget> Get()
        {
            var result = new List<DisplayTarget>();
            foreach (var screen in Screen.AllScreens.OrderBy(s => s.Bounds.X).ThenBy(s => s.Bounds.Y))
            {
                var device = new DisplayDevice { Size = Marshal.SizeOf(typeof(DisplayDevice)) };
                string id = screen.DeviceName;
                if (EnumDisplayDevices(screen.DeviceName, 0, ref device, 1) && !String.IsNullOrEmpty(device.DeviceId)) id = device.DeviceId;
                result.Add(new DisplayTarget { Id = id, DeviceName = screen.DeviceName, Bounds = screen.Bounds, Primary = screen.Primary,
                    Label = String.Format("屏幕 {0}  ·  {1} × {2}{3}", result.Count + 1, screen.Bounds.Width, screen.Bounds.Height, screen.Primary ? "  ·  主屏" : "") });
            }
            return result;
        }
        public static string Fingerprint(IEnumerable<DisplayTarget> displays)
        {
            return String.Join("|", displays.OrderBy(d => d.Id).Select(d => d.Id + ":" + d.Bounds.ToString()));
        }
    }

    public class SwitchSettings
    {
        public string Backend { get; set; }
        public string DriverHotkey { get; set; }
        public string DriverProcessName { get; set; }
        public bool DriverShortcutConfirmed { get; set; }
        public string OtdCliPath { get; set; }
        public string TabletName { get; set; }
        public SwitchSettings()
        {
            Backend = "otd"; DriverHotkey = "Ctrl+Alt+Shift+F12";
            DriverProcessName = ""; OtdCliPath = ""; TabletName = "";
        }
        public SwitchSettings Copy() { return (SwitchSettings)MemberwiseClone(); }
    }

    public static class SettingsStore
    {
        public static string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TabletScreenSwitcher");
        public static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength = 8 * 1024 * 1024 }; }
        public static SwitchSettings Load()
        {
            Directory.CreateDirectory(DirectoryPath);
            string path = Path.Combine(DirectoryPath, "settings.json");
            if (!File.Exists(path)) return new SwitchSettings();
            try { return Json().Deserialize<SwitchSettings>(File.ReadAllText(path, Encoding.UTF8)) ?? new SwitchSettings(); }
            catch (Exception e) { throw new InvalidDataException("设置文件无法读取，原文件已保留：" + path, e); }
        }
        public static void Save(SwitchSettings settings)
        {
            if (settings.Backend != "driver" && settings.Backend != "otd") throw new ArgumentException("未知切换方式。");
            Directory.CreateDirectory(DirectoryPath);
            string path = Path.Combine(DirectoryPath, "settings.json"), temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, Json().Serialize(settings), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak"); else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        public static void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(DirectoryPath);
                string path = Path.Combine(DirectoryPath, "activity.log");
                if (File.Exists(path) && new FileInfo(path).Length > 1024 * 1024) File.WriteAllText(path, "", Encoding.UTF8);
                File.AppendAllText(path, DateTimeOffset.Now.ToString("o") + " " + message.Replace('\r', ' ').Replace('\n', ' ') + Environment.NewLine, Encoding.UTF8);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public class SwitchResult
    {
        public bool Success, Verified;
        public string Message, TargetId;
        public static SwitchResult Ok(string message, bool verified, string targetId)
        { return new SwitchResult { Success = true, Verified = verified, Message = message, TargetId = targetId }; }
        public static SwitchResult Fail(string message) { return new SwitchResult { Message = message }; }
    }

    public class DisplayArea
    {
        public double Width, Height, X, Y;
        public bool Matches(DisplayArea other)
        { return other != null && Math.Abs(Width - other.Width) < 0.1 && Math.Abs(Height - other.Height) < 0.1 && Math.Abs(X - other.X) < 0.1 && Math.Abs(Y - other.Y) < 0.1; }
        public string[] Arguments(string tablet)
        { return new[] { "setdisplayarea", tablet, Number(Width), Number(Height), Number(X), Number(Y) }; }
        static string Number(double value) { return value.ToString("0.###", CultureInfo.InvariantCulture); }
    }

    public class OtdSnapshot
    {
        public string JsonText;
        public Dictionary<string, object> Root;
        public static OtdSnapshot Parse(string json)
        {
            var root = SettingsStore.Json().DeserializeObject(json) as Dictionary<string, object>;
            if (root == null || !root.ContainsKey("Profiles")) throw new InvalidDataException("OTD 设置格式不受支持：缺少 Profiles。");
            return new OtdSnapshot { Root = root, JsonText = json };
        }
        public List<string> TabletNames()
        { return Profiles().Select(p => Convert.ToString(p["Tablet"], CultureInfo.InvariantCulture)).ToList(); }
        IEnumerable<Dictionary<string, object>> Profiles()
        {
            var profiles = Root["Profiles"] as IEnumerable;
            if (profiles == null) throw new InvalidDataException("OTD Profiles 格式不受支持。");
            foreach (object item in profiles)
            {
                var profile = item as Dictionary<string, object>;
                if (profile == null || !profile.ContainsKey("Tablet") || !(profile["Tablet"] is string)) throw new InvalidDataException("OTD Tablet 字段格式不受支持。");
                yield return profile;
            }
        }
        public Dictionary<string, object> Profile(string tablet)
        {
            var matches = Profiles().Where(p => String.Equals((string)p["Tablet"], tablet, StringComparison.Ordinal)).ToList();
            if (matches.Count != 1) throw new InvalidOperationException("请填写唯一且完全匹配的 OTD 数位板名称。当前配置：" + String.Join("、", TabletNames()));
            return matches[0];
        }
        static Dictionary<string, object> Map(Dictionary<string, object> map, string key)
        {
            object value;
            if (!map.TryGetValue(key, out value) || !(value is Dictionary<string, object>)) throw new InvalidDataException("OTD 设置缺少 " + key + "。");
            return (Dictionary<string, object>)value;
        }
        public DisplayArea Area(string tablet)
        {
            var map = Map(Map(Profile(tablet), "AbsoluteModeSettings"), "Display");
            var area = new DisplayArea { Width = Read(map, "Width"), Height = Read(map, "Height"), X = Read(map, "X"), Y = Read(map, "Y") };
            if (area.Width <= 0 || area.Height <= 0) throw new InvalidDataException("OTD 显示区域宽高必须大于零。");
            return area;
        }
        static double Read(Dictionary<string, object> map, string key)
        {
            object value;
            if (!map.TryGetValue(key, out value)) throw new InvalidDataException("OTD 显示区域缺少 " + key + "。");
            if (!(value is int || value is long || value is double || value is float || value is decimal))
                throw new InvalidDataException("OTD 显示区域必须使用有效数字：" + key + "。");
            double number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            if (Double.IsNaN(number) || Double.IsInfinity(number)) throw new InvalidDataException("OTD 显示区域含无效数字。");
            return number;
        }
        public string OutputPath(string tablet)
        {
            var mode = Map(Profile(tablet), "OutputMode");
            return mode.ContainsKey("Path") ? Convert.ToString(mode["Path"], CultureInfo.InvariantCulture) : "";
        }
        public void RequireAbsolute(string tablet)
        {
            string path = OutputPath(tablet);
            if (path != "OpenTabletDriver.Desktop.Output.AbsoluteMode" && path != "VoiDPlugins.OutputMode.WinInkAbsoluteMode")
                throw new InvalidOperationException("当前输出模式不是已知的绝对定位 / Windows Ink 模式：" + path + "。请先在 OTD 中配置绘画输出。");
        }
        public string DrawingNote(string tablet)
        { return OutputPath(tablet) == "OpenTabletDriver.Desktop.Output.AbsoluteMode" ? "当前是鼠标输出，PS 笔压需要先在 OTD 配置 Windows Ink。" : "Windows Ink 模式；实际笔压仍需在 PS 实笔确认。"; }
    }

    public interface IOtdClient
    {
        Task CheckAsync(string executable);
        Task<OtdSnapshot> SnapshotAsync(string executable);
        Task SetAreaAsync(string executable, string tablet, DisplayArea area);
    }

    public class OtdClient : IOtdClient
    {
        string checkedExecutable;
        DateTime checkedWriteTime;
        public static string Quote(string argument)
        {
            // Windows CommandLineToArgvW quoting, without passing through a shell.
            var output = new StringBuilder("\""); int slashes = 0;
            foreach (char c in argument)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') output.Append('\\', slashes * 2 + 1).Append('"');
                else output.Append('\\', slashes).Append(c);
                slashes = 0;
            }
            return output.Append('\\', slashes * 2).Append('"').ToString();
        }
        async Task<string> RunAsync(string executable, params string[] arguments)
        {
            if (String.IsNullOrWhiteSpace(executable) || !Path.IsPathRooted(executable) || !File.Exists(executable))
                throw new FileNotFoundException("请选择 OpenTabletDriver.Console.exe 的完整路径。");
            var start = new ProcessStartInfo(executable, String.Join(" ", arguments.Select(Quote))) {
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Path.GetDirectoryName(executable) };
            using (var process = new Process { StartInfo = start })
            {
                process.Start();
                Task<string> stdout = process.StandardOutput.ReadToEndAsync(), stderr = process.StandardError.ReadToEndAsync();
                bool exited = await Task.Run(() => process.WaitForExit(6000));
                if (!exited) { try { process.Kill(); } catch (InvalidOperationException) { } throw new TimeoutException("OTD 6 秒内没有响应；请检查 daemon。驱动可能已接受请求，请核对当前映射；未自动重试。"); }
                string output = (await stdout) + "\n" + (await stderr);
                if (process.ExitCode != 0) throw new InvalidOperationException("OTD 返回错误 " + process.ExitCode + "：" + output.Trim());
                if (System.Text.RegularExpressions.Regex.IsMatch(output, @"(?im)(unrecognized (command|argument)|unhandled exception|^\s*(error|fatal)\b|no tablet found|failed to connect)"))
                    throw new InvalidOperationException("OTD 未接受请求：" + output.Trim());
                return output;
            }
        }
        public async Task CheckAsync(string executable)
        {
            if (!String.IsNullOrWhiteSpace(executable) && File.Exists(executable) &&
                String.Equals(executable, checkedExecutable, StringComparison.OrdinalIgnoreCase) && File.GetLastWriteTimeUtc(executable) == checkedWriteTime) return;
            string help = await RunAsync(executable, "--help");
            if (!help.Contains("setdisplayarea") || !help.Contains("savesettings"))
                throw new InvalidOperationException("此 OTD Console 的命令不兼容；需要 setdisplayarea 和 savesettings（按 0.6.x 接口实现）。");
            checkedExecutable = executable;
            checkedWriteTime = File.GetLastWriteTimeUtc(executable);
        }
        public async Task<OtdSnapshot> SnapshotAsync(string executable)
        {
            Directory.CreateDirectory(SettingsStore.DirectoryPath);
            string path = Path.Combine(SettingsStore.DirectoryPath, "snapshot-" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                await RunAsync(executable, "savesettings", path);
                if (!File.Exists(path)) throw new InvalidOperationException("OTD 没有生成实时设置，可能命令不兼容或 daemon 未启动。");
                return OtdSnapshot.Parse(File.ReadAllText(path, Encoding.UTF8));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
        public async Task SetAreaAsync(string executable, string tablet, DisplayArea area)
        { await RunAsync(executable, area.Arguments(tablet)); }
    }

    public static class DriverShortcut
    {
        [StructLayout(LayoutKind.Sequential)] struct MouseInput { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)] struct KeyboardInput { public ushort Key, Scan; public uint Flags, Time; public UIntPtr Extra; }
        [StructLayout(LayoutKind.Explicit)] struct InputUnion { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyboardInput Keyboard; }
        [StructLayout(LayoutKind.Sequential)] struct Input { public uint Type; public InputUnion Data; }
        [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint count, Input[] inputs, int size);
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
        public static List<ushort> Parse(string hotkey)
        {
            if (String.IsNullOrWhiteSpace(hotkey)) throw new ArgumentException("请填写原厂驱动切屏快捷键。");
            var keys = new List<ushort>(); bool hasMain = false;
            foreach (string raw in hotkey.Split('+'))
            {
                string token = raw.Trim(); ushort key;
                switch (token.ToUpperInvariant())
                {
                    case "CTRL": case "CONTROL": key = 0x11; break;
                    case "ALT": key = 0x12; break;
                    case "SHIFT": key = 0x10; break;
                    case "WIN": key = 0x5B; break;
                    default:
                        if (hasMain) throw new ArgumentException("快捷键只能包含一个普通按键。");
                        Keys parsed;
                        if (token.Length == 1 && token[0] >= '0' && token[0] <= '9') key = (ushort)token[0];
                        else if (Enum.TryParse<Keys>(token, true, out parsed) && (((int)parsed >= 0x41 && (int)parsed <= 0x5A) || ((int)parsed >= 0x70 && (int)parsed <= 0x87) || parsed == Keys.Space)) key = (ushort)parsed;
                        else throw new ArgumentException("快捷键支持 Ctrl / Alt / Shift / Win + 字母、数字、F1–F24 或 Space。");
                        hasMain = true; break;
                }
                if (keys.Contains(key)) throw new ArgumentException("快捷键有重复按键。");
                if (hasMain && (key == 0x10 || key == 0x11 || key == 0x12 || key == 0x5B)) throw new ArgumentException("请把修饰键写在普通按键之前。");
                keys.Add(key);
            }
            if (!hasMain || !keys.Any(k => k == 0x11 || k == 0x12 || k == 0x5B)) throw new ArgumentException("快捷键需要 Ctrl、Alt 或 Win 修饰键。");
            if (keys.Count == 3 && keys.Contains(0x11) && keys.Contains(0x12) && keys.Any(k => k == 0x78 || k == 0x79))
                throw new ArgumentException("该快捷键与本工具的全局快捷键冲突，请在原厂驱动中换一个组合。");
            return keys;
        }
        public static async Task SendAsync(string hotkey)
        {
            List<ushort> keys = Parse(hotkey);
            int[] modifiers = { 0x10, 0x11, 0x12, 0x5B, 0x5C };
            var elapsed = Stopwatch.StartNew();
            while (modifiers.Any(k => (GetAsyncKeyState(k) & 0x8000) != 0))
            {
                if (elapsed.ElapsedMilliseconds > 1800) throw new InvalidOperationException("请松开快捷键后再切换，避免修饰键残留。");
                await Task.Delay(25);
            }
            var inputs = new List<Input>();
            foreach (ushort key in keys) inputs.Add(new Input { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput { Key = key } } });
            foreach (ushort key in keys.AsEnumerable().Reverse()) inputs.Add(new Input { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput { Key = key, Flags = 2 } } });
            uint count = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf(typeof(Input)));
            if (count != inputs.Count)
            {
                var releases = keys.AsEnumerable().Reverse().Select(k => new Input { Type = 1, Data = new InputUnion { Keyboard = new KeyboardInput { Key = k, Flags = 2 } } }).ToArray();
                SendInput((uint)releases.Length, releases, Marshal.SizeOf(typeof(Input)));
                throw new InvalidOperationException("系统没有完整发送快捷键。请确认工具与驱动权限级别一致。");
            }
        }
    }

    public class SwitchService
    {
        readonly SwitchSettings settings;
        readonly IOtdClient client;
        readonly Func<List<DisplayTarget>> getDisplays;
        readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
        string topology;
        DisplayArea previousArea, lastArea;
        string previousTablet, previousExecutable, previousTopology;
        public SwitchService(SwitchSettings settings) : this(settings, new OtdClient(), DisplayCatalog.Get) { }
        public SwitchService(SwitchSettings settings, IOtdClient client, Func<List<DisplayTarget>> getDisplays)
        { this.settings = settings; this.client = client; this.getDisplays = getDisplays; topology = DisplayCatalog.Fingerprint(getDisplays()); }

        async Task<SwitchResult> Guard(Func<SwitchSettings, Task<SwitchResult>> action)
        {
            if (!await gate.WaitAsync(0)) return SwitchResult.Fail("正在处理上一次请求，请稍后再试。");
            try
            {
                SwitchResult result = await action(settings.Copy());
                SettingsStore.Log((result.Success ? "OK " : "FAIL ") + result.Message); return result;
            }
            catch (Exception e) { SettingsStore.Log("FAIL " + e.Message); return SwitchResult.Fail(e.Message); }
            finally { gate.Release(); }
        }
        static void CheckDriver(SwitchSettings cfg)
        {
            if (!cfg.DriverShortcutConfirmed) throw new InvalidOperationException("请先在原厂驱动设置同一个切屏快捷键，再勾选已设置。");
            DriverShortcut.Parse(cfg.DriverHotkey);
            if (!String.IsNullOrWhiteSpace(cfg.DriverProcessName))
            {
                string name = Path.GetFileNameWithoutExtension(cfg.DriverProcessName.Trim());
                var processes = Process.GetProcessesByName(name);
                try { if (processes.Length == 0) throw new InvalidOperationException("未发现原厂驱动进程 " + name + "，请先启动驱动。"); }
                finally { foreach (var process in processes) process.Dispose(); }
            }
        }
        public Task<SwitchResult> ProbeAsync()
        {
            return Guard(async cfg => {
                if (cfg.Backend == "driver")
                {
                    CheckDriver(cfg);
                    return SwitchResult.Ok("快捷键配置有效。原厂接口不能回读屏幕，请用笔验证切换；不会自动发送按键。", false, null);
                }
                RequireOtd(cfg);
                await client.CheckAsync(cfg.OtdCliPath);
                var snapshot = await client.SnapshotAsync(cfg.OtdCliPath);
                if (String.IsNullOrWhiteSpace(cfg.TabletName)) return SwitchResult.Fail("已连接 OTD。请填写数位板名称：" + String.Join("、", snapshot.TabletNames()));
                snapshot.RequireAbsolute(cfg.TabletName);
                var area = snapshot.Area(cfg.TabletName);
                var displays = getDisplays();
                string updatedTopology = DisplayCatalog.Fingerprint(displays);
                if (topology != updatedTopology) previousArea = null;
                topology = updatedTopology;
                var target = displays.FirstOrDefault(d => AreaFor(d, displays).Matches(area));
                return SwitchResult.Ok("已读取 OTD 配置：" + cfg.TabletName + "；" + (target == null ? "当前为自定义映射" : target.Label) + "。" + snapshot.DrawingNote(cfg.TabletName), true, target == null ? null : target.Id);
            });
        }
        public Task<SwitchResult> CycleAsync(List<DisplayTarget> ignored)
        {
            return Guard(async cfg => {
                if (cfg.Backend == "driver")
                {
                    CheckDriver(cfg); await DriverShortcut.SendAsync(cfg.DriverHotkey);
                    return SwitchResult.Ok("已发送原厂切屏请求。无法回读原厂映射，请用笔确认；没有自动重试。", false, null);
                }
                RequireOtd(cfg);
                var displays = CheckedDisplays();
                await client.CheckAsync(cfg.OtdCliPath);
                var before = await client.SnapshotAsync(cfg.OtdCliPath);
                before.RequireAbsolute(cfg.TabletName);
                var area = before.Area(cfg.TabletName);
                int current = displays.FindIndex(d => AreaFor(d, displays).Matches(area));
                return await ApplyAsync(cfg, displays[(current + 1) % displays.Count], before, displays);
            });
        }
        public Task<SwitchResult> SelectAsync(DisplayTarget target)
        {
            return Guard(async cfg => {
                RequireOtd(cfg);
                var displays = CheckedDisplays();
                var current = displays.FirstOrDefault(d => target != null && d.Id == target.Id && d.Bounds == target.Bounds);
                if (current == null) throw new InvalidOperationException("目标屏幕已断开或布局已改变，请刷新并检查连接。");
                await client.CheckAsync(cfg.OtdCliPath);
                var before = await client.SnapshotAsync(cfg.OtdCliPath); before.RequireAbsolute(cfg.TabletName);
                return await ApplyAsync(cfg, current, before, displays);
            });
        }
        static void RequireOtd(SwitchSettings cfg)
        {
            if (cfg.Backend != "otd") throw new InvalidOperationException("原厂快捷键模式只支持驱动自己的循环切换。指定某块屏幕需要 OTD 模式。");
        }
        List<DisplayTarget> CheckedDisplays()
        {
            var displays = getDisplays();
            if (displays.Count == 0) throw new InvalidOperationException("未发现可用屏幕。");
            if (DisplayCatalog.Fingerprint(displays) != topology) throw new InvalidOperationException("屏幕布局已改变。请核对 OTD 中的显示器列表，必要时重启 OTD daemon，再点击“检查连接”。");
            return displays;
        }
        public static DisplayArea AreaFor(DisplayTarget target, List<DisplayTarget> displays)
        {
            return new DisplayArea { Width = target.Bounds.Width, Height = target.Bounds.Height,
                X = target.Bounds.X - displays.Min(d => d.Bounds.Left) + target.Bounds.Width / 2.0,
                Y = target.Bounds.Y - displays.Min(d => d.Bounds.Top) + target.Bounds.Height / 2.0 };
        }
        async Task<SwitchResult> ApplyAsync(SwitchSettings cfg, DisplayTarget target, OtdSnapshot before, List<DisplayTarget> displays)
        {
            DisplayArea desired = AreaFor(target, displays), old = before.Area(cfg.TabletName);
            if (DisplayCatalog.Fingerprint(getDisplays()) != topology) throw new InvalidOperationException("读取配置时屏幕布局发生变化，请检查连接后重试。");
            if (old.Matches(desired)) return SwitchResult.Ok("OTD 已映射到 " + target.Label + "，无需重复切换。" + before.DrawingNote(cfg.TabletName), true, target.Id);
            Directory.CreateDirectory(Path.Combine(SettingsStore.DirectoryPath, "backups"));
            string backup = Path.Combine(SettingsStore.DirectoryPath, "backups", "before-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".json");
            File.WriteAllText(backup, before.JsonText, new UTF8Encoding(false));
            previousArea = old; lastArea = desired; previousTablet = cfg.TabletName; previousExecutable = cfg.OtdCliPath; previousTopology = topology;
            if (DisplayCatalog.Fingerprint(getDisplays()) != topology) throw new InvalidOperationException("切换前屏幕布局发生变化，已取消。");
            await client.SetAreaAsync(cfg.OtdCliPath, cfg.TabletName, desired);
            var after = await client.SnapshotAsync(cfg.OtdCliPath);
            after.RequireAbsolute(cfg.TabletName);
            if (!after.Area(cfg.TabletName).Matches(desired)) throw new InvalidOperationException("OTD 回读与目标不一致；未确认成功，也未自动重试。可检查 OTD，或撤销上次映射。");
            if (DisplayCatalog.Fingerprint(getDisplays()) != topology) throw new InvalidOperationException("切换期间屏幕布局改变，映射结果待核对。请检查连接。");
            return SwitchResult.Ok("OTD 配置已核验 → " + target.Label + "。" + after.DrawingNote(cfg.TabletName), true, target.Id);
        }
        public Task<SwitchResult> RestoreAsync()
        {
            return Guard(async cfg => {
                RequireOtd(cfg); CheckedDisplays();
                if (previousArea == null || cfg.TabletName != previousTablet || cfg.OtdCliPath != previousExecutable) throw new InvalidOperationException("本次运行没有该数位板可撤销的映射。");
                if (topology != previousTopology) throw new InvalidOperationException("屏幕布局已改变，不能恢复旧布局的映射。");
                var snapshot = await client.SnapshotAsync(cfg.OtdCliPath);
                snapshot.RequireAbsolute(cfg.TabletName);
                var area = snapshot.Area(cfg.TabletName);
                if (!area.Matches(lastArea) && !area.Matches(previousArea)) throw new InvalidOperationException("映射已被其他程序修改，为保留你的新设置，未执行撤销。");
                CheckedDisplays();
                await client.SetAreaAsync(cfg.OtdCliPath, cfg.TabletName, previousArea);
                var after = await client.SnapshotAsync(cfg.OtdCliPath);
                CheckedDisplays();
                after.RequireAbsolute(cfg.TabletName);
                if (!after.Area(cfg.TabletName).Matches(previousArea)) throw new InvalidOperationException("撤销回读未通过，请在 OTD 中核对。");
                previousArea = null;
                return SwitchResult.Ok("已恢复上一次显示区域，并通过 OTD 配置回读核验。", true, null);
            });
        }
    }
}
