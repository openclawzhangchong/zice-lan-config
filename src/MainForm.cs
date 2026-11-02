using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Threading;
using System.Windows.Forms;

namespace ZiceLan
{
    public class MainForm : Form
    {
        private ComboBox cmbAdapter;
        private ComboBox cmbPrefix;
        private TextBox txtMask;
        private TextBox txtGateway;
        private ComboBox cmbDnsPreset;
        private TextBox txtDns;
        private NumericUpDown numReserve;
        private LinkLabel lblRouter;
        private Button btnRefresh;
        private Button btnScan;
        private Button btnRandom;
        private Button btnStop;
        private Button btnApply;
        private Button btnDhcp;
        private Button btnWallpaper;
        private Button btnWallBlack;
        private ComboBox cmbWallBackground;
        private ComboBox cmbWallText;
        private Label lblHotspotState;
        private Label lblHotspotPick;
        private ListBox lstHotspot;
        private Button btnHotspotAdvice;
        private Button btnHotspotApply;
        private Button btnHotspotRevert;
        private List<HotspotCandidate> _hotspotCandidates = new List<HotspotCandidate>();
        private int _selectedHotspot = -1;
        private string _hotspotPrevious = string.Empty;
        private ProgressBar bar;
        private Label lblPhase;
        private Label lblStats;
        private Label lblSelected;
        private Label lblIp;
        private System.Windows.Forms.Timer _ipTimer;
        private ListBox lstFree;
        private TextBox txtLog;

        private List<AdapterInfo> _adapters = new List<AdapterInfo>();
        private AdapterInfo _current;
        private ScanResult _scan;
        private string _scanPrefix = string.Empty;
        private string _lastPrefix = string.Empty;
        private int _selectedOctet = -1;
        private bool _busy;
        private bool _suppressAdapterEvent;
        private bool _suppressDnsPresetEvent;
        private volatile bool _cancelScan;
        private readonly Random _rng = new Random();

