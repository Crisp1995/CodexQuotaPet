using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace CodexQuotaPet
{
    internal sealed class MainWindow : Window
    {
        private const int HotkeyId = 0xC0DE;
        private readonly AppSettings _settings;
        private readonly SettingsStore _settingsStore;
        private readonly DashboardService _service;
        private readonly DispatcherTimer _refreshTimer;
        private readonly DispatcherTimer _clockTimer;
        private readonly DispatcherTimer _saveTimer;
        private readonly Forms.NotifyIcon _tray;
        private Drawing.Icon _trayIcon;
        private Border _shell;
        private Grid _root;
        private Grid _bodies;
        private UIElement _header;
        private UIElement _footer;
        private Grid _simpleBody;
        private ScrollViewer _detailBody;
        private QuotaRing _simpleRing;
        private QuotaRing _primaryRing;
        private UsageChart _chart;
        private TextBlock _connectionText;
        private TextBlock _primaryReset;
        private TextBlock _weekTokens;
        private TextBlock _weekTokenCaption;
        private TextBlock _todayTokens;
        private TextBlock _todayRequests;
        private TextBlock _averageTokens;
        private TextBlock _lastRequest;
        private TextBlock _resetCredits;
        private TextBlock _footerStatus;
        private Button _simpleModeButton;
        private Button _detailModeButton;
        private bool _refreshing;
        private bool _allowClose;
        private int? _lastNotifiedRemaining;
        private DashboardSnapshot _current;
        private bool _isDetailed;
        private bool _applyingMode;
        private bool _isLoaded;

        public MainWindow(AppSettings settings, SettingsStore settingsStore)
        {
            _settings = settings;
            _settingsStore = settingsStore;
            _service = new DashboardService(settings);
            _isDetailed = settings.DetailedMode;
            Title = "Codex Quota Pet";
            Icon = LoadWindowIcon();
            Width = _isDetailed ? settings.DetailWidth : settings.SimpleWidth;
            Height = _isDetailed ? settings.DetailHeight : settings.SimpleHeight;
            MinWidth = _isDetailed ? 520 : 72;
            MinHeight = _isDetailed ? 560 : 72;
            MaxWidth = _isDetailed ? 1200 : 220;
            MaxHeight = _isDetailed ? Math.Min(1000, SystemParameters.WorkArea.Height - 8) : 220;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.CanResize;
            // Keep a normal taskbar entry while visible; hiding the window removes it naturally,
            // while the tray icon and Ctrl+Alt+Q remain available for recovery.
            ShowInTaskbar = true;
            Topmost = settings.AlwaysOnTop;
            Content = BuildUi();

            _tray = CreateTrayIcon();
            _refreshTimer = new DispatcherTimer();
            _refreshTimer.Tick += delegate { BeginRefresh(); };
            ApplyRefreshInterval();
            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += delegate { UpdateCountdowns(); };
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
            _saveTimer.Tick += delegate { _saveTimer.Stop(); SaveSettings(); };

            SourceInitialized += OnSourceInitialized;
            Loaded += delegate
            {
                _isLoaded = true;
                ApplyMode(false);
                RestorePosition();
                _refreshTimer.Start();
                _clockTimer.Start();
                BeginRefresh();
            };
            SizeChanged += OnWindowSizeChanged;
            Closing += OnClosing;
            Closed += delegate
            {
                _refreshTimer.Stop();
                _clockTimer.Stop();
                _saveTimer.Stop();
                _tray.Visible = false;
                _tray.Dispose();
                if (_trayIcon != null) _trayIcon.Dispose();
                Win32.UnregisterHotKey(new WindowInteropHelper(this).Handle, HotkeyId);
            };
        }

        private UIElement BuildUi()
        {
            _shell = new Border
            {
                CornerRadius = new CornerRadius(16),
                BorderThickness = new Thickness(1),
                BorderBrush = Brush("#31558E"),
                Background = new LinearGradientBrush(Color("#142542"), Color("#0B1428"), 135),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 4, Opacity = 0.45, Color = Colors.Black }
            };
            _root = new Grid();
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(48) });
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(42) });
            _shell.Child = _root;

            _header = BuildHeader();
            Grid.SetRow(_header, 0);
            _root.Children.Add(_header);

            _bodies = new Grid { Margin = new Thickness(8, 0, 8, 0) };
            _simpleBody = BuildSimpleBody();
            _detailBody = BuildDetailBody();
            _bodies.Children.Add(_simpleBody);
            _bodies.Children.Add(_detailBody);
            Grid.SetRow(_bodies, 1);
            _root.Children.Add(_bodies);

            _footer = BuildFooter();
            Grid.SetRow(_footer, 2);
            _root.Children.Add(_footer);
            return _shell;
        }

        private UIElement BuildHeader()
        {
            Grid grid = new Grid { Margin = new Thickness(16, 4, 12, 0), Background = Brushes.Transparent };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(new TextBlock { Text = "◉", FontSize = 24, Foreground = Brush("#86A8FF"), VerticalAlignment = VerticalAlignment.Center });
            title.Children.Add(new TextBlock { Text = "  Codex 额度喵", FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });
            title.Children.Add(new TextBlock { Text = "  ●", FontSize = 12, Foreground = Brush("#42DB83"), VerticalAlignment = VerticalAlignment.Center });
            _connectionText = new TextBlock { Text = " 等待连接", FontSize = 11, Foreground = Brush("#82A0D5"), VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(_connectionText);
            grid.Children.Add(title);

            StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Button refresh = HeaderButton("↻", "立即刷新");
            refresh.Click += delegate { BeginRefresh(); };
            Button pin = HeaderButton("⌖", "置顶开关");
            pin.Click += delegate { ToggleTopmost(); };
            Button hide = HeaderButton("—", "隐藏到托盘");
            hide.Click += delegate { Hide(); };
            Button close = HeaderButton("×", "隐藏到托盘");
            close.Click += delegate { Hide(); };
            actions.Children.Add(refresh);
            actions.Children.Add(pin);
            actions.Children.Add(hide);
            actions.Children.Add(close);
            Grid.SetColumn(actions, 1);
            grid.Children.Add(actions);
            grid.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.LeftButton == MouseButtonState.Pressed && !(e.OriginalSource is Button))
                {
                    try { DragMove(); SnapAndSavePosition(); } catch { }
                }
            };
            return grid;
        }

        private Grid BuildSimpleBody()
        {
            Grid grid = new Grid { Background = Brushes.Transparent, ToolTip = "拖动移动 · 双击详细模式 · 右键菜单" };
            _simpleRing = NewRing(0);
            _simpleRing.CompactScaling = true;
            _simpleRing.HideLabel = true;
            _simpleRing.Margin = new Thickness(1);
            grid.Children.Add(_simpleRing);
            grid.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ClickCount >= 2) { SwitchMode(true); e.Handled = true; return; }
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    try { DragMove(); SnapAndSavePosition(); } catch { }
                }
            };
            ContextMenu menu = new ContextMenu();
            MenuItem detail = new MenuItem { Header = "打开详细模式" };
            detail.Click += delegate { SwitchMode(true); };
            MenuItem refresh = new MenuItem { Header = "立即刷新" };
            refresh.Click += delegate { BeginRefresh(); };
            MenuItem hide = new MenuItem { Header = "隐藏到托盘" };
            hide.Click += delegate { Hide(); };
            MenuItem exit = new MenuItem { Header = "退出" };
            exit.Click += delegate { ExitApplication(); };
            menu.Items.Add(detail);
            menu.Items.Add(refresh);
            menu.Items.Add(hide);
            menu.Items.Add(new Separator());
            menu.Items.Add(exit);
            grid.ContextMenu = menu;
            return grid;
        }

        private ScrollViewer BuildDetailBody()
        {
            StackPanel stack = new StackPanel();
            Grid quotaGrid = new Grid();
            quotaGrid.ColumnDefinitions.Add(new ColumnDefinition());
            quotaGrid.ColumnDefinitions.Add(new ColumnDefinition());
            quotaGrid.ColumnDefinitions.Add(new ColumnDefinition());
            quotaGrid.Children.Add(BuildQuotaCard(0));
            quotaGrid.Children.Add(BuildWeeklyTokenCard(1));
            Border resetCard = Card(new Thickness(4));
            StackPanel resetStack = new StackPanel { Margin = new Thickness(14, 12, 14, 10) };
            resetStack.Children.Add(Label("可用重置卡", 12));
            _resetCredits = Value("—", 30);
            resetStack.Children.Add(_resetCredits);
            resetStack.Children.Add(new TextBlock { Text = "只显示次数，不会自动使用", Foreground = Brush("#7085AE"), FontSize = 10, Margin = new Thickness(0, 7, 0, 0), TextWrapping = TextWrapping.Wrap });
            resetCard.Child = resetStack;
            Grid.SetColumn(resetCard, 2);
            quotaGrid.Children.Add(resetCard);
            stack.Children.Add(quotaGrid);

            Grid stats = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            for (int i = 0; i < 4; i++) stats.ColumnDefinitions.Add(new ColumnDefinition());
            _todayTokens = AddStat(stats, 0, "今日 Token", "—");
            _todayRequests = AddStat(stats, 1, "今日请求", "—");
            _averageTokens = AddStat(stats, 2, "平均/次", "—");
            _lastRequest = AddStat(stats, 3, "最近请求", "—");
            stack.Children.Add(stats);

            Border chartCard = Card(new Thickness(4, 8, 4, 4));
            Grid chartGrid = new Grid { Margin = new Thickness(14, 10, 14, 8) };
            chartGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            chartGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(150) });
            chartGrid.Children.Add(Label("最近 7 天 Token 使用趋势", 13));
            _chart = new UsageChart { Margin = new Thickness(0, 8, 0, 0) };
            Grid.SetRow(_chart, 1);
            chartGrid.Children.Add(_chart);
            chartCard.Child = chartGrid;
            stack.Children.Add(chartCard);
            stack.Children.Add(BuildSettingsCard());

            ScrollViewer viewer = new ScrollViewer
            {
                Content = stack,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Visibility = Visibility.Collapsed
            };
            viewer.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (e.ClickCount >= 2 && !IsInsideInteractiveControl(e.OriginalSource as DependencyObject))
                {
                    SwitchMode(false);
                    e.Handled = true;
                }
            };
            return viewer;
        }

        private UIElement BuildQuotaCard(int column)
        {
            Border card = Card(new Thickness(4));
            Grid grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(148) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            QuotaRing ring = NewRing(142);
            _primaryRing = ring;
            grid.Children.Add(ring);
            TextBlock reset = new TextBlock { Text = "等待重置时间", HorizontalAlignment = HorizontalAlignment.Center, Foreground = Brush("#7188B3"), FontSize = 10, Margin = new Thickness(2, 0, 2, 8) };
            _primaryReset = reset;
            Grid.SetRow(reset, 1);
            grid.Children.Add(reset);
            card.Child = grid;
            Grid.SetColumn(card, column);
            return card;
        }

        private UIElement BuildWeeklyTokenCard(int column)
        {
            Border card = Card(new Thickness(4));
            StackPanel stack = new StackPanel { Margin = new Thickness(14, 18, 14, 12), VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(Label("本周期 Token", 12));
            _weekTokens = Value("—", 30);
            stack.Children.Add(_weekTokens);
            _weekTokenCaption = new TextBlock { Text = "自上次额度重置", Foreground = Brush("#7085AE"), FontSize = 10, Margin = new Thickness(0, 9, 0, 0), TextWrapping = TextWrapping.Wrap };
            stack.Children.Add(_weekTokenCaption);
            card.Child = stack;
            Grid.SetColumn(card, column);
            return card;
        }

        private UIElement BuildSettingsCard()
        {
            Border card = Card(new Thickness(4, 8, 4, 4));
            StackPanel stack = new StackPanel { Margin = new Thickness(14, 10, 14, 12) };
            stack.Children.Add(Label("设置", 13));

            Button interval = new Button
            {
                Content = IntervalLabel(_settings.RefreshIntervalSeconds) + "  ▾",
                Width = 120,
                Height = 27,
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = Brush("#182947"),
                BorderBrush = Brush("#314A73"),
                Foreground = Brushes.White,
                Cursor = Cursors.Hand
            };
            interval.Click += delegate
            {
                int[] options = { 60, 180, 300, 600 };
                int index = Array.IndexOf(options, _settings.RefreshIntervalSeconds);
                _settings.RefreshIntervalSeconds = options[(index + 1 + options.Length) % options.Length];
                interval.Content = IntervalLabel(_settings.RefreshIntervalSeconds) + "  ▾";
                ApplyRefreshInterval();
                SaveSettings();
            };
            stack.Children.Add(SettingRow("自动刷新频率", interval));

            CheckBox topmost = Toggle(_settings.AlwaysOnTop);
            topmost.Checked += delegate { _settings.AlwaysOnTop = true; Topmost = true; SaveSettings(); };
            topmost.Unchecked += delegate { _settings.AlwaysOnTop = false; Topmost = false; SaveSettings(); };
            stack.Children.Add(SettingRow("始终置顶", topmost));

            CheckBox notify = Toggle(_settings.LowQuotaNotification);
            notify.Checked += delegate { _settings.LowQuotaNotification = true; SaveSettings(); };
            notify.Unchecked += delegate { _settings.LowQuotaNotification = false; SaveSettings(); };
            stack.Children.Add(SettingRow("额度不足提醒", notify));

            CheckBox startup = Toggle(_settings.StartWithWindows);
            startup.Checked += delegate { ChangeStartup(true, startup); };
            startup.Unchecked += delegate { ChangeStartup(false, startup); };
            stack.Children.Add(SettingRow("开机启动", startup));

            TextBlock privacy = new TextBlock
            {
                Text = "隐私：只调用本机 app-server 并读取 Token 数值；不读取 auth.json、Cookie 或会话正文。",
                Foreground = Brush("#7085AE"), FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0)
            };
            stack.Children.Add(privacy);
            card.Child = stack;
            return card;
        }

        private UIElement BuildFooter()
        {
            Grid grid = new Grid { Margin = new Thickness(16, 4, 12, 5) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _footerStatus = new TextBlock { Text = "↻  等待首次刷新", Foreground = Brush("#8298C3"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            grid.Children.Add(_footerStatus);
            StackPanel modes = new StackPanel { Orientation = Orientation.Horizontal };
            _simpleModeButton = ModeButton("☰  简洁模式");
            _detailModeButton = ModeButton("☷  详细模式");
            _simpleModeButton.Click += delegate { SwitchMode(false); };
            _detailModeButton.Click += delegate { SwitchMode(true); };
            modes.Children.Add(_simpleModeButton);
            modes.Children.Add(_detailModeButton);
            Grid.SetColumn(modes, 1);
            grid.Children.Add(modes);
            return grid;
        }

        private void BeginRefresh()
        {
            if (_refreshing) return;
            _refreshing = true;
            _connectionText.Text = " 正在同步";
            _connectionText.Foreground = Brush("#FFB149");
            _footerStatus.Text = "↻  正在读取本机 Codex 数据…";
            Task<DashboardSnapshot>.Factory.StartNew(delegate { return _service.Refresh(); }).ContinueWith(task => Dispatcher.BeginInvoke(new Action(delegate
            {
                _refreshing = false;
                if (task.IsFaulted)
                {
                    _footerStatus.Text = "⚠  刷新失败";
                    _connectionText.Text = " 读取错误";
                    _connectionText.Foreground = Brush("#FF5270");
                    return;
                }
                ApplySnapshot(task.Result);
            })));
        }

        private void ApplySnapshot(DashboardSnapshot data)
        {
            _current = data;
            if (data.Quota != null)
            {
                QuotaWindowData preferred = data.Quota.PreferredWindow;
                ApplyRing(_primaryRing, preferred, "本周期额度");
                ApplyRing(_simpleRing, data.Quota.PreferredWindow, "额度");
                _primaryReset.Text = FormatResetLine(preferred);
                _resetCredits.Text = data.Quota.ResetCreditsAvailable.HasValue ? data.Quota.ResetCreditsAvailable.Value.ToString(CultureInfo.InvariantCulture) + " 张" : "未提供";
                _connectionText.Text = data.IsStale ? " 使用旧数据" : " 已连接";
                _connectionText.Foreground = data.IsStale ? Brush("#FFB149") : Brush("#42DB83");
                MaybeNotifyLowQuota(preferred);
            }
            else
            {
                _connectionText.Text = " 未连接";
                _connectionText.Foreground = Brush("#FF5270");
            }

            _todayTokens.Text = UsageChart.FormatCompact(data.TodayTokens);
            _weekTokens.Text = UsageChart.FormatCompact(data.CurrentCycleTokens);
            _weekTokenCaption.Text = data.CurrentCycleStartedAt.HasValue
                ? "自 " + data.CurrentCycleStartedAt.Value.ToString("M/d HH:mm") + " · 按日估算"
                : "当前额度周期 · 按日估算";
            _todayRequests.Text = data.TodayRequests.ToString(CultureInfo.InvariantCulture);
            _averageTokens.Text = UsageChart.FormatCompact(data.AverageTokensPerRequest);
            _lastRequest.Text = data.LocalUsage != null && data.LocalUsage.LastRequestAt.HasValue ? data.LocalUsage.LastRequestAt.Value.ToString("HH:mm:ss") : "—";
            _chart.Items = data.PreferredDailyUsage;
            _footerStatus.Text = data.ErrorMessage == null
                ? "↻  最后更新：" + data.AttemptedAt.ToString("HH:mm:ss") + " · " + data.UsageSource
                : "⚠  " + data.ErrorMessage;
            UpdateCountdowns();
        }

        private void ApplyRing(QuotaRing ring, QuotaWindowData window, string fallback)
        {
            if (ring == null) return;
            ring.WarningThreshold = _settings.WarningThreshold;
            ring.CriticalThreshold = _settings.CriticalThreshold;
            if (window == null)
            {
                ring.IsAvailable = false;
                ring.Remaining = 0;
                ring.Label = fallback;
                ring.Caption = "当前未提供";
            }
            else
            {
                ring.IsAvailable = true;
                ring.Remaining = window.RemainingPercent;
                ring.Label = fallback;
                ring.Caption = window.ResetsAtLocal.HasValue ? "等待重置" : "无重置时间";
            }
        }

        private void UpdateCountdowns()
        {
            if (_current == null || _current.Quota == null) return;
            QuotaWindowData preferred = _current.Quota.PreferredWindow;
            if (_simpleRing != null && preferred != null) _simpleRing.Caption = FormatCompactCountdown(preferred.ResetsAtLocal);
            if (_primaryRing != null && preferred != null) _primaryRing.Caption = FormatCountdown(preferred.ResetsAtLocal);
        }

        private void ApplyMode(bool save)
        {
            _applyingMode = true;
            _settings.DetailedMode = _isDetailed;
            _simpleBody.Visibility = _isDetailed ? Visibility.Collapsed : Visibility.Visible;
            _detailBody.Visibility = _isDetailed ? Visibility.Visible : Visibility.Collapsed;
            _header.Visibility = _isDetailed ? Visibility.Visible : Visibility.Collapsed;
            _footer.Visibility = _isDetailed ? Visibility.Visible : Visibility.Collapsed;
            _root.RowDefinitions[0].Height = _isDetailed ? new GridLength(48) : new GridLength(0);
            _root.RowDefinitions[2].Height = _isDetailed ? new GridLength(42) : new GridLength(0);
            _bodies.Margin = _isDetailed ? new Thickness(8, 0, 8, 0) : new Thickness(0);
            MinWidth = _isDetailed ? 520 : 72;
            MinHeight = _isDetailed ? 560 : 72;
            MaxWidth = _isDetailed ? 1200 : 220;
            MaxHeight = _isDetailed ? Math.Min(1000, SystemParameters.WorkArea.Height - 8) : 220;
            Width = _isDetailed ? _settings.DetailWidth : _settings.SimpleWidth;
            Height = _isDetailed ? Math.Min(_settings.DetailHeight, MaxHeight) : _settings.SimpleHeight;
            // Keep a recovery entry while the floating ball is visible; hiding the window
            // removes it, while tray and Ctrl+Alt+Q remain available.
            ShowInTaskbar = true;
            UpdateShellShape();
            _simpleModeButton.Background = _isDetailed ? Brushes.Transparent : Brush("#233965");
            _detailModeButton.Background = _isDetailed ? Brush("#233965") : Brushes.Transparent;
            _applyingMode = false;
            if (save) { SnapAndSavePosition(); SaveSettings(); }
        }

        private void SwitchMode(bool detailed)
        {
            if (_isDetailed == detailed) return;
            Rect work = SystemParameters.WorkArea;
            double oldWidth = ActualWidth > 0 ? ActualWidth : Width;
            double rightGap = work.Right - (Left + oldWidth);
            bool keepRightEdge = rightGap >= -2 && rightGap <= 120;
            RememberCurrentSize();
            _isDetailed = detailed;
            ApplyMode(true);
            if (keepRightEdge)
            {
                Left = work.Right - Width - Math.Max(0, rightGap);
                SnapAndSavePosition();
            }
            ShowAndActivate();
        }

        private void RememberCurrentSize()
        {
            if (_isDetailed)
            {
                _settings.DetailWidth = ActualWidth > 0 ? ActualWidth : Width;
                _settings.DetailHeight = ActualHeight > 0 ? ActualHeight : Height;
            }
            else
            {
                _settings.SimpleWidth = ActualWidth > 0 ? ActualWidth : Width;
                _settings.SimpleHeight = ActualHeight > 0 ? ActualHeight : Height;
            }
        }

        private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateShellShape();
            if (!_isLoaded || _applyingMode) return;
            RememberCurrentSize();
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void UpdateShellShape()
        {
            if (_shell == null) return;
            _shell.CornerRadius = _isDetailed ? new CornerRadius(16) : new CornerRadius(Math.Max(28, Math.Min(ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height) / 2));
            _shell.BorderBrush = _isDetailed ? Brush("#31558E") : Brush("#496ED0");
        }

        private void RestorePosition()
        {
            Rect work = SystemParameters.WorkArea;
            Left = _settings.WindowLeft.HasValue ? _settings.WindowLeft.Value : work.Right - Width - 18;
            Top = _settings.WindowTop.HasValue ? _settings.WindowTop.Value : work.Top + 18;
            Left = Math.Max(work.Left, Math.Min(work.Right - Width, Left));
            Top = Math.Max(work.Top, Math.Min(work.Bottom - Height, Top));
        }

        private void SnapAndSavePosition()
        {
            Rect work = SystemParameters.WorkArea;
            if (_settings.SnapToScreen)
            {
                if (Math.Abs(Left - work.Left) < 20) Left = work.Left;
                if (Math.Abs((Left + Width) - work.Right) < 20) Left = work.Right - Width;
                if (Math.Abs(Top - work.Top) < 20) Top = work.Top;
                if (Math.Abs((Top + Height) - work.Bottom) < 20) Top = work.Bottom - Height;
            }
            Left = Math.Max(work.Left, Math.Min(work.Right - Width, Left));
            Top = Math.Max(work.Top, Math.Min(work.Bottom - Height, Top));
            _settings.WindowLeft = Left;
            _settings.WindowTop = Top;
            SaveSettings();
        }

        private Forms.NotifyIcon CreateTrayIcon()
        {
            _trayIcon = LoadTrayIcon();
            Forms.NotifyIcon tray = new Forms.NotifyIcon
            {
                Text = "Codex 额度喵",
                Icon = _trayIcon,
                Visible = true
            };
            Forms.ContextMenuStrip menu = new Forms.ContextMenuStrip();
            menu.Items.Add("显示 / 隐藏", null, delegate { Dispatcher.BeginInvoke(new Action(ToggleVisibility)); });
            menu.Items.Add("立即刷新", null, delegate { Dispatcher.BeginInvoke(new Action(BeginRefresh)); });
            menu.Items.Add("简洁 / 详细模式", null, delegate { Dispatcher.BeginInvoke(new Action(delegate { SwitchMode(!_isDetailed); })); });
            menu.Items.Add("始终置顶", null, delegate { Dispatcher.BeginInvoke(new Action(ToggleTopmost)); });
            menu.Items.Add(new Forms.ToolStripSeparator());
            menu.Items.Add("退出", null, delegate { Dispatcher.BeginInvoke(new Action(ExitApplication)); });
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { Dispatcher.BeginInvoke(new Action(ShowAndActivate)); };
            return tray;
        }

        private static Drawing.Icon LoadTrayIcon()
        {
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CodexQuotaPet.TrayIcon.png"))
                {
                    if (stream == null) throw new InvalidOperationException("tray icon resource missing");
                    using (Drawing.Bitmap source = new Drawing.Bitmap(stream))
                    using (Drawing.Bitmap scaled = new Drawing.Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                    using (Drawing.Graphics graphics = Drawing.Graphics.FromImage(scaled))
                    {
                        graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                        graphics.DrawImage(source, new Drawing.Rectangle(0, 0, 32, 32));
                        IntPtr handle = scaled.GetHicon();
                        try { return (Drawing.Icon)Drawing.Icon.FromHandle(handle).Clone(); }
                        finally { Win32.DestroyIcon(handle); }
                    }
                }
            }
            catch
            {
                return (Drawing.Icon)Drawing.SystemIcons.Information.Clone();
            }
        }

        private static ImageSource LoadWindowIcon()
        {
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("CodexQuotaPet.TrayIcon.png"))
                {
                    if (stream == null) return null;
                    BitmapFrame frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    frame.Freeze();
                    return frame;
                }
            }
            catch { return null; }
        }

        private static bool IsInsideInteractiveControl(DependencyObject source)
        {
            DependencyObject current = source;
            while (current != null)
            {
                if (current is Button || current is CheckBox || current is ComboBox || current is System.Windows.Controls.Primitives.ScrollBar)
                    return true;
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }

        public void ShowAndActivate()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        public void ExitApplication()
        {
            _allowClose = true;
            Close();
            Application.Current.Shutdown();
        }

        private void ToggleVisibility()
        {
            if (IsVisible) Hide(); else ShowAndActivate();
        }

        private void ToggleTopmost()
        {
            _settings.AlwaysOnTop = !_settings.AlwaysOnTop;
            Topmost = _settings.AlwaysOnTop;
            SaveSettings();
        }

        private void ChangeStartup(bool enabled, CheckBox control)
        {
            try
            {
                _settingsStore.ApplyStartupSetting(enabled, System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
                _settings.StartWithWindows = enabled;
                SaveSettings();
            }
            catch
            {
                control.IsChecked = !enabled;
                _tray.ShowBalloonTip(3500, "Codex 额度喵", "无法修改开机启动设置。", Forms.ToolTipIcon.Warning);
            }
        }

        private void MaybeNotifyLowQuota(QuotaWindowData window)
        {
            if (!_settings.LowQuotaNotification || window == null || window.RemainingPercent > _settings.CriticalThreshold) return;
            if (_lastNotifiedRemaining.HasValue && _lastNotifiedRemaining.Value == window.RemainingPercent) return;
            _lastNotifiedRemaining = window.RemainingPercent;
            _tray.ShowBalloonTip(5000, "Codex 额度不足", window.Label + "仅剩 " + window.RemainingPercent + "% ，" + FormatCountdown(window.ResetsAtLocal) + "。", Forms.ToolTipIcon.Warning);
        }

        private void ApplyRefreshInterval()
        {
            _refreshTimer.Interval = TimeSpan.FromSeconds(_settings.RefreshIntervalSeconds);
        }

        private void SaveSettings()
        {
            try { _settingsStore.Save(_settings); } catch { }
        }

        private void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_allowClose) return;
            e.Cancel = true;
            Hide();
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle).AddHook(WndProc);
            Win32.RegisterHotKey(handle, HotkeyId, 0x0001 | 0x0002, 0x51); // Ctrl+Alt+Q
        }

        private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == 0x0084 && WindowState == WindowState.Normal)
            {
                Win32.RECT rect;
                if (Win32.GetWindowRect(hwnd, out rect))
                {
                    long packed = lParam.ToInt64();
                    int x = unchecked((short)(packed & 0xFFFF));
                    int y = unchecked((short)((packed >> 16) & 0xFFFF));
                    const int edge = 8;
                    bool left = x >= rect.Left && x < rect.Left + edge;
                    bool right = x <= rect.Right && x > rect.Right - edge;
                    bool top = y >= rect.Top && y < rect.Top + edge;
                    bool bottom = y <= rect.Bottom && y > rect.Bottom - edge;
                    int hit = 0;
                    if (left && top) hit = 13;
                    else if (right && top) hit = 14;
                    else if (left && bottom) hit = 16;
                    else if (right && bottom) hit = 17;
                    else if (left) hit = 10;
                    else if (right) hit = 11;
                    else if (top) hit = 12;
                    else if (bottom) hit = 15;
                    if (hit != 0) { handled = true; return new IntPtr(hit); }
                }
            }
            if (message == 0x0312 && wParam.ToInt32() == HotkeyId)
            {
                ToggleVisibility();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private QuotaRing NewRing(double size)
        {
            QuotaRing ring = new QuotaRing { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, WarningThreshold = _settings.WarningThreshold, CriticalThreshold = _settings.CriticalThreshold };
            if (size > 0)
            {
                ring.Width = size;
                ring.Height = size;
                ring.HorizontalAlignment = HorizontalAlignment.Center;
                ring.VerticalAlignment = VerticalAlignment.Center;
            }
            else
            {
                ring.Width = double.NaN;
                ring.Height = double.NaN;
            }
            return ring;
        }

        private static Border Card(Thickness margin)
        {
            return new Border { Margin = margin, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), BorderBrush = Brush("#21385E"), Background = Brush("#0E1A31") };
        }

        private static TextBlock Label(string text, double size)
        {
            return new TextBlock { Text = text, FontSize = size, Foreground = Brush("#AABCDD") };
        }

        private static TextBlock Value(string text, double size)
        {
            return new TextBlock { Text = text, FontSize = size, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Margin = new Thickness(0, 7, 0, 0) };
        }

        private static TextBlock AddStat(Grid parent, int column, string label, string initial)
        {
            Border card = Card(new Thickness(4));
            StackPanel stack = new StackPanel { Margin = new Thickness(12, 10, 8, 10) };
            stack.Children.Add(Label(label, 11));
            TextBlock value = Value(initial, 20);
            stack.Children.Add(value);
            card.Child = stack;
            Grid.SetColumn(card, column);
            parent.Children.Add(card);
            return value;
        }

        private static Grid SettingRow(string label, UIElement control)
        {
            Grid row = new Grid { Margin = new Thickness(0, 7, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = label, Foreground = Brush("#9CB0D3"), VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
            Grid.SetColumn(control, 1);
            row.Children.Add(control);
            return row;
        }

        private static CheckBox Toggle(bool value)
        {
            return new CheckBox { IsChecked = value, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        }

        private static Button HeaderButton(string text, string tooltip)
        {
            Button button = new Button { Content = text, ToolTip = tooltip, Width = 34, Height = 30, FontSize = 17, Foreground = Brush("#A9BCE0"), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
            return button;
        }

        private static Button ModeButton(string text)
        {
            return new Button { Content = text, Height = 30, Padding = new Thickness(12, 2, 12, 2), Margin = new Thickness(2, 0, 0, 0), Foreground = Brush("#9DB5E5"), Background = Brushes.Transparent, BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
        }

        private static string FormatResetLine(QuotaWindowData window)
        {
            if (window == null) return "当前未提供";
            return window.ResetsAtLocal.HasValue ? "重置于 " + window.ResetsAtLocal.Value.ToString("M/d HH:mm") : "未提供重置时间";
        }

        private static string FormatCountdown(DateTime? target)
        {
            if (!target.HasValue) return "重置时间未提供";
            TimeSpan span = target.Value - DateTime.Now;
            if (span.TotalSeconds <= 0) return "等待服务端刷新";
            if (span.TotalDays >= 1) return ((int)span.TotalDays).ToString(CultureInfo.InvariantCulture) + " 天 " + span.Hours + " 小时";
            if (span.TotalHours >= 1) return ((int)span.TotalHours).ToString(CultureInfo.InvariantCulture) + " 小时 " + span.Minutes + " 分 " + span.Seconds + " 秒";
            return span.Minutes + " 分 " + span.Seconds + " 秒";
        }

        private static string FormatCompactCountdown(DateTime? target)
        {
            if (!target.HasValue) return "未提供";
            TimeSpan span = target.Value - DateTime.Now;
            if (span.TotalSeconds <= 0) return "等待刷新";
            if (span.TotalDays >= 1) return ((int)span.TotalDays).ToString(CultureInfo.InvariantCulture) + "天" + span.Hours + "小时";
            if (span.TotalHours >= 1) return ((int)span.TotalHours).ToString(CultureInfo.InvariantCulture) + "小时" + span.Minutes + "分";
            return span.Minutes + "分" + span.Seconds + "秒";
        }

        private static string IntervalLabel(int seconds)
        {
            return seconds < 60 ? seconds + " 秒" : (seconds / 60) + " 分钟";
        }

        private static Brush Brush(string hex)
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }

        private static Color Color(string hex)
        {
            return (Color)ColorConverter.ConvertFromString(hex);
        }

        private static class Win32
        {
            [StructLayout(LayoutKind.Sequential)]
            internal struct RECT
            {
                public int Left;
                public int Top;
                public int Right;
                public int Bottom;
            }

            [DllImport("user32.dll")]
            internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

            [DllImport("user32.dll")]
            internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

            [DllImport("user32.dll")]
            internal static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

            [DllImport("user32.dll")]
            internal static extern bool DestroyIcon(IntPtr handle);
        }
    }
}
