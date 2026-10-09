using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace TabletSwitch
{
    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("shcore.dll")] static extern int SetProcessDpiAwareness(int awareness);
        static string Option(string[] args, string name)
        {
            int index = Array.IndexOf(args, name);
            if (index < 0) return null;
            if (index + 1 >= args.Length) throw new ArgumentException(name + " 缺少参数。");
            return args[index + 1];
        }
        static bool Has(string[] args, string name) { return Array.IndexOf(args, name) >= 0; }
        [STAThread]
        static int Main(string[] args)
        {
            try { SetProcessDpiAwarenessContext(new IntPtr(-4)); }
            catch (EntryPointNotFoundException) { try { SetProcessDpiAwareness(2); } catch (DllNotFoundException) { } }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string output = null;
            bool smoke = Has(args, "--smoke");
            bool demo = Has(args, "--demo") || Has(args, "--demo-record") ||
                String.Equals(Path.GetFileName(Application.ExecutablePath), "TabletScreenSwitcherDemo.exe", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(Path.GetFileName(Application.ExecutablePath), "PenBridge-Demo.exe", StringComparison.OrdinalIgnoreCase);
#if DEMO
            demo = true;
#endif
            if (demo || smoke) SettingsStore.DirectoryPath = Path.Combine(Path.GetTempPath(), "TabletScreenSwitcher-" + (demo ? "Demo-" : "Smoke-") + Guid.NewGuid().ToString("N"));
            try
            {
                output = Option(args, "--out");
                if (demo && (Has(args, "--check") || Has(args, "--next") || Has(args, "--screen")))
                    throw new ArgumentException("演示模式不能与真实切换命令组合使用；请在演示窗口中操作。");
                if (Has(args, "--list")) { Write(demo ? DemoEnvironment.GetDisplays() : DisplayCatalog.Get(), output); return 0; }
                string frameDirectory = demo ? Option(args, "--demo-record") : null;
                if (smoke && frameDirectory != null) throw new ArgumentException("--smoke 和 --demo-record 请分别执行。");
                bool acquired;
                using (var mutex = new Mutex(true, smoke || frameDirectory != null ? "Local\\TabletScreenSwitcher-Smoke-" + Guid.NewGuid().ToString("N") : demo ? "Local\\TabletScreenSwitcher-Demo" : "Local\\TabletScreenSwitcher", out acquired))
                {
                    if (!acquired) throw new InvalidOperationException(demo ? "交互演示已经打开，请切换到演示窗口。" : "工具已经运行。请点击托盘图标或按 Ctrl+Alt+F10 打开；命令行切换需先从托盘退出。");
                    var settings = demo ? new SwitchSettings { Backend = "otd", OtdCliPath = "DEMO", TabletName = "演示数位板" } : SettingsStore.Load();
                    if (smoke && Option(args, "--smoke-backend") == "driver") settings.Backend = "driver";
                    bool command = Has(args, "--check") || Has(args, "--next") || Has(args, "--screen");
                    if (command)
                    {
                        var service = new SwitchService(settings);
                        var displays = DisplayCatalog.Get();
                        SwitchResult result = Task.Run(async () => {
                            if (Has(args, "--check")) return await service.ProbeAsync();
                            if (Has(args, "--next")) return await service.CycleAsync(displays);
                            int index;
                            if (!Int32.TryParse(Option(args, "--screen"), out index) || index < 1 || index > displays.Count) return SwitchResult.Fail("屏幕编号无效，请先运行 --list。");
                            return await service.SelectAsync(displays[index - 1]);
                        }).GetAwaiter().GetResult();
                        Write(result, output); return result.Success ? 0 : 1;
                    }
                    if (args.Length > 0 && !demo && !smoke && !Has(args, "--out")) throw new ArgumentException("支持 --demo、--list、--check、--next、--screen 1，可加 --out 结果文件路径。");
                    using (var form = new MainForm(settings, demo))
                    {
                        Exception smokeError = null;
                        if (frameDirectory != null)
                        {
                            form.Shown += async (sender, ev) => {
                                try { await RecordDemoAsync(form, frameDirectory); }
                                catch (Exception e) { smokeError = e; }
                                finally { form.ExitApplication(); }
                            };
                        }
                        using (var timer = new System.Windows.Forms.Timer { Interval = 1400 })
                        {
                            if (smoke)
                            {
                                timer.Tick += (sender, ev) => {
                                    timer.Stop();
                                    try
                                    {
                                        string screenshot = Option(args, "--screenshot");
                                        if (!String.IsNullOrEmpty(screenshot))
                                        {
                                            using (var bitmap = new Bitmap(form.Width, form.Height))
                                            { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size)); bitmap.Save(Path.GetFullPath(screenshot), ImageFormat.Png); }
                                        }
                                    }
                                    catch (Exception e) { smokeError = e; }
                                    finally { form.ExitApplication(); }
                                };
                                timer.Start();
                            }
                            Application.Run(form);
                        }
                        if (smokeError != null) throw smokeError;
                        if (smoke) Write(SwitchResult.Ok("界面启动与退出检查通过。未发送快捷键，也未修改驱动。", false, null), output);
                        if (frameDirectory != null) Write(SwitchResult.Ok("已记录 80 帧交互演示。所有屏幕、数位板和笔尖位置均为模拟。", false, null), output);
                    }
                    mutex.ReleaseMutex();
                    return 0;
                }
            }
            catch (Exception e)
            {
                SettingsStore.Log("ERROR " + e.Message);
                Write(SwitchResult.Fail(e.Message), output);
                if (args.Length == 0) MessageBox.Show(e.Message, "数位板切屏", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 1;
            }
        }
        static async Task RecordDemoAsync(MainForm form, string directory)
        {
            directory = Path.GetFullPath(directory);
            Directory.CreateDirectory(directory);
            await Task.Delay(700);
            int[] order = { 2, 1, 3, 2 };
            for (int section = 0; section < order.Length; section++)
            {
                await form.DemoSelectAsync(order[section]);
                for (int frame = 0; frame < 20; frame++)
                {
                    double phase = frame / 19.0 * Math.PI * 2;
                    form.DemoMovePen((float)(0.5 + 0.34 * Math.Cos(phase)), (float)(0.5 + 0.29 * Math.Sin(phase)));
                    form.Refresh();
                    await Task.Delay(70);
                    using (var bitmap = new Bitmap(form.Width, form.Height))
                    {
                        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                        bitmap.Save(Path.Combine(directory, "frame-" + (section * 20 + frame).ToString("D3") + ".png"), ImageFormat.Png);
                    }
                }
            }
        }
        static void Write(object value, string path)
        {
            string json = SettingsStore.Json().Serialize(value);
            if (!String.IsNullOrEmpty(path)) File.WriteAllText(Path.GetFullPath(path), json, new UTF8Encoding(false));
            else Console.WriteLine(json);
        }
    }
}