        public MainForm()
        {
            Text = Product.Title + " " + Product.VersionText;
            Font = new Font("Microsoft YaHei", 9F);
            ClientSize = new Size(1014, 800);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            try
            {
                Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            catch (Exception)
            {
            }
            BuildLayout();
            Load += delegate
            {
                LoadAdapters(true);
                RefreshHotspotState();
                _ipTimer = new System.Windows.Forms.Timer();
                _ipTimer.Interval = 15000;
                _ipTimer.Tick += delegate { if (!_busy) { RefreshIpLineFromNetwork(); } };
                _ipTimer.Start();
            };
            FormClosing += delegate
            {
                _cancelScan = true;
                if (_ipTimer != null)
                {
                    _ipTimer.Stop();
                }
            };
        }

        private void BuildLayout()
        {
            lblIp = new Label();
            lblIp.SetBounds(12, 8, 990, 26);
            lblIp.Font = new Font("Microsoft YaHei", 12F, FontStyle.Bold);
            lblIp.Text = IpDisplay.NoIpLine;
            Controls.Add(lblIp);

            var cfg = new GroupBox();
            cfg.Text = "本机网卡与目标参数";
            cfg.SetBounds(12, 38, 990, 148);
            Controls.Add(cfg);

            cfg.Controls.Add(MakeLabel("网卡", 16, 28, 34));
            cmbAdapter = new ComboBox();
            cmbAdapter.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbAdapter.SetBounds(54, 24, 250, 24);
            cmbAdapter.SelectedIndexChanged += OnAdapterChanged;
            cfg.Controls.Add(cmbAdapter);

            btnRefresh = MakeButton("刷新", 312, 23, 56, 26, delegate { LoadAdapters(true); });
            cfg.Controls.Add(btnRefresh);

            lblRouter = new LinkLabel();
            lblRouter.SetBounds(380, 28, 596, 20);
            lblRouter.Text = "路由器（网关）：检测中…";
            lblRouter.LinkClicked += OnRouterLinkClicked;
            cfg.Controls.Add(lblRouter);

            cfg.Controls.Add(MakeLabel("网段", 16, 60, 34));
            cmbPrefix = new ComboBox();
            cmbPrefix.DropDownStyle = ComboBoxStyle.DropDown;
            cmbPrefix.SetBounds(54, 56, 112, 24);
            foreach (string label in SubnetPresets.Labels())
            {
                cmbPrefix.Items.Add(label);
            }
            cmbPrefix.Text = "192.168.168";
            cmbPrefix.SelectedIndexChanged += delegate { OnPrefixEdited(); };
            cmbPrefix.Leave += delegate { OnPrefixEdited(); };
            cfg.Controls.Add(cmbPrefix);

            cfg.Controls.Add(MakeLabel("掩码", 178, 60, 34));
            txtMask = MakeText("255.255.0.0", 214, 56, 92);
            cfg.Controls.Add(txtMask);

            cfg.Controls.Add(MakeLabel("网关", 318, 60, 34));
            txtGateway = MakeText("192.168.168.1", 354, 56, 92);
            txtGateway.Leave += delegate { ProbeGateway(); };
            cfg.Controls.Add(txtGateway);

            cfg.Controls.Add(MakeLabel("DNS 预设", 458, 60, 58));
            cmbDnsPreset = new ComboBox();
            cmbDnsPreset.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbDnsPreset.SetBounds(520, 56, 112, 24);
            foreach (string label in DnsPresets.Labels())
            {
                cmbDnsPreset.Items.Add(label);
            }
            cmbDnsPreset.SelectedIndex = 0;
            cmbDnsPreset.SelectedIndexChanged += OnDnsPresetChanged;
            cfg.Controls.Add(cmbDnsPreset);

            cfg.Controls.Add(MakeLabel("DNS 地址", 642, 60, 52));
            txtDns = MakeText("223.5.5.5,223.6.6.6", 698, 56, 150);
            txtDns.Leave += delegate { SyncDnsPresetFromText(); };
            cfg.Controls.Add(txtDns);

            cfg.Controls.Add(MakeLabel("保留末段 1-", 858, 60, 66));
            numReserve = new NumericUpDown();
            numReserve.Minimum = 0;
            numReserve.Maximum = 253;
            numReserve.Value = 20;
            numReserve.SetBounds(926, 56, 50, 24);
            cfg.Controls.Add(numReserve);

            cfg.Controls.Add(MakeLabel("壁纸底色", 16, 90, 56));
            cmbWallBackground = new ComboBox();
            cmbWallBackground.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbWallBackground.SetBounds(74, 86, 64, 24);
            foreach (string colorName in WallpaperLayout.ColorNames())
            {
                cmbWallBackground.Items.Add(colorName);
            }
            cmbWallBackground.SelectedItem = "黑";
            cfg.Controls.Add(cmbWallBackground);

            cfg.Controls.Add(MakeLabel("文字色", 146, 90, 50));
            cmbWallText = new ComboBox();
            cmbWallText.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbWallText.SetBounds(198, 86, 64, 24);
            foreach (string colorName in WallpaperLayout.ColorNames())
            {
                cmbWallText.Items.Add(colorName);
            }
            cmbWallText.SelectedItem = "白";
            cfg.Controls.Add(cmbWallText);

            btnWallpaper = MakeButton("生成并设为壁纸", 272, 85, 150, 26, delegate { GenerateWallpaper(false); });
            cfg.Controls.Add(btnWallpaper);

            btnWallBlack = MakeButton("一键纯黑壁纸", 432, 85, 132, 26, delegate { GenerateWallpaper(true); });
            cfg.Controls.Add(btnWallBlack);

            var wallpaperHint = new Label();
            wallpaperHint.SetBounds(574, 90, 402, 20);
            wallpaperHint.ForeColor = Color.DimGray;
            wallpaperHint.Text = "右上角打印当前 IP；写入成功或恢复自动获取后自动换。";
            cfg.Controls.Add(wallpaperHint);

            var note = new Label();
            note.SetBounds(16, 116, 960, 20);
            note.ForeColor = Color.DimGray;
            note.Text = "掩码 / 网关 / DNS 默认沿用本机当前值，只有末段会被换掉；保留末段填 0 = 连路由器一起扫；网段框也可以自己填别的段。";
            cfg.Controls.Add(note);

            var scanBox = new GroupBox();
            scanBox.Text = "空余 IP";
            scanBox.SetBounds(12, 162, 500, 306);
            Controls.Add(scanBox);

            btnScan = MakeButton("1 开始扫描", 16, 24, 112, 30, delegate { StartScan(); });
            scanBox.Controls.Add(btnScan);
            btnRandom = MakeButton("2 随机抽一个", 136, 24, 112, 30, delegate { RandomPick(); });
            btnRandom.Enabled = false;
            scanBox.Controls.Add(btnRandom);
            btnStop = MakeButton("停止", 256, 24, 68, 30, delegate { StopScan(); });
            btnStop.Enabled = false;
            scanBox.Controls.Add(btnStop);

            bar = new ProgressBar();
            bar.SetBounds(16, 62, 468, 14);
            bar.Maximum = 100;
            bar.Value = 0;
            scanBox.Controls.Add(bar);

            lblPhase = new Label();
            lblPhase.SetBounds(16, 80, 468, 18);
            lblPhase.Text = "还没扫描。扫描是只读操作，不会改动本机配置。";
            scanBox.Controls.Add(lblPhase);

            lstFree = new ListBox();
            lstFree.SetBounds(16, 102, 468, 146);
            lstFree.HorizontalScrollbar = true;
            lstFree.IntegralHeight = false;
            lstFree.SelectionMode = SelectionMode.One;
            lstFree.SelectedIndexChanged += OnFreeSelectionChanged;
            scanBox.Controls.Add(lstFree);

            lblStats = new Label();
            lblStats.SetBounds(16, 254, 468, 44);
            lblStats.Text = "空闲 / 占用 / 不明";
            scanBox.Controls.Add(lblStats);

            var logBox = new GroupBox();
            logBox.Text = "运行日志（同时写入文件，便于排查）";
            logBox.SetBounds(520, 162, 482, 306);
            Controls.Add(logBox);

            txtLog = new TextBox();
            txtLog.SetBounds(16, 24, 450, 266);
            txtLog.Multiline = true;
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.BackColor = Color.White;
            logBox.Controls.Add(txtLog);

            var hotspotBox = new GroupBox();
            hotspotBox.Text = "本机 WiFi 热点网段（改这个会瞬断热点下挂的设备，不影响有线网）";
            hotspotBox.SetBounds(12, 474, 990, 176);
            Controls.Add(hotspotBox);

            lblHotspotState = new Label();
            lblHotspotState.SetBounds(16, 24, 958, 20);
            lblHotspotState.Text = "热点状态：读取中…";
            hotspotBox.Controls.Add(lblHotspotState);

            btnHotspotAdvice = MakeButton("算建议段（探测）", 16, 50, 160, 26, delegate { StartHotspotAdvice(); });
            hotspotBox.Controls.Add(btnHotspotAdvice);
            btnHotspotApply = MakeButton("写入选中段并重启 ICS", 184, 50, 220, 26, delegate { ApplyHotspot(false); });
            btnHotspotApply.Enabled = false;
            hotspotBox.Controls.Add(btnHotspotApply);
            btnHotspotRevert = MakeButton("改回原值 / 137.1", 412, 50, 170, 26, delegate { ApplyHotspot(true); });
            hotspotBox.Controls.Add(btnHotspotRevert);

            lblHotspotPick = new Label();
            lblHotspotPick.SetBounds(592, 54, 382, 20);
            lblHotspotPick.Text = "选中：—";
            hotspotBox.Controls.Add(lblHotspotPick);

            lstHotspot = new ListBox();
            lstHotspot.SetBounds(16, 82, 958, 84);
            lstHotspot.IntegralHeight = false;
            lstHotspot.HorizontalScrollbar = true;
            lstHotspot.SelectedIndexChanged += OnHotspotSelectionChanged;
            hotspotBox.Controls.Add(lstHotspot);

            lblSelected = new Label();
            lblSelected.SetBounds(12, 660, 990, 22);
            lblSelected.Font = new Font("Microsoft YaHei", 11F, FontStyle.Bold);
            lblSelected.Text = "还没选中地址";
            Controls.Add(lblSelected);

            btnApply = MakeButton("3 写入静态 IP 并应用", 12, 686, 232, 40, delegate { StartApply(); });
            btnApply.Enabled = false;
            Controls.Add(btnApply);

            btnDhcp = MakeButton("恢复自动获取 IP（回退）", 254, 686, 212, 40, delegate { StartDhcpRestore(); });
            Controls.Add(btnDhcp);

            var lblVersion = new Label();
            lblVersion.SetBounds(700, 696, 302, 20);
            lblVersion.Text = Product.Title + " " + Product.VersionText;
            lblVersion.TextAlign = ContentAlignment.MiddleRight;
            lblVersion.ForeColor = Color.DimGray;
            Controls.Add(lblVersion);

            var lblWarn = new Label();
            lblWarn.SetBounds(12, 734, 990, 54);
            lblWarn.ForeColor = Color.Firebrick;
            lblWarn.Text = "注意：改 IP 会让网络瞬断几秒，远程桌面 / 向日葵会掉线，需要用新地址重连。" + Environment.NewLine
                + "写入前会对目标地址再做一次 ARP 双采样复核；万一改完连不上，用另一台机器或本地登录点「恢复自动获取 IP」即可回退。" + Environment.NewLine
                + "点上面的路由器地址可以用默认浏览器打开路由器管理页。";
            Controls.Add(lblWarn);
        }

        private static Label MakeLabel(string text, int x, int y, int w)
        {
            var label = new Label();
            label.Text = text;
            label.SetBounds(x, y, w, 20);
            return label;
        }

        private static TextBox MakeText(string value, int x, int y, int w)
        {
            var box = new TextBox();
            box.Text = value;
            box.SetBounds(x, y, w, 24);
            return box;
        }

        private static Button MakeButton(string text, int x, int y, int w, int h, EventHandler onClick)
        {
            var button = new Button();
            button.Text = text;
            button.SetBounds(x, y, w, h);
            button.Click += onClick;
            return button;
        }

        private void Ui(Action action)
        {
            if (InvokeRequired)
            {
                BeginInvoke(action);
            }
            else
            {
                action();
            }
        }

        private void Log(string line)
        {
            RunLog.Write(line);
            Ui(delegate { txtLog.AppendText(line + Environment.NewLine); });
        }

        private string PrefixText
        {
            get { return cmbPrefix.Text.Trim(); }
        }

        private void SetBusy(bool busy, string phase)
        {
            _busy = busy;
            btnScan.Enabled = !busy;
            btnRefresh.Enabled = !busy;
            btnStop.Enabled = busy;
            btnRandom.Enabled = !busy && _scan != null && _scan.Free.Count > 0;
            btnApply.Enabled = !busy && _selectedOctet > 0;
            btnDhcp.Enabled = !busy;
            btnWallpaper.Enabled = !busy;
            btnWallBlack.Enabled = !busy;
            btnHotspotAdvice.Enabled = !busy;
            btnHotspotApply.Enabled = !busy && _selectedHotspot >= 0;
            btnHotspotRevert.Enabled = !busy;
            lstHotspot.Enabled = !busy;
            cmbWallBackground.Enabled = !busy;
            cmbWallText.Enabled = !busy;
            cmbAdapter.Enabled = !busy;
            cmbPrefix.Enabled = !busy;
            cmbDnsPreset.Enabled = !busy;
            if (busy)
            {
                lblPhase.Text = phase + " 中…";
            }
        }

        private AdapterInfo CurrentAdapter()
        {
            int index = cmbAdapter.SelectedIndex;
            if (index < 0 || index >= _adapters.Count)
            {
                return null;
            }
            return _adapters[index];
        }

        private void LoadAdapters(bool announce)
        {
            string error;
            _adapters = Adapters.Read(out error);
            if (error.Length > 0)
            {
                Log("读取网卡信息失败：" + error);
            }
            cmbAdapter.Items.Clear();
            foreach (AdapterInfo info in _adapters)
            {
                cmbAdapter.Items.Add(info.Label);
            }
            if (_adapters.Count == 0)
            {
                Log("没有读到任何网卡信息（WMI 可能被禁用），请检查系统服务后重试。");
                _current = null;
                btnApply.Enabled = false;
                RefreshIpLine();
                return;
            }
            int chosen = 0;
            for (int i = 0; i < _adapters.Count; i++)
            {
                if (_adapters[i].InPrefix(PrefixText))
                {
                    chosen = i;
                    break;
                }
            }
            _suppressAdapterEvent = true;
            try
            {
                cmbAdapter.SelectedIndex = chosen;
            }
            finally
            {
                _suppressAdapterEvent = false;
            }
            _current = _adapters[chosen];
            PrefillFromAdapter(_current);
            if (announce)
            {
                Log("网卡 " + _current.Name + "（ifIndex " + _current.InterfaceIndex + "）当前 " + _current.Ip + "/" + _current.Mask
                    + "，网关 " + _current.Gateway + "，DNS " + string.Join(",", _current.Dns.ToArray())
                    + (_current.DhcpEnabled ? "，自动获取已开启" : "，已是静态地址"));
            }
            RefreshIpLine();
        }

        private void OnAdapterChanged(object sender, EventArgs e)
        {
            _current = CurrentAdapter();
            if (_current == null)
            {
                return;
            }
            PrefillFromAdapter(_current);
            InvalidateScan(_suppressAdapterEvent ? string.Empty : "换了网卡，需要重新扫描。");
            RefreshIpLine();
        }

        private void PrefillFromAdapter(AdapterInfo info)
        {
            if (IpCore.IsValidIpv4(info.Ip))
            {
                string prefix = IpCore.BasePrefix(info.Ip);
                if (!cmbPrefix.Items.Contains(prefix))
                {
                    cmbPrefix.Items.Add(prefix);
                }
                cmbPrefix.Text = prefix;
                _lastPrefix = prefix;
            }
            txtMask.Text = info.Mask.Length > 0 ? info.Mask : "255.255.0.0";
            txtGateway.Text = info.Gateway;
            _suppressDnsPresetEvent = true;
            txtDns.Text = string.Join(",", info.Dns.ToArray());
            cmbDnsPreset.Text = DnsPresets.NameForServers(txtDns.Text);
            _suppressDnsPresetEvent = false;
            ProbeGateway();
        }

        private void OnPrefixEdited()
        {
            string prefix = PrefixText;
            if (!IpCore.IsValidPrefix(prefix))
            {
                lblPhase.Text = "网段前缀不对，应写成三段数字，比如 192.168.168";
                return;
            }
            if (string.Equals(prefix, _lastPrefix, StringComparison.Ordinal))
            {
                return;
            }
            string gateway = txtGateway.Text.Trim();
            bool derivedFromOldPrefix = gateway.Length == 0
                || string.Equals(gateway, SubnetPresets.FallbackGatewayFor(_lastPrefix), StringComparison.Ordinal)
                || string.Equals(gateway, IpCore.Combine(_lastPrefix, 1), StringComparison.Ordinal);
            _lastPrefix = prefix;
            if (derivedFromOldPrefix)
            {
                txtGateway.Text = SubnetPresets.FallbackGatewayFor(prefix);
            }
            InvalidateScan("网段改成 " + prefix + " 了，需要重新扫描。");
            ProbeGateway();
        }

        private void InvalidateScan(string message)
        {
            _scan = null;
            _scanPrefix = string.Empty;
            _selectedOctet = -1;
            lstFree.Items.Clear();
            UpdateSelectedLabel();
            lblStats.Text = "空闲 / 占用 / 不明";
            bar.Value = 0;
            btnRandom.Enabled = false;
            btnApply.Enabled = false;
            if (message.Length > 0)
            {
                lblPhase.Text = message;
            }
        }

        private void OnDnsPresetChanged(object sender, EventArgs e)
        {
            if (_suppressDnsPresetEvent)
            {
                return;
            }
            DnsPreset preset = DnsPresets.Find(Convert.ToString(cmbDnsPreset.SelectedItem));
            if (preset == null)
            {
                return;
            }
            _suppressDnsPresetEvent = true;
            txtDns.Text = DnsPresets.TextFor(preset);
            _suppressDnsPresetEvent = false;
            Log("DNS 预设切到 " + preset.Name + "：" + preset.Primary
                + (preset.Secondary.Length > 0 ? " / " + preset.Secondary : ""));
        }

        private void SyncDnsPresetFromText()
        {
            if (_suppressDnsPresetEvent)
            {
                return;
            }
            _suppressDnsPresetEvent = true;
            cmbDnsPreset.Text = DnsPresets.NameForServers(txtDns.Text.Trim());
            _suppressDnsPresetEvent = false;
        }

        private void ProbeGateway()
        {
            string gateway = txtGateway.Text.Trim();
            Ui(delegate
            {
                if (!IpCore.IsValidIpv4(gateway))
                {
                    lblRouter.Text = "路由器（网关）：地址没填，写入时不会设置网关";
                    lblRouter.LinkColor = Color.DimGray;
                    lblRouter.Enabled = false;
                    return;
                }
                lblRouter.Text = "路由器（网关）：" + gateway + " 正在测连通…";
                lblRouter.Enabled = true;
            });
            ThreadPool.QueueUserWorkItem(delegate
            {
                int answers = 0;
                if (IpCore.IsValidIpv4(gateway))
                {
                    for (int i = 0; i < 3; i++)
                    {
                        if (Scanner.ProbePing(gateway, 1000) == IPStatus.Success)
                        {
                            answers++;
                        }
                        Thread.Sleep(150);
                    }
                }
                bool reachable = answers > 0;
                Ui(delegate
                {
                    lblRouter.LinkColor = reachable ? Color.ForestGreen : Color.DimGray;
                    lblRouter.Text = "路由器（网关）：" + gateway
                        + (reachable ? " 可达（3 次探测 " + answers + " 次应答）" : " 3 次都没应答（路由器可能限速 ICMP，不影响写入）")
                        + "　点这里用浏览器打开管理页 http://" + gateway;
                });
            });
        }

        private void OnRouterLinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            string gateway = txtGateway.Text.Trim();
            string error;
            if (Browser.Open(gateway, out error))
            {
                Log("已请求浏览器打开 " + Browser.NormalizeUrl(gateway));
            }
            else
            {
                Log("打开路由器管理页失败：" + error);
            }
            lblRouter.LinkVisited = true;
        }

