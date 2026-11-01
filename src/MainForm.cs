using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace ZapretOBKHOD
{
    internal sealed class MainForm : Form
    {
        private readonly string[] _args;
        private readonly AppSettings _settings;

        private Engine _engine;
        private System.Windows.Forms.Timer _timer;
        private NotifyIcon _tray;
        private ContextMenuStrip _trayMenu;
        private ToolStripMenuItem _trayToggle;
        private ToolStripMenuItem _trayTest;

        private StatusDot _statusDot;
        private Label _statusTitle;
        private Label _statusSub;
        private Label _engineInfo;
        private RoundedButton _toggleButton;

        private ComboBox _presetCombo;
        private Label _presetDesc;

        private CheckBox _cbAutostart;
        private CheckBox _cbTray;
        private CheckBox _cbQuic;
        private CheckBox _cbExclude;

        private CheckChip _chipYouTube;
        private CheckChip _chipDiscord;
        private CheckChip _chipTelegram;
        private RoundedButton _testButton;

        private RichTextBox _logBox;

        private bool _loading = true;
        private bool _starting;
        private bool _testing;
        private bool _wantRunning;
        private bool _exiting;
        private bool _hidden;
        private string _lastError;
        private int _restartCount;

        public MainForm(string[] args)
        {
            _args = args ?? new string[0];
            _settings = AppSettings.Load();
            Logger.Init();
            Logger.Message += OnLogMessage;

            Text = "ZapretOBKHOD — обход блокировок";
            ClientSize = new Size(940, 760);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = Theme.Background;
            Font = Theme.Font(9.75f, FontStyle.Regular);
            AutoScaleMode = AutoScaleMode.Dpi;
            DoubleBuffered = true;

            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }
            if (Icon == null) Icon = SystemIcons.Application;

            _engine = new Engine();
            _engine.ExitedUnexpected += OnEngineExited;

            BuildUi();
            SyncAutostartState();
            ApplySettingsToUi();
            SetupTray();
            SetupTimer();

            Logger.Write("ZapretOBKHOD 1.0 запущен.");
            Logger.Write("Каталог программы: " + AppPaths.BaseDir);
            Logger.Write("Права администратора: " + (Admin.IsElevated() ? "да" : "нет (нужны для WinDivert)"));
            if (!Admin.IsElevated())
            {
                _lastError = "Нужны права администратора. Закройте программу и запустите её снова от имени администратора.";
            }

            // Убираем «хвосты» прошлого запуска: winws мог остаться после сбоя.
            try { _engine.Stop(); }
            catch { }

            Shown += OnFormShown;
            FormClosing += OnFormClosing;
        }

        private void BuildUi()
        {
            var header = new Panel();
            header.SetBounds(0, 0, 940, 96);
            header.BackColor = Color.White;
            Controls.Add(header);

            var border = new Panel();
            border.SetBounds(0, 95, 940, 1);
            border.BackColor = Theme.Border;
            Controls.Add(border);

            var logo = new PictureBox();
            logo.SetBounds(24, 24, 48, 48);
            logo.SizeMode = PictureBoxSizeMode.Zoom;
            logo.BackColor = Color.Transparent;
            try { logo.Image = Icon.ToBitmap(); }
            catch { }
            header.Controls.Add(logo);

            var title = MakeLabel("ZapretOBKHOD", 88, 18, 480, 34, 18f, FontStyle.Bold, Theme.Text);
            header.Controls.Add(title);

            var subtitle = MakeLabel("Обход DPI-блокировок без VPN и зарубежных серверов",
                88, 51, 560, 24, 9.5f, FontStyle.Regular, Theme.Muted);
            header.Controls.Add(subtitle);

            var local = MakeLabel("●  Работает локально", 676, 34, 192, 24, 9f, FontStyle.Bold, Theme.Green);
            local.TextAlign = ContentAlignment.MiddleRight;
            header.Controls.Add(local);

            var about = new RoundedButton();
            about.SetBounds(886, 32, 30, 30);
            about.Radius = 15;
            about.Text = "?";
            about.Font = Theme.Font(10f, FontStyle.Bold);
            about.FillColor = Color.White;
            about.HoverColor = Color.FromArgb(0xF2, 0xF6, 0xFF);
            about.PressedColor = Color.FromArgb(0xE8, 0xEF, 0xFF);
            about.BorderColor = Theme.Border;
            about.TextColor = Theme.Muted;
            about.Click += ShowAbout;
            header.Controls.Add(about);

            var statusCard = MakeCard(24, 112, 892, 148);
            _statusDot = new StatusDot();
            _statusDot.SetBounds(24, 26, 20, 20);
            statusCard.Controls.Add(_statusDot);

            _statusTitle = MakeLabel("Обход выключен", 56, 16, 570, 34, 17f, FontStyle.Bold, Theme.Text);
            statusCard.Controls.Add(_statusTitle);

            _statusSub = MakeLabel("Трафик идёт напрямую через вашего провайдера.",
                56, 54, 600, 46, 9.5f, FontStyle.Regular, Theme.Muted);
            statusCard.Controls.Add(_statusSub);

            _toggleButton = new RoundedButton();
            _toggleButton.SetBounds(680, 40, 188, 52);
            _toggleButton.Text = "Включить обход";
            _toggleButton.Click += ToggleClick;
            statusCard.Controls.Add(_toggleButton);

            _engineInfo = MakeLabel("Движок: zapret winws + WinDivert",
                600, 102, 268, 20, 8.25f, FontStyle.Regular, Theme.Soft);
            _engineInfo.TextAlign = ContentAlignment.MiddleRight;
            statusCard.Controls.Add(_engineInfo);

            var presetCard = MakeCard(24, 272, 892, 184);
            presetCard.Controls.Add(MakeLabel("Профиль обхода", 24, 14, 400, 24, 11f, FontStyle.Bold, Theme.Text));

            _presetCombo = new ComboBox();
            _presetCombo.SetBounds(24, 44, 530, 30);
            _presetCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            _presetCombo.FlatStyle = FlatStyle.Flat;
            _presetCombo.Font = Theme.Font(10f, FontStyle.Regular);
            _presetCombo.BackColor = Color.White;
            foreach (Preset preset in PresetCatalog.All)
            {
                _presetCombo.Items.Add(preset.Name);
            }
            _presetCombo.SelectedIndexChanged += PresetChanged;
            presetCard.Controls.Add(_presetCombo);

            _presetDesc = MakeLabel("", 24, 82, 530, 64, 9f, FontStyle.Regular, Theme.Muted);
            presetCard.Controls.Add(_presetDesc);

            var listsLink = new LinkLabelFlat();
            listsLink.Text = "Открыть папку lists →";
            listsLink.SetBounds(24, 152, 220, 22);
            listsLink.Click += delegate { OpenFolder(AppPaths.ListsDir); };
            presetCard.Controls.Add(listsLink);

            _cbAutostart = MakeCheck("Запускать при входе в Windows", 590, 42, 280);
            _cbAutostart.CheckedChanged += AutostartChanged;
            presetCard.Controls.Add(_cbAutostart);

            _cbTray = MakeCheck("Сворачивать в трей", 590, 72, 280);
            _cbTray.CheckedChanged += TraySettingChanged;
            presetCard.Controls.Add(_cbTray);

            _cbQuic = MakeCheck("Принудительно TCP (блокировать QUIC)", 590, 102, 280);
            _cbQuic.CheckedChanged += QuicChanged;
            presetCard.Controls.Add(_cbQuic);

            _cbExclude = MakeCheck("Не трогать банки и госуслуги", 590, 132, 280);
            _cbExclude.CheckedChanged += ExcludeChanged;
            presetCard.Controls.Add(_cbExclude);

            var testCard = MakeCard(24, 468, 892, 112);
            testCard.Controls.Add(MakeLabel("Проверка доступа", 24, 12, 220, 24, 11f, FontStyle.Bold, Theme.Text));
            var testHint = MakeLabel("напрямую, без VPN", 168, 14, 220, 22, 8.75f, FontStyle.Regular, Theme.Soft);
            testCard.Controls.Add(testHint);

            _chipYouTube = new CheckChip();
            _chipYouTube.SetBounds(24, 46, 228, 54);
            _chipYouTube.SetState("YouTube", "не проверено", Theme.Soft);
            testCard.Controls.Add(_chipYouTube);

            _chipDiscord = new CheckChip();
            _chipDiscord.SetBounds(266, 46, 228, 54);
            _chipDiscord.SetState("Discord", "не проверено", Theme.Soft);
            testCard.Controls.Add(_chipDiscord);

            _chipTelegram = new CheckChip();
            _chipTelegram.SetBounds(508, 46, 228, 54);
            _chipTelegram.SetState("Telegram", "не проверено", Theme.Soft);
            testCard.Controls.Add(_chipTelegram);

            _testButton = new RoundedButton();
            _testButton.SetBounds(750, 46, 118, 54);
            _testButton.Text = "Проверить";
            _testButton.FillColor = Color.White;
            _testButton.HoverColor = Color.FromArgb(0xF2, 0xF6, 0xFF);
            _testButton.PressedColor = Color.FromArgb(0xE8, 0xEF, 0xFF);
            _testButton.BorderColor = Color.FromArgb(0xD0, 0xD5, 0xDD);
            _testButton.TextColor = Color.FromArgb(0x34, 0x40, 0x54);
            _testButton.Font = Theme.Font(9.5f, FontStyle.Bold);
            _testButton.Click += TestClick;
            testCard.Controls.Add(_testButton);

            var logCard = MakeCard(24, 592, 892, 144);
            logCard.Controls.Add(MakeLabel("Журнал", 24, 12, 200, 24, 11f, FontStyle.Bold, Theme.Text));

            var clearButton = MakeSmallButton("Очистить", 548, 11);
            clearButton.Click += delegate { _logBox.Clear(); };
            logCard.Controls.Add(clearButton);

            var listsButton = MakeSmallButton("Списки", 656, 11);
            listsButton.Click += delegate { OpenFolder(AppPaths.ListsDir); };
            logCard.Controls.Add(listsButton);

            var logsButton = MakeSmallButton("Логи", 764, 11);
            logsButton.Click += delegate { OpenFolder(AppPaths.LogsDir); };
            logCard.Controls.Add(logsButton);

            _logBox = new RichTextBox();
            _logBox.SetBounds(16, 44, 860, 86);
            _logBox.BackColor = Theme.Dark;
            _logBox.ForeColor = Color.FromArgb(0xC7, 0xD2, 0xFE);
            _logBox.BorderStyle = BorderStyle.None;
            _logBox.ReadOnly = true;
            _logBox.Font = new Font("Consolas", 8.5f, FontStyle.Regular, GraphicsUnit.Point);
            _logBox.WordWrap = true;
            _logBox.DetectUrls = false;
            _logBox.ScrollBars = RichTextBoxScrollBars.Vertical;
            logCard.Controls.Add(_logBox);
        }

        private RoundedPanel MakeCard(int x, int y, int w, int h)
        {
            var card = new RoundedPanel();
            card.SetBounds(x, y, w, h);
            card.Radius = 14;
            card.FillColor = Theme.Card;
            card.BorderColor = Theme.Border;
            Controls.Add(card);
            return card;
        }

        private Label MakeLabel(string text, int x, int y, int w, int h, float size, FontStyle style, Color color)
        {
            var label = new Label();
            label.SetBounds(x, y, w, h);
            label.Text = text;
            label.Font = Theme.Font(size, style);
            label.ForeColor = color;
            label.BackColor = Color.Transparent;
            label.AutoSize = false;
            label.TextAlign = ContentAlignment.MiddleLeft;
            return label;
        }

        private CheckBox MakeCheck(string text, int x, int y, int w)
        {
            var check = new CheckBox();
            check.SetBounds(x, y, w, 24);
            check.Text = text;
            check.Font = Theme.Font(9f, FontStyle.Regular);
            check.ForeColor = Theme.Text;
            check.BackColor = Color.Transparent;
            check.FlatStyle = FlatStyle.Flat;
            check.AutoSize = false;
            check.Cursor = Cursors.Hand;
            return check;
        }

        private RoundedButton MakeSmallButton(string text, int x, int y)
        {
            var button = new RoundedButton();
            button.SetBounds(x, y, 100, 28);
            button.Text = text;
            button.Radius = 8;
            button.Font = Theme.Font(8.5f, FontStyle.Regular);
            button.FillColor = Color.FromArgb(0xF9, 0xFA, 0xFB);
            button.HoverColor = Color.FromArgb(0xF2, 0xF6, 0xFF);
            button.PressedColor = Color.FromArgb(0xE8, 0xEF, 0xFF);
            button.BorderColor = Theme.Border;
            button.TextColor = Theme.Muted;
            return button;
        }

        private void SetupTray()
        {
            _trayMenu = new ContextMenuStrip();
            _trayMenu.Font = Theme.Font(9f, FontStyle.Regular);

            var show = new ToolStripMenuItem("Открыть ZapretOBKHOD");
            show.Click += ShowWindow;
            _trayMenu.Items.Add(show);

            _trayToggle = new ToolStripMenuItem("Включить обход");
            _trayToggle.Click += ToggleClick;
            _trayMenu.Items.Add(_trayToggle);

            _trayTest = new ToolStripMenuItem("Проверить доступ");
            _trayTest.Click += delegate
            {
                ShowWindow(null, EventArgs.Empty);
                TestAccess();
            };
            _trayMenu.Items.Add(_trayTest);

            _trayMenu.Items.Add(new ToolStripSeparator());

            var exit = new ToolStripMenuItem("Выход");
            exit.Click += delegate
            {
                _exiting = true;
                Close();
            };
            _trayMenu.Items.Add(exit);

            _tray = new NotifyIcon();
            _tray.Icon = Icon;
            _tray.Text = "ZapretOBKHOD";
            _tray.Visible = true;
            _tray.ContextMenuStrip = _trayMenu;
            _tray.DoubleClick += ShowWindow;
        }

        private void SetupTimer()
        {
            _timer = new System.Windows.Forms.Timer();
            _timer.Interval = 2000;
            _timer.Tick += delegate
            {
                if (_exiting) return;
                UpdateUi();
            };
            _timer.Start();
        }

        private void SyncAutostartState()
        {
            bool actual = AutostartTask.IsEnabled();
            if (actual != _settings.Autostart)
            {
                _settings.Autostart = actual;
                _settings.Save();
            }
        }

        private void ApplySettingsToUi()
        {
            _loading = true;

            int index = 0;
            for (int i = 0; i < PresetCatalog.All.Length; i++)
            {
                if (string.Equals(PresetCatalog.All[i].Id, _settings.PresetId, StringComparison.OrdinalIgnoreCase))
                {
                    index = i;
                    break;
                }
            }
            _presetCombo.SelectedIndex = index;
            _presetDesc.Text = PresetCatalog.All[index].Description;

            _cbAutostart.Checked = _settings.Autostart;
            _cbTray.Checked = _settings.MinimizeToTray;
            _cbQuic.Checked = _settings.BlockQuic;
            _cbExclude.Checked = _settings.ExcludeRu;

            _loading = false;
            UpdateUi();
        }

        private void PresetChanged(object sender, EventArgs e)
        {
            if (_loading) return;
            if (_presetCombo.SelectedIndex < 0) return;

            Preset preset = PresetCatalog.All[_presetCombo.SelectedIndex];
            _settings.PresetId = preset.Id;
            _settings.Save();
            _presetDesc.Text = preset.Description;
            Logger.Write("Выбран профиль: " + preset.Name);
            ApplyLiveChange();
        }

        private void AutostartChanged(object sender, EventArgs e)
        {
            if (_loading) return;

            bool enable = _cbAutostart.Checked;
            string error;
            if (AutostartTask.SetEnabled(enable, out error))
            {
                _settings.Autostart = enable;
                _settings.Save();
                Logger.Write(enable
                    ? "Автозапуск включён (задача планировщика ZapretOBKHOD)."
                    : "Автозапуск выключен.");
            }
            else
            {
                _cbAutostart.Checked = !enable;
                Logger.Write("Не удалось изменить автозапуск: " + error);
                MessageBox.Show(this, error, "Автозапуск", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void TraySettingChanged(object sender, EventArgs e)
        {
            if (_loading) return;
            _settings.MinimizeToTray = _cbTray.Checked;
            _settings.Save();
        }

        private void QuicChanged(object sender, EventArgs e)
        {
            if (_loading) return;
            _settings.BlockQuic = _cbQuic.Checked;
            _settings.Save();
            Logger.Write(_settings.BlockQuic ? "Блокировка QUIC включена." : "Блокировка QUIC выключена.");
            ApplyLiveChange();
        }

        private void ExcludeChanged(object sender, EventArgs e)
        {
            if (_loading) return;
            _settings.ExcludeRu = _cbExclude.Checked;
            _settings.Save();
            Logger.Write(_settings.ExcludeRu
                ? "Банки, госуслуги и российские сервисы исключены из обработки."
                : "Обработка банков и госуслуг включена (не рекомендуется).");
            ApplyLiveChange();
        }

        // Применяет изменённые настройки к уже запущенному движку: winws читает
        // параметры только при старте, поэтому его нужно перезапустить.
        private void ApplyLiveChange()
        {
            if (_starting) return;
            if (_engine == null || !_engine.Running) return;

            // Сначала проверяем, что новый профиль вообще собирается.
            // Если нет — оставляем текущий обход работать и показываем причину.
            string error;
            if (PresetCatalog.Build(PresetCatalog.Get(_settings.PresetId), _settings, out error) == null)
            {
                Logger.Write("Настройки не применены: " + error);
                MessageBox.Show(this, error, "Профиль обхода", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Logger.Write("Настройки изменены — перезапускаем движок.");
            StopEngine();
            StartEngine();
        }

        private void ToggleClick(object sender, EventArgs e)
        {
            if (_starting) return;
            if (_engine.Running)
            {
                StopEngine();
            }
            else
            {
                StartEngine();
            }
        }

        private void StartEngine()
        {
            if (_starting) return;
            if (!Admin.IsElevated())
            {
                MessageBox.Show(this,
                    "Для WinDivert нужны права администратора.\r\nЗакройте программу и запустите её снова от имени администратора.",
                    "Нужны права администратора", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Preset preset = PresetCatalog.Get(_settings.PresetId);
            string error;
            string args = PresetCatalog.Build(preset, _settings, out error);
            if (args == null)
            {
                Logger.Write("Профиль не запущен: " + error);
                MessageBox.Show(this, error, "Профиль обхода", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            bool hidden = _hidden;
            _starting = true;
            _wantRunning = true;
            _lastError = null;
            UpdateUi();

            Logger.Write("Запуск профиля: " + preset.Name);
            Logger.Write("команда> winws.exe " + args);

            ThreadPool.QueueUserWorkItem(delegate
            {
                string startError;
                bool ok = false;
                try { ok = _engine.Start(args, _settings.BlockQuic, out startError); }
                catch (Exception ex) { startError = ex.Message; }

                Ui(delegate
                {
                    _starting = false;

                    if (ok)
                    {
                        _restartCount = 0;
                        _lastError = null;
                        Logger.Write("Обход включён.");
                        if (hidden)
                        {
                            try
                            {
                                _tray.ShowBalloonTip(2500, "ZapretOBKHOD",
                                    "Обход включён. Трафик идёт напрямую, без зарубежных серверов.",
                                    ToolTipIcon.Info);
                            }
                            catch { }
                        }
                    }
                    else
                    {
                        _wantRunning = false;
                        _lastError = startError;
                        Logger.Write("Ошибка запуска: " + startError);
                        if (hidden)
                        {
                            try
                            {
                                _tray.ShowBalloonTip(4000, "ZapretOBKHOD",
                                    "Не удалось включить обход. Откройте программу и посмотрите журнал.",
                                    ToolTipIcon.Warning);
                            }
                            catch { }
                        }
                        else
                        {
                            ShowWindow();
                            MessageBox.Show(this, startError, "Не удалось включить обход",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }

                    UpdateUi();
                });
            });
        }

        private void StopEngine()
        {
            _wantRunning = false;
            _restartCount = 0;
            _engine.Stop();
            _lastError = null;
            Logger.Write("Обход выключен.");
            UpdateUi();
        }

        private void OnEngineExited(object sender, EventArgs e)
        {
            Ui(delegate
            {
                if (_exiting || _starting) return;

                if (_settings.AutoRestart && _wantRunning && _restartCount < 3)
                {
                    _restartCount++;
                    int delay = 1200 * _restartCount;
                    Logger.Write("Повторный запуск движка через " + delay + " мс (попытка " +
                                 _restartCount + " из 3).");
                    ThreadPool.QueueUserWorkItem(delegate
                    {
                        Thread.Sleep(delay);
                        Ui(delegate
                        {
                            if (!_exiting && _wantRunning) StartEngine();
                        });
                    });
                }
                else
                {
                    _wantRunning = false;
                    _lastError = "Движок остановлен. Подробности в журнале.";
                    UpdateUi();
                }
            });
        }

        private void UpdateUi()
        {
            if (IsDisposed || Disposing) return;

            bool running = _engine != null && _engine.Running;
            bool canToggle = !_starting && Admin.IsElevated();

            _toggleButton.Enabled = canToggle;

            if (_starting)
            {
                _statusDot.DotColor = Theme.Amber;
                _statusTitle.Text = "Запуск…";
                _statusSub.Text = "Проверяем драйвер WinDivert и применяем профиль обхода.";
                _toggleButton.Text = "Запуск…";
                _toggleButton.FillColor = Theme.Accent;
                _toggleButton.HoverColor = Theme.AccentHover;
                _toggleButton.PressedColor = Theme.AccentPressed;
                _toggleButton.TextColor = Color.White;
                _engineInfo.Text = "Подготовка движка";
            }
            else if (running)
            {
                _statusDot.DotColor = Theme.Green;
                _statusTitle.Text = "Обход включён";
                _statusSub.Text = "Пакеты корректируются локально. Соединение с сайтами идёт напрямую, без зарубежных серверов.";
                _toggleButton.Text = "Выключить обход";
                _toggleButton.FillColor = Theme.Red;
                _toggleButton.HoverColor = Color.FromArgb(0xC9, 0x3A, 0x3F);
                _toggleButton.PressedColor = Color.FromArgb(0xAB, 0x2F, 0x34);
                _toggleButton.TextColor = Color.White;
                _engineInfo.Text = "Движок работает · PID " + _engine.ProcessId;
            }
            else if (!string.IsNullOrEmpty(_lastError))
            {
                _statusDot.DotColor = Theme.Red;
                _statusTitle.Text = "Обход не включён";
                _statusSub.Text = Shorten(_lastError, 150);
                _toggleButton.Text = "Повторить";
                _toggleButton.FillColor = Theme.Accent;
                _toggleButton.HoverColor = Theme.AccentHover;
                _toggleButton.PressedColor = Theme.AccentPressed;
                _toggleButton.TextColor = Color.White;
                _engineInfo.Text = "Ошибка · подробности в журнале";
            }
            else
            {
                _statusDot.DotColor = Theme.Soft;
                _statusTitle.Text = "Обход выключен";
                _statusSub.Text = "Трафик идёт напрямую через вашего провайдера. VPN и зарубежные серверы не используются.";
                _toggleButton.Text = "Включить обход";
                _toggleButton.FillColor = Theme.Accent;
                _toggleButton.HoverColor = Theme.AccentHover;
                _toggleButton.PressedColor = Theme.AccentPressed;
                _toggleButton.TextColor = Color.White;
                _engineInfo.Text = "Движок: zapret winws + WinDivert";
            }

            bool editable = !running && !_starting;
            _presetCombo.Enabled = editable;
            _cbQuic.Enabled = editable;
            _cbExclude.Enabled = editable;
            _testButton.Enabled = !_testing;

            if (_trayToggle != null)
            {
                _trayToggle.Text = running ? "Выключить обход" : "Включить обход";
                _trayToggle.Enabled = canToggle;
                try
                {
                    _tray.Text = running ? "ZapretOBKHOD — обход включён" : "ZapretOBKHOD — обход выключен";
                }
                catch { }
            }
        }

        private static string Shorten(string text, int max)
        {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Replace("\r", " ").Replace("\n", " ").Trim();
            if (text.Length <= max) return text;
            return text.Substring(0, max - 1) + "…";
        }

        private void TestClick(object sender, EventArgs e)
        {
            TestAccess();
        }

        private void TestAccess()
        {
            if (_testing) return;

            _testing = true;
            _testButton.Text = "Проверка…";
            _testButton.Enabled = false;
            _chipYouTube.SetTesting();
            _chipDiscord.SetTesting();
            _chipTelegram.SetTesting();
            Logger.Write("Проверка доступа: YouTube, Discord, Telegram.");

            NetTest.RunAll(delegate(List<CheckResult> results)
            {
                Ui(delegate
                {
                    _testing = false;
                    _testButton.Text = "Проверить";
                    _testButton.Enabled = true;

                    foreach (CheckResult result in results)
                    {
                        if (result.Name == "YouTube") _chipYouTube.SetResult(result);
                        else if (result.Name == "Discord") _chipDiscord.SetResult(result);
                        else if (result.Name == "Telegram") _chipTelegram.SetResult(result);

                        Logger.Write("Проверка " + result.Name + ": " +
                                     (result.Ok ? (result.Warn ? "ОК (с ограничениями)" : "ОК") : "ОШИБКА") +
                                     " — " + result.Detail);
                    }
                });
            });
        }

        private void OpenFolder(string path)
        {
            try
            {
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
                Process.Start("explorer.exe", "\"" + path + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось открыть папку:\r\n" + path + "\r\n\r\n" + ex.Message,
                    "Папка", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void ShowAbout(object sender, EventArgs e)
        {
            string text =
                "ZapretOBKHOD 1.0\r\n\r\n" +
                "Локальный обход DPI-блокировок.\r\n\r\n" +
                "• Без VPN и зарубежных серверов.\r\n" +
                "• Трафик идёт напрямую через вашего провайдера.\r\n" +
                "• Пакеты корректируются только на этом компьютере.\r\n\r\n" +
                "Движок: zapret (winws) + WinDivert, открытый исходный код.\r\n" +
                "Лицензия движка: engine\\LICENSE-zapret.txt\r\n\r\n" +
                "Списки сайтов: lists\\\r\n" +
                "Журнал: " + AppPaths.LogsDir;
            MessageBox.Show(this, text, "О программе", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnLogMessage(string line)
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<string>(AppendLog), line); }
                catch { }
                return;
            }
            AppendLog(line);
        }

        private void AppendLog(string line)
        {
            if (_logBox == null || _logBox.IsDisposed) return;

            _logBox.AppendText(line + Environment.NewLine);
            if (_logBox.TextLength > 200000)
            {
                _logBox.Select(0, _logBox.TextLength - 150000);
                _logBox.SelectedText = string.Empty;
            }
            _logBox.SelectionStart = _logBox.TextLength;
            _logBox.ScrollToCaret();
        }

        private void Ui(Action action)
        {
            if (IsDisposed || Disposing) return;
            try
            {
                if (InvokeRequired) BeginInvoke(action);
                else action();
            }
            catch
            {
            }
        }

        private bool HasArg(string value)
        {
            foreach (string arg in _args)
            {
                if (string.Equals(arg, value, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private void OnFormShown(object sender, EventArgs e)
        {
            bool autostart = HasArg("--autostart");
            bool minimized = HasArg("--minimized") || autostart;

            if (minimized)
            {
                HideToTray(false);
            }

            if (autostart)
            {
                StartEngine();
            }
        }

        private void ShowWindow(object sender, EventArgs e)
        {
            ShowWindow();
        }

        private void ShowWindow()
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(ShowWindow)); }
                catch { }
                return;
            }

            _hidden = false;
            ShowInTaskbar = true;
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
            BringToFront();
        }

        private void HideToTray(bool balloon)
        {
            _hidden = true;
            Hide();
            ShowInTaskbar = false;

            if (balloon && _settings.MinimizeToTray && _tray != null)
            {
                try
                {
                    _tray.ShowBalloonTip(2500, "ZapretOBKHOD",
                        "Программа свёрнута в трей. Обход продолжает работать.",
                        ToolTipIcon.Info);
                }
                catch { }
            }
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_exiting && e.CloseReason == CloseReason.UserClosing && _settings.MinimizeToTray)
            {
                e.Cancel = true;
                HideToTray(true);
                return;
            }

            _exiting = true;
            _wantRunning = false;
            if (_timer != null) _timer.Stop();

            try { _engine.Stop(); }
            catch { }

            if (_tray != null)
            {
                _tray.Visible = false;
                _tray.Dispose();
                _tray = null;
            }

            Logger.Write("Выход из программы.");
        }
    }
}
