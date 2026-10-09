using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TabletSwitch
{
    public sealed class MainForm : Form
    {
        private const int HotkeyCycle = 100;
        private const int HotkeyShow = 101;
        private const int WmHotkey = 0x0312;
        private readonly Color Ink = Palette.Ink;
        private readonly Color Muted = Palette.Muted;
        private readonly Color Accent = Palette.Blue;
        private readonly SwitchSettings settings;
        private readonly SwitchService service;
        private readonly bool demo;
        private DemoTabletView demoTablet;
        private Label demoMapping;
        private Button demoPlayButton;
        private bool demoPlaying;
        private int demoPlaybackVersion;
        private readonly List<int> registeredHotkeys = new List<int>();
        private readonly List<Button> selectButtons = new List<Button>();
        private readonly List<Panel> displayCards = new List<Panel>();
        private List<DisplayTarget> displays = new List<DisplayTarget>();
        private FlowLayoutPanel displayList;
        private MonitorLayoutView monitorMap;
        private Label screenSectionTitle;
        private Label displayCount;
        private Label mappingSummary;
        private Panel contentViewport;
        private TableLayoutPanel pageLayout;
        private Panel configurationCard;
        private RoundedButton screensNavigation;
        private RoundedButton configNavigation;
        private ComboBox backend;
        private Panel driverPanel;
        private Panel otdPanel;
        private TextBox driverHotkey;
        private TextBox driverProcess;
        private CheckBox driverConfirmed;
        private TextBox otdPath;
        private TextBox tabletName;
        private Button cycleButton;
        private Button undoButton;
        private Button saveButton;
        private Button probeButton;
        private Button refreshButton;
        private Button browseButton;
        private Label hotkeyStatus;
        private Label backendHint;
        private Label backendValue;
        private TextBox statusText;
        private NotifyIcon tray;
        private ContextMenuStrip trayMenu;
        private bool busy;
        private bool exiting;
        private bool shownOnce;
        private bool trayTipShown;
        private bool eventsAttached;
        private bool cleanedUp;
        private bool layoutInitialized;
        private DateTime lastOperation = DateTime.MinValue;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public MainForm(SwitchSettings initialSettings) : this(initialSettings, false) { }

        public MainForm(SwitchSettings initialSettings, bool demoMode)
        {
            SuspendLayout();
            demo = demoMode;
            settings = initialSettings ?? (demo ? new SwitchSettings() : SettingsStore.Load());
            if (demo)
            {
                settings.Backend = "otd";
                settings.TabletName = DemoEnvironment.TabletName;
                settings.OtdCliPath = "DEMO";
            }
            service = demo ? new SwitchService(settings, new DemoClient(), DemoEnvironment.GetDisplays) : new SwitchService(settings);
            Text = demo ? "PenBridge · 交互演示" : "PenBridge · 数位板切屏";
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            ClientSize = new Size(1160, 862);
            MinimumSize = new Size(1000, 700);
            BackColor = Palette.Background;
            ForeColor = Ink;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { Icon = SystemIcons.Application; }
            BuildLayout();
            LoadFields();
            if (!demo) BuildTray();
            RefreshDisplays(false);
            if (!demo)
            {
                SystemEvents.DisplaySettingsChanged += DisplaySettingsChanged;
                eventsAttached = true;
            }
            SetStatus(demo ? "演示已就绪。点击上方任意屏幕，再在下方模拟数位板上移动鼠标，观察笔的位置如何映射。"
                : settings.Backend == "otd"
                ? "就绪。选择 OTD 控制台程序，填写数位板完整名称，再点击“检测连接”。\r\n读取配置后单击上方目标屏幕。Photoshop 笔压需先配置 Windows Ink，详见使用说明。"
                : "就绪。先在原厂驱动中设置并验证切屏快捷键，再填写下方配置。\r\n原厂模式只发送循环切屏请求，无法指定屏幕；请用笔确认切换结果。", false);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(true);
            layoutInitialized = true;
        }

        private Label MakeLabel(string text, float size, bool bold)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.ForeColor = Ink;
            label.Font = new Font(Font.FontFamily, size, bold ? FontStyle.Bold : FontStyle.Regular);
            label.Margin = new Padding(0, 0, 0, 4);
            return label;
        }

        private Button MakeButton(string text, int width, bool primary)
        {
            RoundedButton button = new RoundedButton();
            button.Text = text;
            button.Size = new Size(width, 37);
            button.Primary = primary;
            button.BackColor = primary ? Accent : Color.FromArgb(245, 245, 247);
            button.ForeColor = primary ? Color.White : Ink;
            button.Margin = new Padding(0, 0, 8, 0);
            return button;
        }

        private TextBox MakeTextBox()
        {
            TextBox box = new TextBox();
            box.Dock = DockStyle.Fill;
            box.BorderStyle = BorderStyle.FixedSingle;
            box.BackColor = Color.FromArgb(252, 252, 253);
            box.ForeColor = Ink;
            box.Margin = new Padding(0, 2, 8, 0);
            return box;
        }

        private void BuildLayout()
        {
            TableLayoutPanel shell = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 184));
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(shell);
            shell.Controls.Add(BuildSidebar(), 0, 0);

            contentViewport = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Margin = Padding.Empty, BackColor = Palette.Background };
            shell.Controls.Add(contentViewport, 1, 0);
            pageLayout = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, RowCount = 6, Height = 856, Padding = new Padding(28, 22, 28, 12), Margin = Padding.Empty, BackColor = Palette.Background };
            pageLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pageLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
            pageLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 300));
            pageLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
            pageLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 236));
            pageLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
            pageLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            pageLayout.MinimumSize = new Size(760, 856);
            contentViewport.Controls.Add(pageLayout);

            TableLayoutPanel header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            header.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Label title = MakeLabel("你的屏幕，随笔切换。", 23F, true);
            title.Margin = Padding.Empty;
            header.Controls.Add(title, 0, 0);
            RoundedPanel modeBadge = new RoundedPanel { Dock = DockStyle.Top, Height = 28, BackColor = demo ? Palette.BlueWash : Color.FromArgb(233, 235, 240), CornerRadius = 14, DrawBorder = false, Margin = new Padding(0, 9, 0, 0) };
            Label badge = MakeLabel(demo ? "●  交互演示" : "●  本机模式", 8.5F, true);
            badge.AutoSize = false;
            badge.Dock = DockStyle.Fill;
            badge.TextAlign = ContentAlignment.MiddleCenter;
            badge.ForeColor = demo ? Accent : Muted;
            badge.BackColor = Color.Transparent;
            badge.Margin = Padding.Empty;
            modeBadge.Controls.Add(badge);
            header.Controls.Add(modeBadge, 1, 0);
            Label subtitle = MakeLabel(demo ? "点选一块屏幕，体验笔尖在多屏之间自由切换。" : "让数位板回到你正在创作的那块屏幕。", 10F, false);
            subtitle.ForeColor = Muted;
            subtitle.Margin = new Padding(1, 4, 0, 0);
            header.Controls.Add(subtitle, 0, 1);
            header.SetColumnSpan(subtitle, 2);
            pageLayout.Controls.Add(header, 0, 0);

            RoundedPanel displaySection = new RoundedPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 17, 20, 14), Margin = new Padding(0, 0, 0, 12) };
            TableLayoutPanel displayContent = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty, BackColor = Color.White };
            displayContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            displayContent.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            displayContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            TableLayoutPanel mapHeader = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            mapHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            mapHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126));
            screenSectionTitle = MakeLabel("屏幕布局", 10F, true);
            screenSectionTitle.Dock = DockStyle.Fill;
            screenSectionTitle.AutoSize = false;
            mapHeader.Controls.Add(screenSectionTitle, 0, 0);
            displayCount = MakeLabel("", 8.5F, false);
            displayCount.ForeColor = Muted;
            displayCount.Dock = DockStyle.Fill;
            displayCount.AutoSize = false;
            displayCount.TextAlign = ContentAlignment.TopRight;
            mapHeader.Controls.Add(displayCount, 1, 0);
            displayContent.Controls.Add(mapHeader, 0, 0);
            monitorMap = new MonitorLayoutView { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 9), BackColor = Color.White };
            monitorMap.DemoMode = demo;
            monitorMap.TargetClicked += async delegate(DisplayTarget target) { await RunOperation("select", target, true); };
            displayContent.Controls.Add(monitorMap, 0, 1);
            displayList = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true, Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Color.White };
            displayContent.Controls.Add(displayList, 0, 2);
            displaySection.Controls.Add(displayContent);
            pageLayout.Controls.Add(displaySection, 0, 1);

            TableLayoutPanel actionRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 424));
            actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = false };
            cycleButton = MakeButton("切换到下一屏  →", 162, true);
            cycleButton.Click += async delegate { await RunOperation("cycle", null, true); };
            undoButton = MakeButton("撤销映射", 116, false);
            undoButton.Click += async delegate { await RunOperation("undo", null, true); };
            refreshButton = MakeButton("刷新屏幕", 112, false);
            refreshButton.Click += delegate { RefreshDisplays(true); };
            actions.Controls.Add(cycleButton);
            actions.Controls.Add(undoButton);
            actions.Controls.Add(refreshButton);
            actionRow.Controls.Add(actions, 0, 0);
            mappingSummary = MakeLabel(demo ? "等待模拟映射" : "尚未核验映射", 8.5F, false);
            mappingSummary.Dock = DockStyle.Top;
            mappingSummary.Height = 37;
            mappingSummary.AutoSize = false;
            mappingSummary.ForeColor = Muted;
            mappingSummary.TextAlign = ContentAlignment.MiddleRight;
            actionRow.Controls.Add(mappingSummary, 1, 0);
            pageLayout.Controls.Add(actionRow, 0, 2);

            RoundedPanel config = new RoundedPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 16, 20, 14), Margin = new Padding(0, 0, 0, 12) };
            TableLayoutPanel configLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty, BackColor = Color.White };
            configLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            configLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
            configLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            configLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            config.Controls.Add(configLayout);
            TableLayoutPanel backendRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            backendRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94));
            backendRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 318));
            backendRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            Label backendLabel = MakeLabel("连接设置", 10F, true);
            backendLabel.Margin = new Padding(0, 4, 0, 0);
            backend = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 0), FlatStyle = FlatStyle.Standard, BackColor = Color.White, ForeColor = Ink, IntegralHeight = false, DropDownHeight = 100 };
            backend.Items.Add("原厂驱动快捷键（保留当前驱动）");
            backend.Items.Add("OpenTabletDriver（直接选择屏幕）");
            backend.SelectedIndexChanged += delegate { UpdateBackendView(); };
            backendRow.Controls.Add(backendLabel, 0, 0);
            backendRow.Controls.Add(backend, 1, 0);
            backendValue = MakeLabel("", 8F, false);
            backendValue.ForeColor = Accent;
            backendValue.Dock = DockStyle.Fill;
            backendValue.AutoSize = false;
            backendValue.TextAlign = ContentAlignment.TopRight;
            backendValue.Margin = new Padding(8, 5, 0, 0);
            backendRow.Controls.Add(backendValue, 2, 0);
            configLayout.Controls.Add(backendRow, 0, 0);
            backendHint = MakeLabel("", 8.5F, false);
            backendHint.AutoSize = false;
            backendHint.Dock = DockStyle.Fill;
            backendHint.ForeColor = Muted;
            configLayout.Controls.Add(backendHint, 0, 1);
            Panel fields = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Color.White };
            driverPanel = BuildDriverFields();
            otdPanel = BuildOtdFields();
            fields.Controls.Add(driverPanel);
            fields.Controls.Add(otdPanel);
            configLayout.Controls.Add(fields, 0, 2);
            FlowLayoutPanel configActions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            saveButton = MakeButton("保存设置", 108, false);
            saveButton.Click += delegate { SaveFields(true); };
            probeButton = MakeButton("检测连接", 108, false);
            probeButton.Click += async delegate { await RunOperation("probe", null, true); };
            configActions.Controls.Add(saveButton);
            configActions.Controls.Add(probeButton);
            Label saveHint = MakeLabel("保存后，全局快捷键使用这组设置", 8F, false);
            saveHint.ForeColor = Muted;
            saveHint.Margin = new Padding(8, 10, 0, 0);
            configActions.Controls.Add(saveHint);
            configLayout.Controls.Add(configActions, 0, 3);
            if (demo)
            {
                config.Visible = false;
                config.Enabled = false;
                configurationCard = BuildDemoPanel();
                pageLayout.Controls.Add(configurationCard, 0, 3);
                // Shared operation fields remain alive but demo never connects to a real adapter.
                Controls.Add(config);
            }
            else
            {
                configurationCard = config;
                pageLayout.Controls.Add(config, 0, 3);
            }

            RoundedPanel statusPanel = new RoundedPanel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(18, 13, 18, 12), Margin = new Padding(0, 0, 0, 7), CornerRadius = 15 };
            TableLayoutPanel statusContent = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty, BackColor = Color.White };
            statusContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            statusContent.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Label statusHeading = MakeLabel("活动状态", 8.5F, true);
            statusHeading.ForeColor = Muted;
            statusContent.Controls.Add(statusHeading, 0, 0);
            statusText = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Color.White, ForeColor = Ink, ScrollBars = ScrollBars.Vertical, TabStop = false, Margin = Padding.Empty, Font = new Font(Font.FontFamily, 8.5F) };
            statusContent.Controls.Add(statusText, 0, 1);
            statusPanel.Controls.Add(statusContent);
            pageLayout.Controls.Add(statusPanel, 0, 4);

            TableLayoutPanel footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 68));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 83));
            hotkeyStatus = MakeLabel(demo ? "纯模拟体验 · 不连接驱动，不改变真实设备" : "Ctrl + Alt + F9  轮换屏幕    ·    F10  显示窗口", 8F, false);
            hotkeyStatus.Dock = DockStyle.Fill;
            hotkeyStatus.AutoSize = false;
            hotkeyStatus.ForeColor = Muted;
            hotkeyStatus.TextAlign = ContentAlignment.MiddleLeft;
            footer.Controls.Add(hotkeyStatus, 0, 0);
            LinkLabel help = new LinkLabel { Text = "使用说明", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, LinkColor = Muted, ActiveLinkColor = Accent, LinkBehavior = LinkBehavior.HoverUnderline, Font = new Font(Font.FontFamily, 8F) };
            help.LinkClicked += delegate { OpenHelp(); };
            LinkLabel logs = new LinkLabel { Text = "设置与日志", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight, LinkColor = Muted, ActiveLinkColor = Accent, LinkBehavior = LinkBehavior.HoverUnderline, Font = new Font(Font.FontFamily, 8F) };
            logs.LinkClicked += delegate { OpenLogs(); };
            footer.Controls.Add(help, 1, 0);
            footer.Controls.Add(logs, 2, 0);
            pageLayout.Controls.Add(footer, 0, 5);
        }

        private Panel BuildSidebar()
        {
            Panel side = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(239, 239, 242), Margin = Padding.Empty, Padding = new Padding(17, 28, 17, 20) };
            TableLayoutPanel stack = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Margin = Padding.Empty, BackColor = side.BackColor };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
            stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 135));
            Panel brand = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            BrandMark mark = new BrandMark { Location = new Point(9, 0), Size = new Size(40, 40) };
            Label brandName = MakeLabel("PenBridge", 18F, true);
            brandName.Font = new Font("Segoe UI", 18F, FontStyle.Bold);
            brandName.Location = new Point(7, 47);
            Label brandCaption = MakeLabel("数位板切屏", 9F, false);
            brandCaption.ForeColor = Muted;
            brandCaption.Location = new Point(9, 80);
            brand.Controls.Add(mark);
            brand.Controls.Add(brandName);
            brand.Controls.Add(brandCaption);
            stack.Controls.Add(brand, 0, 0);
            Label group = MakeLabel("工作空间", 8F, false);
            group.ForeColor = Muted;
            group.Margin = new Padding(11, 4, 0, 0);
            stack.Controls.Add(group, 0, 1);
            screensNavigation = (RoundedButton)MakeButton("▣   屏幕映射", 150, false);
            screensNavigation.Dock = DockStyle.Top;
            screensNavigation.TextAlign = ContentAlignment.MiddleLeft;
            screensNavigation.Selected = true;
            screensNavigation.Margin = Padding.Empty;
            screensNavigation.Click += delegate { NavigateTo(false); };
            stack.Controls.Add(screensNavigation, 0, 2);
            configNavigation = (RoundedButton)MakeButton(demo ? "◇   模拟数位板" : "◇   连接设置", 150, false);
            configNavigation.Dock = DockStyle.Top;
            configNavigation.Quiet = true;
            configNavigation.TextAlign = ContentAlignment.MiddleLeft;
            configNavigation.Margin = Padding.Empty;
            configNavigation.Click += delegate { NavigateTo(true); };
            stack.Controls.Add(configNavigation, 0, 3);
            Panel note = new Panel { Dock = DockStyle.Fill, Margin = new Padding(10, 0, 8, 0) };
            Label workflow = MakeLabel("专注于创作", 9F, true);
            workflow.Location = new Point(0, 0);
            Label caption = MakeLabel(demo ? "体验横屏、竖屏与多屏\r\n映射，无需连接数位板。\r\n\r\nDEMO  /  模拟环境" : "为 Photoshop 多屏\r\n工作流准备。\r\n\r\nWINDOWS  /  桌面工具", 8F, false);
            caption.AutoSize = false;
            caption.SetBounds(0, 27, 128, 108);
            caption.ForeColor = Muted;
            note.Controls.Add(workflow);
            note.Controls.Add(caption);
            stack.Controls.Add(note, 0, 5);
            side.Controls.Add(stack);
            return side;
        }

        private void NavigateTo(bool configuration)
        {
            if (contentViewport == null) return;
            screensNavigation.Selected = !configuration;
            configNavigation.Selected = configuration;
            screensNavigation.Invalidate();
            configNavigation.Invalidate();
            if (configuration)
            {
                contentViewport.ScrollControlIntoView(configurationCard);
                if (demo) demoPlayButton.Focus(); else backend.Focus();
            }
            else
            {
                contentViewport.AutoScrollPosition = Point.Empty;
                if (selectButtons.Count > 0 && selectButtons[0].Enabled) selectButtons[0].Focus();
            }
        }

        private Panel BuildDemoPanel()
        {
            RoundedPanel panel = new RoundedPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 17, 20, 16), Margin = new Padding(0, 0, 0, 12) };
            TableLayoutPanel content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, BackColor = Color.White };
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 54));
            TableLayoutPanel pad = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0, 0, 26, 0) };
            pad.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
            pad.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            pad.Controls.Add(MakeLabel("模拟数位板", 10F, true), 0, 0);
            demoTablet = new DemoTabletView { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Color.White };
            demoTablet.PenMoved += delegate(float x, float y)
            {
                StopDemoPlayback();
                DemoMovePen(x, y);
            };
            pad.Controls.Add(demoTablet, 0, 1);
            content.Controls.Add(pad, 0, 0);
            TableLayoutPanel guide = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
            guide.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
            guide.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            guide.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            demoMapping = MakeLabel("演示数位板 → 屏幕 2 · 主屏", 10F, true);
            demoMapping.AutoSize = false;
            demoMapping.Dock = DockStyle.Fill;
            demoMapping.ForeColor = Accent;
            guide.Controls.Add(demoMapping, 0, 0);
            Label instructions = MakeLabel("移动鼠标，像移动你的笔。\r\n左侧的橙色笔点会映射到已选屏幕。\r\n\r\n点击上方屏幕切换目标，也可以自动演示。\r\n模拟体验不代表真实设备兼容或 PS 压感测试。", 8.5F, false);
            instructions.AutoSize = false;
            instructions.Dock = DockStyle.Fill;
            instructions.ForeColor = Muted;
            guide.Controls.Add(instructions, 0, 1);
            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            demoPlayButton = MakeButton("播放演示  ▷", 128, true);
            demoPlayButton.Click += async delegate
            {
                if (demoPlaying) StopDemoPlayback();
                else await PlayDemoAsync();
            };
            buttons.Controls.Add(demoPlayButton);
            Label hint = MakeLabel("横屏 → 竖屏 → 主屏", 8F, false);
            hint.ForeColor = Muted;
            hint.Margin = new Padding(5, 10, 0, 0);
            buttons.Controls.Add(hint);
            guide.Controls.Add(buttons, 0, 2);
            content.Controls.Add(guide, 1, 0);
            panel.Controls.Add(content);
            return panel;
        }

        public Task DemoSelectAsync(int oneBased)
        {
            if (!demo) throw new InvalidOperationException("此操作仅用于交互演示。");
            if (oneBased < 1 || oneBased > displays.Count) throw new ArgumentOutOfRangeException("oneBased");
            return RunOperation("select", displays[oneBased - 1], false);
        }

        public void DemoMovePen(float x, float y)
        {
            if (!demo || IsDisposed || exiting) return;
            PointF point = new PointF(Math.Max(0, Math.Min(1, x)), Math.Max(0, Math.Min(1, y)));
            demoTablet.PenPosition = point;
            monitorMap.DemoPenPosition = point;
        }

        private void StopDemoPlayback()
        {
            demoPlaybackVersion++;
            demoPlaying = false;
            if (demoPlayButton != null && !demoPlayButton.IsDisposed) demoPlayButton.Text = "播放演示  ▷";
        }

        public async Task PlayDemoAsync()
        {
            if (!demo || demoPlaying || IsDisposed || exiting) return;
            demoPlaying = true;
            int version = ++demoPlaybackVersion;
            demoPlayButton.Text = "停止演示";
            try
            {
                // Each screen receives the same normalized pen motion; portrait mapping is immediately visible.
                foreach (int screen in new[] { 1, 3, 2 })
                {
                    await Task.Delay(500);
                    if (IsDisposed || exiting || version != demoPlaybackVersion) return;
                    await DemoSelectAsync(screen);
                    for (int step = 0; step < 72; step++)
                    {
                        if (IsDisposed || exiting || version != demoPlaybackVersion) return;
                        double angle = step * Math.PI * 2 / 71;
                        DemoMovePen((float)(0.5 + 0.36 * Math.Cos(angle)), (float)(0.5 + 0.34 * Math.Sin(angle)));
                        await Task.Delay(35);
                    }
                }
                if (!IsDisposed && !exiting && version == demoPlaybackVersion)
                    SetStatus("自动演示完成：数位板已依次映射到左侧横屏、右侧竖屏和主屏。你可以继续点击屏幕并移动模拟笔。", false);
            }
            finally
            {
                if (version == demoPlaybackVersion) StopDemoPlayback();
            }
        }

        private Panel BuildDriverFields()
        {
            Panel panel = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            TableLayoutPanel grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 3, Margin = Padding.Empty };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 114));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Label keyLabel = MakeLabel("驱动快捷键", 9F, false);
            keyLabel.Margin = new Padding(0, 5, 0, 0);
            grid.Controls.Add(keyLabel, 0, 0);
            driverHotkey = MakeTextBox();
            grid.Controls.Add(driverHotkey, 1, 0);
            Label processLabel = MakeLabel("驱动进程（可选）", 9F, false);
            processLabel.Margin = new Padding(0, 5, 0, 0);
            grid.Controls.Add(processLabel, 2, 0);
            driverProcess = MakeTextBox();
            grid.Controls.Add(driverProcess, 3, 0);
            driverConfirmed = new CheckBox { Text = "我已在原厂驱动中设定这个“切换屏幕”快捷键，并手动验证有效", Dock = DockStyle.Fill, AutoSize = false, Margin = Padding.Empty };
            grid.Controls.Add(driverConfirmed, 0, 1);
            grid.SetColumnSpan(driverConfirmed, 4);
            Label note = MakeLabel("仅转发快捷键；无法读取原厂映射结果。进程名填写程序名即可，不含 .exe。", 8F, false);
            note.ForeColor = Muted;
            note.Dock = DockStyle.Fill;
            note.AutoSize = false;
            grid.Controls.Add(note, 0, 2);
            grid.SetColumnSpan(note, 4);
            panel.Controls.Add(grid);
            return panel;
        }

        private Panel BuildOtdFields()
        {
            Panel panel = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            TableLayoutPanel grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3, Margin = Padding.Empty };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 86));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Label pathLabel = MakeLabel("OTD 控制台程序", 9F, false);
            pathLabel.Margin = new Padding(0, 5, 0, 0);
            grid.Controls.Add(pathLabel, 0, 0);
            otdPath = MakeTextBox();
            otdPath.TextChanged += delegate { ClearVerifiedTarget(); };
            grid.Controls.Add(otdPath, 1, 0);
            browseButton = MakeButton("浏览…", 78, false);
            browseButton.Height = 26;
            browseButton.Margin = Padding.Empty;
            browseButton.Click += delegate
            {
                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.Title = "选择 OpenTabletDriver.Console.exe";
                    dialog.Filter = "OpenTabletDriver 控制台|OpenTabletDriver.Console.exe|可执行程序|*.exe";
                    dialog.CheckFileExists = true;
                    if (dialog.ShowDialog(this) == DialogResult.OK) otdPath.Text = dialog.FileName;
                }
            };
            grid.Controls.Add(browseButton, 2, 0);
            Label nameLabel = MakeLabel("数位板完整名称", 9F, false);
            nameLabel.Margin = new Padding(0, 5, 0, 0);
            grid.Controls.Add(nameLabel, 0, 1);
            tabletName = MakeTextBox();
            tabletName.TextChanged += delegate { ClearVerifiedTarget(); };
            grid.Controls.Add(tabletName, 1, 1);
            grid.SetColumnSpan(tabletName, 2);
            Label note = MakeLabel("名称须与 OTD 检测结果一致。Photoshop 笔压需要 Windows Ink；请先按使用说明配置 OTD。", 8F, false);
            note.Dock = DockStyle.Fill;
            note.AutoSize = false;
            note.ForeColor = Muted;
            grid.Controls.Add(note, 0, 2);
            grid.SetColumnSpan(note, 3);
            panel.Controls.Add(grid);
            return panel;
        }

        private void LoadFields()
        {
            driverHotkey.Text = String.IsNullOrWhiteSpace(settings.DriverHotkey) ? "Ctrl+Alt+Shift+F12" : settings.DriverHotkey;
            driverProcess.Text = settings.DriverProcessName ?? "";
            driverConfirmed.Checked = settings.DriverShortcutConfirmed;
            otdPath.Text = settings.OtdCliPath ?? "";
            tabletName.Text = settings.TabletName ?? "";
            backend.SelectedIndex = String.Equals(settings.Backend, "otd", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            UpdateBackendView();
        }

        private bool SaveFields(bool announce)
        {
            if (demo) return true;
            try
            {
                settings.Backend = backend.SelectedIndex == 1 ? "otd" : "driver";
                settings.DriverHotkey = driverHotkey.Text.Trim();
                settings.DriverProcessName = driverProcess.Text.Trim();
                settings.DriverShortcutConfirmed = driverConfirmed.Checked;
                settings.OtdCliPath = otdPath.Text.Trim().Trim('"');
                settings.TabletName = tabletName.Text.Trim();
                SettingsStore.Save(settings);
                if (announce) SetStatus("设置已保存。全局快捷键和托盘操作现在使用这组设置。", false);
                return true;
            }
            catch (Exception ex)
            {
                SetStatus("无法保存设置：" + ex.Message, true);
                return false;
            }
        }

        private void UpdateBackendView()
        {
            if (driverPanel == null || otdPanel == null) return;
            bool isOtd = backend.SelectedIndex == 1;
            backendValue.Text = isOtd ? "当前：OpenTabletDriver" : "当前：原厂驱动";
            ClearVerifiedTarget();
            driverPanel.Visible = !isOtd;
            otdPanel.Visible = isOtd;
            backendHint.Text = isOtd
                ? "直接修改 OTD 的显示区域；切换后回读配置核验，支持负坐标和竖屏。"
                : "适用于提供“切换屏幕”键盘快捷键的品牌驱动；需要先在驱动内完成配置。";
            screenSectionTitle.Text = isOtd ? "屏幕布局 · 点选目标屏幕" : "屏幕布局 · 使用下方按钮轮换";
            if (demo) screenSectionTitle.Text = "屏幕布局 · 点选屏幕，切换模拟笔";
            UpdateEnabledState();
        }

        private void UpdateEnabledState()
        {
            bool isOtd = backend != null && backend.SelectedIndex == 1;
            if (backend != null) backend.Enabled = !busy && !demo;
            if (driverPanel != null) driverPanel.Enabled = !busy && !demo;
            if (otdPanel != null) otdPanel.Enabled = !busy && !demo;
            if (cycleButton != null) cycleButton.Enabled = !busy && displays.Count > 0;
            if (undoButton != null) undoButton.Enabled = !busy && isOtd;
            if (refreshButton != null) refreshButton.Enabled = !busy;
            if (saveButton != null) saveButton.Enabled = !busy;
            if (probeButton != null) probeButton.Enabled = !busy;
            foreach (Button button in selectButtons) button.Enabled = !busy && isOtd;
            if (monitorMap != null) monitorMap.CanSelectTarget = !busy && isOtd;
            UseWaitCursor = busy;
        }

        private void RefreshDisplays(bool announce)
        {
            try
            {
                string currentDemoTarget = demo ? monitorMap.VerifiedTargetId : null;
                displays = demo ? DemoEnvironment.GetDisplays() : DisplayCatalog.Get();
                displayCount.Text = displays.Count + (demo ? " 块模拟屏幕" : " 块活动屏幕");
                monitorMap.SetTargets(displays);
                if (demo) monitorMap.VerifiedTargetId = currentDemoTarget;
                else if (mappingSummary != null) { mappingSummary.Text = "尚未核验映射"; mappingSummary.ForeColor = Muted; }
                displayList.SuspendLayout();
                while (displayList.Controls.Count > 0)
                {
                    Control old = displayList.Controls[0];
                    displayList.Controls.RemoveAt(0);
                    old.Dispose();
                }
                selectButtons.Clear();
                displayCards.Clear();
                for (int index = 0; index < displays.Count; index++)
                {
                    DisplayTarget target = displays[index];
                    int number = index + 1;
                    Panel card = new Panel { Size = new Size(150, 35), BackColor = Color.White, Margin = new Padding(0, 0, 8, 0), Tag = target.Id };
                    Button select = MakeButton("屏幕 " + number + (target.Primary ? " · 主屏" : target.Bounds.Height > target.Bounds.Width ? " · 竖屏" : ""), 150, false);
                    select.Height = 34;
                    select.Location = Point.Empty;
                    select.AccessibleName = "映射到屏幕 " + number + "，" + target.Label + "，" + target.Bounds.Width + " × " + target.Bounds.Height;
                    select.AccessibleDescription = "桌面位置 " + target.Bounds.X + "，" + target.Bounds.Y + (target.Primary ? "，主屏幕" : "");
                    select.Tag = target;
                    RoundedButton styledSelect = select as RoundedButton;
                    if (styledSelect != null) styledSelect.Selected = target.Id == monitorMap.VerifiedTargetId;
                    select.Click += async delegate(object sender, EventArgs args) { await RunOperation("select", (DisplayTarget)((Button)sender).Tag, true); };
                    card.Controls.Add(select);
                    if (layoutInitialized) card.Scale(new SizeF(CurrentAutoScaleDimensions.Width / 96F, CurrentAutoScaleDimensions.Height / 96F));
                    displayList.Controls.Add(card);
                    displayCards.Add(card);
                    selectButtons.Add(select);
                }
                if (displays.Count == 0)
                {
                    Label empty = MakeLabel("未发现活动屏幕。请检查 Windows 显示设置后刷新。", 10F, false);
                    empty.Margin = new Padding(10, 5, 0, 0);
                    displayList.Controls.Add(empty);
                }
                displayList.ResumeLayout();
                UpdateEnabledState();
                if (announce) SetStatus(demo ? "已重新显示 3 块模拟屏幕。当前模拟映射保持不变。" : "屏幕列表已刷新，共 " + displays.Count + " 块活动屏幕。没有自动改变数位板映射。", false);
            }
            catch (Exception ex)
            {
                displayList.ResumeLayout();
                SetStatus("读取屏幕信息失败：" + ex.Message, true);
            }
        }

        private async Task RunOperation(string action, DisplayTarget target, bool saveCurrentFields)
        {
            if (busy || exiting || IsDisposed) return;
            if (demo)
            {
                settings.Backend = "otd";
                settings.TabletName = DemoEnvironment.TabletName;
                settings.OtdCliPath = "DEMO";
            }
            if (demo && saveCurrentFields) StopDemoPlayback();
            if ((DateTime.UtcNow - lastOperation).TotalMilliseconds < 450) return;
            if (saveCurrentFields && !SaveFields(false)) return;
            if ((action == "select" || action == "undo") && !String.Equals(settings.Backend, "otd", StringComparison.OrdinalIgnoreCase))
            {
                SetStatus("原厂驱动模式只支持轮换屏幕。要直接指定屏幕或撤销映射，请配置 OpenTabletDriver。", true);
                return;
            }
            busy = true;
            lastOperation = DateTime.UtcNow;
            UpdateEnabledState();
            SetStatus(action == "probe" ? "正在检测连接…" : action == "undo" ? "正在恢复上次映射…" : "正在切换，请稍候…", false);
            try
            {
                SwitchResult result;
                if (action == "probe") result = await service.ProbeAsync();
                else if (action == "undo") result = await service.RestoreAsync();
                else if (action == "select") result = await service.SelectAsync(target);
                else result = await service.CycleAsync(new List<DisplayTarget>(displays));
                if (IsDisposed || exiting) return;
                if (demo && result.Success && action == "undo") result.TargetId = (await service.ProbeAsync()).TargetId;
                string prefix = result.Success ? (result.Verified ? "配置已核验 · " : "操作已完成 · ") : "未完成 · ";
                if (demo && result.Success)
                {
                    int selected = displays.FindIndex(d => d.Id == result.TargetId);
                    string screenName = selected < 0 ? "自定义区域" : "屏幕 " + (selected + 1) + (displays[selected].Primary ? " · 主屏" : selected == 2 ? " · 竖屏" : " · 左侧横屏");
                    demoMapping.Text = "演示数位板 → " + screenName;
                    SetStatus((action == "undo" ? "模拟映射已撤销 → " : "模拟映射已就绪 → ") + screenName + "。\r\n在模拟数位板中移动鼠标，橙色笔点会出现在对应屏幕的相同相对位置。", false);
                }
                else SetStatus((demo ? "演示 · " : "") + prefix + result.Message, !result.Success);
                {
                    monitorMap.VerifiedTargetId = result.Success && result.Verified ? result.TargetId : null;
                    int selectedIndex = displays.FindIndex(d => d.Id == monitorMap.VerifiedTargetId);
                    mappingSummary.Text = selectedIndex >= 0 ? (demo ? "模拟映射" : "配置已核验") + "  →  屏幕 " + (selectedIndex + 1) : demo ? "等待模拟映射" : "尚未核验映射";
                    mappingSummary.ForeColor = selectedIndex >= 0 ? Accent : Muted;
                    foreach (Button button in selectButtons)
                    {
                        RoundedButton styled = button as RoundedButton;
                        if (styled != null)
                        {
                            styled.Selected = selectedIndex >= 0 && ((DisplayTarget)button.Tag).Id == monitorMap.VerifiedTargetId;
                            styled.Invalidate();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (!IsDisposed && !exiting) { ClearVerifiedTarget(); SetStatus("操作失败：" + ex.Message, true); }
            }
            finally
            {
                busy = false;
                if (!IsDisposed && !exiting) UpdateEnabledState();
            }
        }

        private void SetStatus(string message, bool error)
        {
            if (statusText == null || IsDisposed || exiting) return;
            statusText.ForeColor = error ? Color.FromArgb(159, 52, 40) : Ink;
            statusText.Text = DateTime.Now.ToString("HH:mm:ss") + "  " + message;
            statusText.SelectionStart = 0;
            statusText.SelectionLength = 0;
            if (error && tray != null && !Visible)
                tray.ShowBalloonTip(4000, "数位板屏幕切换器", message, ToolTipIcon.Warning);
        }

        private void ClearVerifiedTarget()
        {
            if (monitorMap != null) monitorMap.VerifiedTargetId = null;
            foreach (Button button in selectButtons)
            {
                RoundedButton styled = button as RoundedButton;
                if (styled != null) { styled.Selected = false; styled.Invalidate(); }
            }
            if (mappingSummary != null) { mappingSummary.Text = demo ? "等待模拟映射" : "尚未核验映射"; mappingSummary.ForeColor = Muted; }
        }

        private void BuildTray()
        {
            trayMenu = new ContextMenuStrip();
            ToolStripMenuItem show = new ToolStripMenuItem("打开切换器  Ctrl+Alt+F10");
            show.Click += delegate { ShowWindow(); };
            ToolStripMenuItem cycle = new ToolStripMenuItem("切换到下一屏  Ctrl+Alt+F9");
            cycle.Click += async delegate { await RunOperation("cycle", null, false); };
            ToolStripMenuItem quit = new ToolStripMenuItem("退出切换器");
            quit.Click += delegate { ExitApplication(); };
            trayMenu.Items.Add(show);
            trayMenu.Items.Add(cycle);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(quit);
            tray = new NotifyIcon { Text = "PenBridge · 双击打开", Icon = Icon, ContextMenuStrip = trayMenu, Visible = true };
            tray.DoubleClick += delegate { ShowWindow(); };
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Rectangle workArea = Screen.FromControl(this).WorkingArea;
            if (Width > workArea.Width || Height > workArea.Height)
            {
                int availableWidth = Math.Max(320, workArea.Width - 24);
                int availableHeight = Math.Max(300, workArea.Height - 24);
                MinimumSize = new Size(Math.Min(MinimumSize.Width, availableWidth), Math.Min(MinimumSize.Height, availableHeight));
                Size = new Size(Math.Min(Width, availableWidth), Math.Min(Height, availableHeight));
                Location = new Point(workArea.X + (workArea.Width - Width) / 2, workArea.Y + (workArea.Height - Height) / 2);
            }
            if (shownOnce) return;
            shownOnce = true;
            if (demo) await RunOperation("probe", null, false);
            else RegisterShortcuts();
        }

        private void RegisterShortcuts()
        {
            List<string> failed = new List<string>();
            TryRegister(HotkeyCycle, (uint)Keys.F9, "F9", failed);
            TryRegister(HotkeyShow, (uint)Keys.F10, "F10", failed);
            if (failed.Count > 0)
            {
                hotkeyStatus.Text = "快捷键冲突：Ctrl+Alt+" + String.Join(" / ", failed.ToArray()) + "；可用窗口按钮";
                hotkeyStatus.ForeColor = Color.FromArgb(159, 52, 40);
                SetStatus("部分全局快捷键注册失败，可能已被其他程序占用：Ctrl+Alt+" + String.Join("、Ctrl+Alt+", failed.ToArray()) + "。窗口按钮仍可使用。", true);
            }
        }

        private void TryRegister(int id, uint key, string label, List<string> failed)
        {
            if (RegisterHotKey(Handle, id, 0x0001 | 0x0002 | 0x4000, key)) registeredHotkeys.Add(id);
            else failed.Add(label);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmHotkey)
            {
                HandleShortcut(m.WParam.ToInt32());
                return;
            }
            base.WndProc(ref m);
        }

        private async void HandleShortcut(int id)
        {
            if (demo) return;
            if (id == HotkeyShow) { ShowWindow(); return; }
            if (id == HotkeyCycle) { await RunOperation("cycle", null, false); return; }
        }

        private void ShowWindow()
        {
            if (IsDisposed || exiting) return;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
        }

        private void DisplaySettingsChanged(object sender, EventArgs e)
        {
            if (IsDisposed || exiting || !IsHandleCreated) return;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (IsDisposed || exiting) return;
                    RefreshDisplays(false);
                    SetStatus("屏幕布局已变化，列表已刷新；未自动改变映射。OTD 模式请先核对 OTD 屏幕列表并点击“检测连接”，再切换。", true);
                });
            }
            catch (InvalidOperationException) { }
        }

        private void OpenHelp()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "README.md");
                if (!File.Exists(path)) path = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "README.md"));
                if (!File.Exists(path))
                {
                    using (Stream resource = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("PenBridge.Readme"))
                    {
                        if (resource == null) throw new FileNotFoundException("找不到程序内置的使用说明。");
                        string helpDirectory = Path.Combine(SettingsStore.DirectoryPath, "help");
                        Directory.CreateDirectory(helpDirectory);
                        path = Path.Combine(helpDirectory, "README.md");
                        using (FileStream output = new FileStream(path, FileMode.Create, FileAccess.Write)) resource.CopyTo(output);
                    }
                }
                try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
                catch (System.ComponentModel.Win32Exception) { Process.Start("notepad.exe", "\"" + path + "\""); }
            }
            catch (Exception ex) { SetStatus("无法打开使用说明：" + ex.Message, true); }
        }

        private void OpenLogs()
        {
            try
            {
                Directory.CreateDirectory(SettingsStore.DirectoryPath);
                Process.Start(new ProcessStartInfo(SettingsStore.DirectoryPath) { UseShellExecute = true });
            }
            catch (Exception ex) { SetStatus("无法打开设置目录：" + ex.Message, true); }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!demo && !exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                if (!trayTipShown && tray != null)
                {
                    trayTipShown = true;
                    tray.ShowBalloonTip(3000, "切换器仍在运行", "使用 Ctrl+Alt+F9 切换，Ctrl+Alt+F10 打开。右键托盘图标可退出。", ToolTipIcon.Info);
                }
            }
            base.OnFormClosing(e);
        }

        public void ExitApplication()
        {
            exiting = true;
            Close();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            foreach (int id in registeredHotkeys) UnregisterHotKey(Handle, id);
            registeredHotkeys.Clear();
            base.OnHandleDestroyed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !cleanedUp)
            {
                cleanedUp = true;
                if (demo) StopDemoPlayback();
                if (eventsAttached) SystemEvents.DisplaySettingsChanged -= DisplaySettingsChanged;
                if (tray != null) { tray.Visible = false; tray.Dispose(); }
                if (trayMenu != null) trayMenu.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class MonitorLayoutView : Control
    {
        private readonly List<DisplayTarget> targets = new List<DisplayTarget>();
        private readonly List<RectangleF> hitAreas = new List<RectangleF>();
        private readonly ToolTip tip = new ToolTip();
        private bool canSelect;
        private string verifiedTarget;
        private int hoverIndex = -1;
        private PointF demoPenPosition = new PointF(0.5F, 0.5F);
        public bool DemoMode { get; set; }
        public PointF DemoPenPosition
        {
            get { return demoPenPosition; }
            set { demoPenPosition = value; Invalidate(); }
        }
        public event Action<DisplayTarget> TargetClicked;

        public MonitorLayoutView()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AccessibleName = "按桌面位置绘制的屏幕布局";
            AccessibleDescription = "可在布局中点击屏幕；也可使用下方屏幕按钮进行键盘操作。";
        }

        public bool CanSelectTarget
        {
            get { return canSelect; }
            set { canSelect = value; Cursor = value && hoverIndex >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

        public string VerifiedTargetId
        {
            get { return verifiedTarget; }
            set { verifiedTarget = value; Invalidate(); }
        }

        public void SetTargets(List<DisplayTarget> values)
        {
            targets.Clear();
            targets.AddRange(values);
            verifiedTarget = null;
            hoverIndex = -1;
            UpdateHitAreas();
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateHitAreas();
        }

        private void UpdateHitAreas()
        {
            hitAreas.Clear();
            if (targets.Count == 0) return;
            Rectangle bounds = targets[0].Bounds;
            for (int index = 1; index < targets.Count; index++) bounds = Rectangle.Union(bounds, targets[index].Bounds);
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            float padding = Math.Max(12F, Font.Height * 0.9F);
            float scale = Math.Min(Math.Max(1F, Width - padding * 2F) / bounds.Width, Math.Max(1F, Height - padding * 2F) / bounds.Height);
            float originX = (Width - bounds.Width * scale) / 2F;
            float originY = (Height - bounds.Height * scale) / 2F;
            foreach (DisplayTarget target in targets)
            {
                Rectangle screen = target.Bounds;
                hitAreas.Add(new RectangleF(originX + (screen.X - bounds.X) * scale + 2F, originY + (screen.Y - bounds.Y) * scale + 2F,
                    Math.Max(4F, screen.Width * scale - 4F), Math.Max(4F, screen.Height * scale - 4F)));
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float dpi = g.DpiX / 96F;
            using (SolidBrush canvas = new SolidBrush(Color.FromArgb(250, 250, 252)))
                Palette.FillRound(g, canvas, new RectangleF(0, 0, Width - 1, Height - 1), 12 * dpi);
            using (SolidBrush dot = new SolidBrush(Color.FromArgb(228, 230, 237)))
                for (float x = 17 * dpi; x < Width - 12 * dpi; x += 20 * dpi)
                    for (float y = 15 * dpi; y < Height - 8 * dpi; y += 20 * dpi)
                        g.FillEllipse(dot, x, y, dpi, dpi);
            if (targets.Count == 0)
            {
                TextRenderer.DrawText(g, "等待屏幕信息", Font, ClientRectangle, Palette.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            using (Font numberFont = new Font(Font.FontFamily, 14F, FontStyle.Bold))
            using (Font smallFont = new Font(Font.FontFamily, 8F, FontStyle.Regular))
            {
                for (int index = 0; index < hitAreas.Count; index++)
                {
                    RectangleF rectangle = hitAreas[index];
                    DisplayTarget target = targets[index];
                    bool confirmed = !String.IsNullOrEmpty(verifiedTarget) && String.Equals(verifiedTarget, target.Id, StringComparison.Ordinal);
                    bool hover = canSelect && hoverIndex == index;
                    float radius = 9 * dpi;
                    using (SolidBrush shadow = new SolidBrush(Color.FromArgb(15, 35, 45, 70)))
                        Palette.FillRound(g, shadow, new RectangleF(rectangle.X + dpi, rectangle.Y + 4 * dpi, rectangle.Width, rectangle.Height), radius);
                    using (GraphicsPath path = Palette.Rounded(rectangle, radius))
                    using (LinearGradientBrush gradient = new LinearGradientBrush(rectangle, confirmed ? Color.FromArgb(89, 172, 255) : hover ? Color.FromArgb(238, 247, 255) : Color.White, confirmed ? Color.FromArgb(0, 98, 239) : hover ? Color.FromArgb(218, 236, 255) : Color.FromArgb(232, 237, 244), 65F))
                    {
                        g.FillPath(gradient, path);
                        if (confirmed)
                        {
                            GraphicsState state = g.Save();
                            g.SetClip(path);
                            using (Pen arc = new Pen(Color.FromArgb(24, 255, 255, 255), 18 * dpi))
                            {
                                g.DrawEllipse(arc, rectangle.X - rectangle.Width * 0.3F, rectangle.Y + rectangle.Height * 0.15F, rectangle.Width * 1.5F, rectangle.Height * 1.8F);
                                g.DrawEllipse(arc, rectangle.X - rectangle.Width * 0.5F, rectangle.Y - rectangle.Height * 0.9F, rectangle.Width * 1.6F, rectangle.Height * 1.6F);
                            }
                            g.Restore(state);
                        }
                        using (Pen line = new Pen(confirmed ? Color.FromArgb(0, 107, 239) : hover ? Palette.Blue : Color.FromArgb(205, 212, 222), confirmed || hover ? 1.7F * dpi : dpi))
                            g.DrawPath(line, path);
                    }
                    Color ink = confirmed ? Color.White : Palette.Ink;
                    Color detailInk = confirmed ? Color.FromArgb(227, 240, 255) : Color.FromArgb(111, 122, 140);
                    Rectangle area = Rectangle.Round(rectangle);
                    string number = "屏幕 " + (index + 1);
                    if (rectangle.Height > Font.Height * 4.5F && rectangle.Width > Font.Height * 4.2F)
                    {
                        int center = area.Y + area.Height / 2;
                        int lineHeight = (int)(22 * dpi);
                        TextRenderer.DrawText(g, number, numberFont, new Rectangle(area.X + 4, center - (int)(31 * dpi), area.Width - 8, (int)(30 * dpi)), ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                        TextRenderer.DrawText(g, target.Bounds.Width + " × " + target.Bounds.Height, smallFont, new Rectangle(area.X + 4, center, area.Width - 8, lineHeight), detailInk, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                        string detail = confirmed ? (DemoMode ? "● 模拟映射" : "● 配置已核验") : target.Primary ? "主屏幕" : target.Bounds.Height > target.Bounds.Width ? "竖向显示" : "扩展显示";
                        TextRenderer.DrawText(g, detail, smallFont, new Rectangle(area.X + 4, center + lineHeight, area.Width - 8, lineHeight), detailInk, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                    }
                    else TextRenderer.DrawText(g, (index + 1).ToString(), numberFont, area, ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    if (DemoMode && confirmed)
                    {
                        float inset = 9 * dpi;
                        float x = rectangle.X + inset + demoPenPosition.X * Math.Max(0, rectangle.Width - inset * 2);
                        float y = rectangle.Y + inset + demoPenPosition.Y * Math.Max(0, rectangle.Height - inset * 2);
                        using (SolidBrush halo = new SolidBrush(Color.FromArgb(75, 255, 255, 255))) g.FillEllipse(halo, x - 11 * dpi, y - 11 * dpi, 22 * dpi, 22 * dpi);
                        using (SolidBrush halo = new SolidBrush(Color.White)) g.FillEllipse(halo, x - 6 * dpi, y - 6 * dpi, 12 * dpi, 12 * dpi);
                        using (SolidBrush marker = new SolidBrush(Color.FromArgb(255, 159, 10))) g.FillEllipse(marker, x - 4 * dpi, y - 4 * dpi, 8 * dpi, 8 * dpi);
                    }
                }
            }
        }

        private int HitTest(Point point)
        {
            for (int index = hitAreas.Count - 1; index >= 0; index--) if (hitAreas[index].Contains(point)) return index;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = HitTest(e.Location);
            if (hoverIndex == index) return;
            hoverIndex = index;
            Cursor = canSelect && index >= 0 ? Cursors.Hand : Cursors.Default;
            if (index >= 0)
            {
                DisplayTarget target = targets[index];
                tip.SetToolTip(this, "屏幕 " + (index + 1) + " · " + target.Label + "\r\n" + target.Bounds.Width + " × " + target.Bounds.Height + " · 位置 (" + target.Bounds.X + ", " + target.Bounds.Y + ")\r\n" + (canSelect ? "单击将数位板映射到此屏幕" : "原厂驱动模式请使用轮换按钮"));
            }
            else tip.SetToolTip(this, "");
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hoverIndex = -1;
            Cursor = Cursors.Default;
            tip.SetToolTip(this, "");
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int index = HitTest(e.Location);
            if (e.Button == MouseButtons.Left && canSelect && index >= 0 && TargetClicked != null) TargetClicked(targets[index]);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) tip.Dispose();
            base.Dispose(disposing);
        }
    }
}