        private void StopScan()
        {
            if (!_busy)
            {
                return;
            }
            _cancelScan = true;
            btnStop.Enabled = false;
            lblPhase.Text = "正在停止，已扫到的结果会保留…";
            Log("已请求停止扫描（未扫完的地址不会当成空闲）");
        }

        private void StartScan()
        {
            if (_busy)
            {
                return;
            }
            string prefix = PrefixText;
            if (!IpCore.IsValidPrefix(prefix))
            {
                Log("网段前缀不对，应写成三段数字，比如 192.168.168");
                return;
            }
            AdapterInfo adapter = CurrentAdapter();
            if (adapter == null)
            {
                Log("先选一块网卡。");
                return;
            }
            if (!IpCore.IsValidIpv4(adapter.Ip))
            {
                Log("这块网卡 " + adapter.Name + " 现在没有 IPv4 地址，扫了也不作数（本机还没进到任何网段里）。先连网再来。");
                return;
            }
            int reserveTo = Convert.ToInt32(numReserve.Value);
            int reserveFrom = reserveTo >= 1 ? 1 : 0;

            InvalidateScan(string.Empty);
            _cancelScan = false;
            bar.Value = 0;
            SetBusy(true, "扫描");
            Log("开始扫描 " + prefix + ".1-254，保留末段 1-" + reserveTo + "，网卡 " + adapter.Name + "（只读，不改配置）");

            string ownIp = adapter.Ip;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string arpTable = Adapters.ReadArpTableText();
                ScanResult result = Scanner.Run(prefix, reserveFrom, reserveTo, ownIp, ownIp, arpTable,
                    delegate(string phase, int done, int total)
                    {
                        Ui(delegate
                        {
                            bar.Maximum = total > 0 ? total : 1;
                            bar.Value = done > bar.Maximum ? bar.Maximum : done;
                            lblPhase.Text = phase + "：" + done + "/" + (total > 0 ? total : 1);
                        });
                    },
                    delegate { return _cancelScan; });
                Ui(delegate { FinishScan(result, prefix); });
            });
        }

        private void FinishScan(ScanResult result, string prefix)
        {
            _scan = result;
            _scanPrefix = prefix;
            foreach (int octet in result.Free)
            {
                lstFree.Items.Add(IpCore.Combine(prefix, octet));
            }
            double seconds = result.ElapsedMs / 1000.0;
            string secondsText = seconds.ToString("0.0", CultureInfo.InvariantCulture);
            lblStats.Text = "空闲 " + result.Free.Count + " 个 · 占用 " + result.Used.Count + " 个 · 不明 " + result.Unknown.Count
                + " 个 · 候选 " + result.Candidates + " 个 · 用时 " + secondsText + " 秒"
                + (result.NotScanned > 0 ? " · 未扫 " + result.NotScanned + " 个" : "");
            if (result.Note.Length > 0)
            {
                lblPhase.Text = result.Note;
                Log("提示：" + result.Note);
            }
            else
            {
                lblPhase.Text = "扫描完成，选中一个空闲地址（或用随机抽取），然后点第 3 步写入。";
            }
            Log("扫描结果：空闲 " + result.Free.Count + " / 占用 " + result.Used.Count + " / 不明 " + result.Unknown.Count
                + (result.NotScanned > 0 ? " / 未扫 " + result.NotScanned : "") + "，用时 " + secondsText + " 秒"
                + "（ping 段 " + (result.PingMs / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " 秒，ARP 段 "
                + (result.ArpMs / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " 秒）");
            SetBusy(false, string.Empty);
        }

        private void OnFreeSelectionChanged(object sender, EventArgs e)
        {
            int index = lstFree.SelectedIndex;
            if (index < 0 || _scan == null || index >= _scan.Free.Count)
            {
                return;
            }
            _selectedOctet = _scan.Free[index];
            UpdateSelectedLabel();
            btnApply.Enabled = !_busy && _selectedOctet > 0;
        }

        private void UpdateSelectedLabel()
        {
            if (_selectedOctet > 0)
            {
                lblSelected.Text = "将写入：" + IpCore.Combine(PrefixText, _selectedOctet);
            }
            else
            {
                lblSelected.Text = "还没选中地址";
            }
        }

        private void RandomPick()
        {
            if (_busy || _scan == null || _scan.Free.Count == 0)
            {
                Log("没有可抽取的空闲地址，先重新扫描。");
                return;
            }
            int ownOctet = _current != null ? IpCore.OctetOf(_current.Ip) : -1;
            int octet = Picker.PickRandom(_scan.Free, ownOctet, _rng);
            if (octet < 0)
            {
                Log("空闲列表里只剩本机当前地址，换一个再试。");
                return;
            }
            int index = _scan.Free.IndexOf(octet);
            lstFree.SelectedIndex = index;
            if (index >= 0)
            {
                lstFree.TopIndex = index;
            }
            Log("随机抽到 " + IpCore.Combine(PrefixText, octet));
        }

        private void StartApply()
        {
            if (_busy || _selectedOctet <= 0)
            {
                return;
            }
            AdapterInfo adapter = CurrentAdapter();
            if (adapter == null || !adapter.UsableForNetsh)
            {
                Log("网卡信息不可用，先点「刷新」。");
                return;
            }
            string prefix = PrefixText;
            if (!string.Equals(prefix, _scanPrefix, StringComparison.Ordinal))
            {
                Log("网段前缀在扫描之后被改过（这一轮扫的是 " + _scanPrefix + "），请重新扫描再写入。");
                return;
            }
            string mask = txtMask.Text.Trim();
            string gateway = txtGateway.Text.Trim();
            List<string> dns = ParseDns(txtDns.Text);
            string newIp = IpCore.Combine(prefix, _selectedOctet);

            if (!IpCore.IsValidIpv4(mask))
            {
                Log("掩码格式不对：" + mask);
                return;
            }
            if (gateway.Length > 0 && !IpCore.IsValidIpv4(gateway))
            {
                Log("网关格式不对：" + gateway);
                return;
            }
            foreach (string server in dns)
            {
                if (!IpCore.IsValidIpv4(server))
                {
                    Log("DNS 格式不对：" + server);
                    return;
                }
            }

            string detail = "网卡：" + adapter.Name + "（ifIndex " + adapter.InterfaceIndex + "）" + Environment.NewLine
                + "IP 地址：" + adapter.Ip + "  →  " + newIp + Environment.NewLine
                + "子网掩码：" + mask + Environment.NewLine
                + "默认网关：" + (gateway.Length > 0 ? gateway : "（不设）") + Environment.NewLine
                + "DNS：" + (dns.Count > 0 ? string.Join(",", dns.ToArray()) : "（不改）") + Environment.NewLine
                + Environment.NewLine
                + "网络会瞬断几秒，远程桌面 / 向日葵会掉线，需要用新地址 " + newIp + " 重连。" + Environment.NewLine
                + "确定要写入吗？";
            if (MessageBox.Show(this, detail, "确认写入静态 IP", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                Log("已取消写入，本机配置未改动。");
                return;
            }

            SetBusy(true, "写入");
            lblSelected.Text = "写入中：" + newIp;
            Log("开始写入 " + newIp);
            ThreadPool.QueueUserWorkItem(delegate
            {
                ApplyReport report = NetworkApplier.Apply(adapter, newIp, mask, gateway, dns, Log);
                Ui(delegate
                {
                    SetBusy(false, string.Empty);
                    if (report.Success)
                    {
                        Log("写入成功：" + report.Message);
                        LoadAdapters(false);
                        _selectedOctet = -1;
                        UpdateSelectedLabel();
                        MessageBox.Show(this, report.Message, "已生效",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                        RefreshWallpaperFor(newIp);
                        return;
                    }
                    Log("写入未确认：" + report.Message);
                    MessageBox.Show(this, report.Message, "没成功",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                });
            });
        }

        private void RefreshIpLine()
        {
            AdapterInfo adapter = CurrentAdapter();
            bool hasIp = adapter != null && IpCore.IsValidIpv4(adapter.Ip);
            lblIp.Text = IpDisplay.Line(adapter);
            lblIp.ForeColor = hasIp ? Color.Black : Color.Firebrick;
        }

        private void RefreshIpLineFromNetwork()
        {
            int wantedIndex = _current != null ? _current.Index : -1;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error;
                List<AdapterInfo> list = Adapters.Read(out error);
                AdapterInfo found = null;
                foreach (AdapterInfo info in list)
                {
                    if (info.Index == wantedIndex)
                    {
                        found = info;
                        break;
                    }
                }
                AdapterInfo result = found;
                Ui(delegate
                {
                    if (result != null)
                    {
                        _current = result;
                        AdapterInfo shown = CurrentAdapter();
                        if (shown != null)
                        {
                            _adapters[cmbAdapter.SelectedIndex] = result;
                        }
                        RefreshIpLine();
                    }
                    else
                    {
                        RefreshIpLine();
                    }
                });
            });
        }

        private void GenerateWallpaper(bool forceBlackWhite)
        {
            if (_busy)
            {
                return;
            }
            RefreshIpLine();
            AdapterInfo adapter = CurrentAdapter();
            string ip = adapter != null ? adapter.Ip : (_current != null ? _current.Ip : string.Empty);
            if (!IpCore.IsValidIpv4(ip))
            {
                Log("这台机器现在没有可用的 IPv4 地址，壁纸不生成（否则只会印一个空地址）。先插网线或确认已取到地址再试。");
                return;
            }
            string background = forceBlackWhite ? "黑" : Convert.ToString(cmbWallBackground.SelectedItem);
            string textColor = forceBlackWhite ? "白" : Convert.ToString(cmbWallText.SelectedItem);
            int width = WallpaperService.PrimaryScreenWidth;
            int height = WallpaperService.PrimaryScreenHeight;
            Log((forceBlackWhite ? "一键纯黑壁纸：" : "生成壁纸：") + WallpaperLayout.Caption(ip)
                + "，" + width + "x" + height + " 屏幕");
            SetBusy(true, "生成壁纸");
            ThreadPool.QueueUserWorkItem(delegate
            {
                WallpaperService.Apply(ip, background, textColor, width, height, true, Log);
                Ui(delegate { SetBusy(false, string.Empty); });
            });
        }

        private void RefreshWallpaperFor(string ip)
        {
            if (!IpCore.IsValidIpv4(ip))
            {
                Log("拿不到生效后的 IP，壁纸没刷新。");
                return;
            }
            string background = Convert.ToString(cmbWallBackground.SelectedItem);
            string textColor = Convert.ToString(cmbWallText.SelectedItem);
            int width = WallpaperService.PrimaryScreenWidth;
            int height = WallpaperService.PrimaryScreenHeight;
            ThreadPool.QueueUserWorkItem(delegate
            {
                WallpaperService.Apply(ip, background, textColor, width, height, true, Log);
            });
        }

        private void RefreshHotspotState()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                HotspotState state = HotspotSettings.Read();
                Ui(delegate
                {
                    lblHotspotState.Text = "热点状态：" + state.Describe();
                    lblHotspotState.ForeColor = state.Error.Length > 0 ? Color.Firebrick : Color.Black;
                });
            });
        }

        private static string JoinGateways(List<HotspotCandidate> candidates)
        {
            var parts = new List<string>();
            foreach (HotspotCandidate candidate in candidates)
            {
                parts.Add(candidate.Gateway + "(" + candidate.Distance + "跳)");
            }
            return string.Join("  ", parts.ToArray());
        }

        private static string JoinOctets(List<int> octets)
        {
            var parts = new List<string>();
            foreach (int octet in octets)
            {
                parts.Add(octet.ToString(CultureInfo.InvariantCulture));
            }
            return string.Join(",", parts.ToArray());
        }

        private void StartHotspotAdvice()
        {
            if (_busy)
            {
                return;
            }
            lstHotspot.Items.Clear();
            _hotspotCandidates = new List<HotspotCandidate>();
            _selectedHotspot = -1;
            lblHotspotPick.Text = "选中：—";
            btnHotspotApply.Enabled = false;
            SetBusy(true, "算建议段");
            Log("算热点建议段：先排除本机已占用段和常见默认段，再对全部候选段的网关位发 ping（约 5-10 秒）");
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error;
                List<AdapterInfo> adapters = Adapters.Read(out error);
                HotspotState state = HotspotSettings.Read();
                HotspotAdvice advice = HotspotSettings.Advise(adapters, state.ScopeAddress);
                Ui(delegate
                {
                    foreach (HotspotCandidate candidate in advice.Picked)
                    {
                        lstHotspot.Items.Add(candidate.Gateway + "　" + candidate.Reason);
                    }
                    _hotspotCandidates = advice.Picked;
                    Log("本机已占用段：" + JoinOctets(advice.Used) + "；当前热点段："
                        + (state.ScopeAddress.Length > 0 ? state.ScopeAddress : "读不到"));
                    if (advice.Responded.Count > 0)
                    {
                        Log("网关位有人应答、已剔除的段：" + JoinOctets(advice.Responded));
                    }
                    Log("建议段：" + JoinGateways(advice.Picked));
                    lblPhase.Text = "热点建议：" + advice.Note;
                    SetBusy(false, string.Empty);
                });
            });
        }

        private void OnHotspotSelectionChanged(object sender, EventArgs e)
        {
            int index = lstHotspot.SelectedIndex;
            if (index < 0 || index >= _hotspotCandidates.Count)
            {
                _selectedHotspot = -1;
                btnHotspotApply.Enabled = false;
                return;
            }
            _selectedHotspot = index;
            lblHotspotPick.Text = "选中：" + _hotspotCandidates[index].Gateway;
            btnHotspotApply.Enabled = !_busy;
        }

        private void ApplyHotspot(bool revert)
        {
            if (_busy)
            {
                return;
            }
            string target;
            if (revert)
            {
                target = _hotspotPrevious.Length > 0 ? _hotspotPrevious : HotspotSettings.DefaultGateway;
            }
            else
            {
                if (_selectedHotspot < 0 || _selectedHotspot >= _hotspotCandidates.Count)
                {
                    Log("先从下面的建议列表里选一个段。");
                    return;
                }
                target = _hotspotCandidates[_selectedHotspot].Gateway;
            }

            HotspotState before = HotspotSettings.Read();
            if (string.Equals(before.ScopeAddress, target, StringComparison.OrdinalIgnoreCase))
            {
                Log("热点段已经是 " + target + "，不用改。");
                return;
            }
            string current = before.ScopeAddress.Length > 0 ? before.ScopeAddress : "读不到";
            string detail = "本机 WiFi 热点网段：" + current + "  →  " + target + Environment.NewLine + Environment.NewLine
                + "步骤：写注册表 ScopeAddress / ScopeAddressBackup → 重启 ICS 服务（SharedAccess）"
                + "→ 禁用再启用热点虚拟网卡试着重建承载网络。" + Environment.NewLine
                + "注意：承载网络是在移动热点被打开那一刻按注册表建的，所以新段通常要你在"
                + "「设置 → 移动热点」关掉再打开一次才落到网卡上；有线网不受影响。" + Environment.NewLine
                + "热点下挂的设备会掉线重连。原值 " + current + " 会记进日志，界面也有「改回原值」按钮。";
            string title = revert ? "确认改回热点原段" : "确认修改热点网段";
            if (MessageBox.Show(this, detail, title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                Log("已取消修改热点网段，注册表未改动。");
                return;
            }

            SetBusy(true, "改热点段");
            Log((revert ? "改回热点段 " : "写入热点段 ") + target);
            ThreadPool.QueueUserWorkItem(delegate
            {
                ChangeResult change = HotspotSettings.Change(target, Log);
                if (change.Previous.Length > 0)
                {
                    _hotspotPrevious = change.Previous;
                }
                Ui(delegate
                {
                    string message = (change.Written ? "" : "没写成。") + change.Detail;
                    lblHotspotState.Text = "热点状态：" + (change.After != null ? change.After.Describe() : "读不到");
                    SetBusy(false, string.Empty);
                    MessageBox.Show(this, message, change.Verified ? "已生效（热点设备会重连）" : "请核对",
                        MessageBoxButtons.OK, change.Verified ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                    RefreshIpLine();
                });
            });
        }

        private void StartDhcpRestore()
        {
            if (_busy)
            {
                return;
            }
            AdapterInfo adapter = CurrentAdapter();
            if (adapter == null || !adapter.UsableForNetsh)
            {
                Log("网卡信息不可用，先点「刷新」。");
                return;
            }
            string detail = "把网卡 " + adapter.Name + " 的 IPv4 地址和 DNS 都改回自动获取（DHCP）。"
                + Environment.NewLine + Environment.NewLine + "网络同样会瞬断几秒。确定吗？";
            if (MessageBox.Show(this, detail, "确认恢复自动获取", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            {
                Log("已取消回退。");
                return;
            }
            SetBusy(true, "回退");
            Log("开始恢复自动获取 IP");
            ThreadPool.QueueUserWorkItem(delegate
            {
                ApplyReport report = NetworkApplier.RestoreDhcp(adapter, Log);
                Ui(delegate
                {
                    SetBusy(false, string.Empty);
                    Log((report.Success ? "回退成功：" : "回退结果：") + report.Message);
                    LoadAdapters(false);
                    MessageBox.Show(this, report.Message, report.Success ? "已恢复自动获取" : "请确认网络",
                        MessageBoxButtons.OK, report.Success ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                    if (_current != null)
                    {
                        RefreshWallpaperFor(_current.Ip);
                    }
                });
            });
        }

        private static List<string> ParseDns(string text)
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                return list;
            }
            foreach (string part in text.Split(new char[] { ',', '，', ' ', ';', '；' }, StringSplitOptions.RemoveEmptyEntries))
            {
                list.Add(part.Trim());
            }
            return list;
        }
    }
}
