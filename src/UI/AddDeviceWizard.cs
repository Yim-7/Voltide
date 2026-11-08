using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using Voltide.Core;
using Voltide.Interop;

namespace Voltide.UI
{
    /// <summary>
    /// “添加设备”向导。扫描全部 HID 设备，用户选择一个并配置查询报文与
    /// 电量字节后可先“测试读取”核对，确认无误再保存到 devices.json。
    /// 此后 CustomDeviceProvider 会自动匹配并准确显示电量，无需改代码。
    /// </summary>
    public class AddDeviceWizard : Form
    {
        private readonly ComboBox _device = new ComboBox();
        private readonly Button _rescan = new Button();
        private readonly TextBox _name = new TextBox();
        private readonly NumericUpDown _reportLen = new NumericUpDown();
        private readonly ComboBox _mode = new ComboBox();
        private readonly TextBox _request = new TextBox();
        private readonly NumericUpDown _battIndex = new NumericUpDown();
        private readonly NumericUpDown _chargeIndex = new NumericUpDown();
        private readonly ComboBox _kind = new ComboBox();
        private readonly Button _test = new Button();
        private readonly Button _probe = new Button();
        private readonly Button _diag = new Button();
        private readonly Button _save = new Button();
        private readonly Button _cancel = new Button();
        private readonly TextBox _result = new TextBox();
        private readonly Label _warn = new Label();

        private int _row;

        public AddDeviceWizard()
        {
            Text = "添加设备";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(480, 660);
            BackColor = Color.FromArgb(245, 245, 247);

            BuildLayout();
            LoadDevices();
        }

        // ------------------------------------------------------------- layout

        private void BuildLayout()
        {
            const int labelW = 96;
            const int inputX = 110;
            const int inputW = 340;

            AddHeader("第 1 步：选择设备");
            _device.Location = new Point(inputX, NextY());
            _device.Size = new Size(inputW - 90, 24);
            _device.DropDownStyle = ComboBoxStyle.DropDownList;
            _device.SelectedIndexChanged += DeviceSelected;
            Controls.Add(_device);

            _rescan.Location = new Point(inputX + inputW - 82, _device.Top - 1);
            _rescan.Size = new Size(82, 26);
            _rescan.Text = "重新扫描";
            _rescan.Click += delegate { LoadDevices(); };
            Controls.Add(_rescan);
            Advance();

            // 接口适配警告：选中只读/短帧接口时实时提示。
            _warn.Location = new Point(14, NextY());
            _warn.Size = new Size(450, 26);
            _warn.ForeColor = Color.FromArgb(176, 50, 50);
            Controls.Add(_warn);
            Advance();

            AddHeader("第 2 步：填写信息");
            AddRow("设备名称", _name, labelW, inputX, inputW);
            ConfigureNumber(_reportLen, 1, 4096, 64);
            AddRow("报告长度", _reportLen, labelW, inputX, 120);

            _mode.DropDownStyle = ComboBoxStyle.DropDownList;
            _mode.Items.AddRange(new object[] { "主动查询（发送报文，最准）", "被动监听（只读不写）" });
            _mode.SelectedIndex = 0;
            _mode.SelectedIndexChanged += delegate { UpdateWarning(); };
            AddRow("获取方式", _mode, labelW, inputX, inputW);

            _kind.DropDownStyle = ComboBoxStyle.DropDownList;
            _kind.Items.AddRange(new object[] { "耳机", "鼠标", "键盘", "其他" });
            _kind.SelectedIndex = 0;
            AddRow("设备类型", _kind, labelW, inputX, 160);

            AddHeader("第 3 步：配置报文（十六进制）");
            Label reqLabel = new Label();
            reqLabel.Text = "查询报文";
            reqLabel.Location = new Point(14, NextY());
            reqLabel.Size = new Size(450, 18);
            Controls.Add(reqLabel);

            _request.Location = new Point(inputX, reqLabel.Bottom + 4);
            _request.Size = new Size(inputW, 48);
            _request.Multiline = true;
            _request.ScrollBars = ScrollBars.Vertical;
            _request.Text = "55 65 00 00";
            Controls.Add(_request);
            _row = ControlRow(_request.Bottom);

            ConfigureNumber(_battIndex, 0, 255, 3);
            AddRow("电量字节", _battIndex, labelW, inputX, 100);
            ConfigureNumber(_chargeIndex, -1, 255, -1);
            AddRow("充电字节", _chargeIndex, labelW, inputX, 100);

            AddHeader("第 4 步：测试并保存");
            int btnY = NextY();
            _test.Location = new Point(inputX, btnY);
            _test.Size = new Size(100, 30);
            _test.Text = "测试读取";
            _test.Click += TestRead;
            Controls.Add(_test);

            _probe.Location = new Point(inputX + 108, btnY);
            _probe.Size = new Size(100, 30);
            _probe.Text = "自动探测";
            _probe.Click += ProbeCommand;
            Controls.Add(_probe);

            _diag.Location = new Point(inputX + 216, btnY);
            _diag.Size = new Size(110, 30);
            _diag.Text = "导出诊断";
            _diag.Click += ExportDiagnostics;
            Controls.Add(_diag);
            Advance();

            _result.Location = new Point(inputX, NextY());
            _result.Size = new Size(inputW, 110);
            _result.Multiline = true;
            _result.ReadOnly = true;
            _result.ScrollBars = ScrollBars.Vertical;
            _result.BackColor = Color.FromArgb(238, 238, 242);
            Controls.Add(_result);
            _row = ControlRow(_result.Bottom);

            _save.Location = new Point(inputX + 120, NextY());
            _save.Size = new Size(100, 32);
            _save.Text = "保存设备";
            _save.Click += SaveDevice;
            Controls.Add(_save);

            _cancel.Location = new Point(inputX + 232, _save.Top);
            _cancel.Size = new Size(90, 32);
            _cancel.Text = "取消";
            _cancel.Click += delegate { Close(); };
            Controls.Add(_cancel);
        }

        private void AddHeader(string text)
        {
            Label h = new Label();
            h.Text = text;
            h.Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold);
            h.ForeColor = Color.FromArgb(60, 56, 120);
            h.Location = new Point(14, NextY());
            h.Size = new Size(450, 22);
            Controls.Add(h);
            Advance();
        }

        private void AddRow(string label, Control input, int labelW, int inputX, int inputW)
        {
            Label l = new Label();
            l.Text = label;
            l.TextAlign = ContentAlignment.MiddleLeft;
            int y = NextY();
            l.Location = new Point(14, y);
            l.Size = new Size(labelW, 24);
            Controls.Add(l);

            input.Location = new Point(inputX, y);
            input.Size = new Size(inputW, input is NumericUpDown ? 24 : 26);
            Controls.Add(input);
            Advance();
        }

        private int NextY()
        {
            return 12 + _row * 30;
        }

        private void Advance()
        {
            _row++;
        }

        private static int ControlRow(int bottom)
        {
            return (bottom - 12) / 30 + 1;
        }

        private static void ConfigureNumber(NumericUpDown n, decimal min, decimal max, decimal value)
        {
            n.Minimum = min;
            n.Maximum = max;
            n.Value = value;
        }

        // ------------------------------------------------------------- devices

        private void LoadDevices()
        {
            _device.Items.Clear();
            try
            {
                List<HidDeviceInfo> catalog = HidNative.Enumerate();
                int best = -1;
                long bestScore = long.MinValue;
                for (int i = 0; i < catalog.Count; i++)
                {
                    _device.Items.Add(new DeviceItem(catalog[i]));
                    long score = InterfaceScore(catalog[i]);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = i;
                    }
                }
                // 默认选中最适合主动查询的接口（可写、长帧、厂商自定义），
                // 避免一上来就落在短帧只读接口上。
                if (best >= 0) _device.SelectedIndex = best;
            }
            catch (Exception ex)
            {
                MessageBox.Show("枚举设备失败：" + ex.Message);
            }
        }

        private void DeviceSelected(object sender, EventArgs e)
        {
            DeviceItem item = _device.SelectedItem as DeviceItem;
            if (item == null) return;
            HidDeviceInfo h = item.Device;

            _name.Text = string.IsNullOrEmpty(h.ProductName) ? "我的设备" : h.ProductName;
            int len = Math.Max(h.InputReportLength, h.OutputReportLength);
            if (len > 0) _reportLen.Value = Clamp(len, _reportLen.Minimum, _reportLen.Maximum);

            UpdateWarning();
        }

        /// <summary>
        /// 接口对“主动查询”的适配评分：可写、长帧、厂商自定义用途页得分高。
        /// 短帧只读的系统控制接口得分低，因此不会被默认选中。
        /// </summary>
        private static long InterfaceScore(HidDeviceInfo h)
        {
            long score = h.InputReportLength + h.OutputReportLength;
            if (h.OutputReportLength > 0) score += 10000;
            if (h.UsagePage >= 0xFF00) score += 5000;
            if (h.InputReportLength >= 64) score += 2000;
            return score;
        }

        /// <summary>根据当前所选接口与获取方式，给出可操作的警告。</summary>
        private void UpdateWarning()
        {
            _warn.Text = "";
            DeviceItem item = _device.SelectedItem as DeviceItem;
            if (item == null) return;
            HidDeviceInfo h = item.Device;
            bool query = _mode.SelectedIndex != 1;

            if (query && h.OutputReportLength <= 0)
            {
                _warn.Text = "⚠ 此接口只读（输出长度 0），主动查询会写入失败。请在下拉中选择带“输出≥64”的接口，或改用被动监听。";
            }
            else if (query && h.InputReportLength < 8)
            {
                _warn.Text = "⚠ 此接口报告长度仅 " + h.InputReportLength + " 字节，通常不是电量命令接口。请确认是否选对了同一设备的其他接口。";
            }
        }

        private static decimal Clamp(int v, decimal min, decimal max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        // ------------------------------------------------------------- actions

        private CustomDeviceRule CollectRule()
        {
            DeviceItem item = _device.SelectedItem as DeviceItem;
            if (item == null) throw new InvalidOperationException("请先选择设备");

            HidDeviceInfo h = item.Device;
            CustomDeviceRule rule = new CustomDeviceRule();
            rule.Name = _name.Text.Trim();
            rule.VendorId = h.VendorId;
            rule.ProductId = h.ProductId;
            rule.ReportLength = (int)_reportLen.Value;
            rule.Mode = _mode.SelectedIndex == 1 ? BatteryQueryMode.Listen : BatteryQueryMode.Query;
            rule.RequestHex = _request.Text;
            rule.BatteryIndex = (int)_battIndex.Value;
            rule.ChargingIndex = (int)_chargeIndex.Value;
            rule.Kind = ParseKind(_kind.SelectedIndex);
            rule.MatchPath = h.Path;
            return rule;
        }

        private static DeviceKind ParseKind(int index)
        {
            switch (index)
            {
                case 0: return DeviceKind.Headset;
                case 1: return DeviceKind.Mouse;
                case 2: return DeviceKind.Keyboard;
                default: return DeviceKind.Other;
            }
        }

        /// <summary>
        /// 导出全部 HID 接口的完整信息到 hid-diag.txt，便于在看不到设备时
        /// 准确判断该用哪个接口、哪类报告（中断/Feature）以及字节索引。
        /// </summary>
        private void ExportDiagnostics(object sender, EventArgs e)
        {
            try
            {
                List<HidDeviceInfo> catalog = HidNative.Enumerate();
                StringBuilder sb = new StringBuilder();
                sb.AppendLine(AppBrand.Name + " HID 诊断  时间 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine("接口总数: " + catalog.Count);
                sb.AppendLine();

                for (int i = 0; i < catalog.Count; i++)
                {
                    HidDeviceInfo h = catalog[i];
                    sb.AppendLine("[" + i + "] " + h.VendorId.ToString("X4") + ":" + h.ProductId.ToString("X4"));
                    sb.AppendLine("    产品  : " + h.ProductName);
                    sb.AppendLine("    厂商  : " + h.Manufacturer);
                    sb.AppendLine("    用途页: " + h.UsagePage.ToString("X4") + "  用途: " + h.Usage.ToString("X4"));
                    sb.AppendLine("    报告  : 输入" + h.InputReportLength +
                                  " 输出" + h.OutputReportLength +
                                  " Feature" + h.FeatureReportLength);
                    sb.AppendLine("    路径  : " + h.Path);
                    sb.AppendLine();
                }

                string path = Path.Combine(Path.GetDirectoryName(DeviceConfigStore.ConfigPath()) ?? "", "hid-diag.txt");
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
                MessageBox.Show("诊断信息已导出到：\r\n" + path +
                                "\r\n\r\n请把该文件发我，我据此判断 X7 的正确接口与读取方式。", "导出成功");
            }
            catch (Exception ex)
            {
                MessageBox.Show("导出失败：" + ex.Message);
            }
        }

        /// <summary>
        /// 不知道设备命令时，在同厂商 report id 0x55 下遍历命令字节，找出能返回
        /// 稳定 0-100 值的“报文 + 电量索引”。后台线程运行，避免卡住界面。
        /// 探测结果仍需对照真实电量核对，无法保证每个候选都是电量字段。
        /// </summary>
        private volatile bool _probing;

        private void ProbeCommand(object sender, EventArgs e)
        {
            if (_probing) return;
            CustomDeviceRule rule;
            try { rule = CollectRule(); }
            catch (Exception ex) { _result.Text = ex.Message; return; }

            DeviceItem sel = _device.SelectedItem as DeviceItem;
            if (sel != null && sel.Device.OutputReportLength <= 0)
            {
                _result.Text = "当前接口只读，无法自动探测。请回到第 1 步选择可写接口。";
                return;
            }

            int frameLen = rule.ReportLength;
            string path = rule.MatchPath;
            _probing = true;
            _result.Text = "自动探测中，约需 20-40 秒，请勿关闭窗口…\r\n";
            SetButtonsEnabled(false);

            System.Threading.Thread worker = new System.Threading.Thread(() =>
            {
                try
                {
                    using (HidSession s = new HidSession(path, frameLen, frameLen, false))
                    {
                        s.Flush(30);
                        int hitCount = 0;

                        // 同厂商 report id 0x55；0x65 优先，再全量遍历。
                        foreach (int cmd in BuildProbeOrder())
                        {
                            if (!_probing) return;

                            byte[] frame = new byte[frameLen];
                            frame[0] = 0x55;
                            frame[1] = (byte)cmd;
                            if (!s.Write(frame, 120)) continue;

                            byte[] reply = ReadOne(s, 1);
                            if (reply == null) continue;

                            // 复验稳定性：再发一次，比较候选位置两次读数。
                            s.Write(frame, 120);
                            byte[] reply2 = ReadOne(s, 1);
                            if (reply2 == null) continue;

                            int max = Math.Min(reply.Length, Math.Min(reply2.Length, 24));
                            for (int idx = 0; idx < max; idx++)
                            {
                                if (reply[idx] == reply2[idx] && reply[idx] <= 100)
                                {
                                    string line = "命中 报文\"55 " + cmd.ToString("X2") +
                                                  "\"  电量索引" + idx + " = " + reply[idx] + "%";
                                    hitCount++;
                                    AppendResult(line + "\r\n");
                                }
                            }
                        }

                        if (hitCount == 0)
                            AppendResult("未找到能返回稳定 0-100 的命令。建议用 USBPcap 抓包官方软件，把报文发我。\r\n");
                        else
                            AppendResult("探测完成，共 " + hitCount + " 条候选。请对照真实电量选出正确的一条，把报文和索引填回上方。\r\n");
                    }
                }
                catch (Exception ex)
                {
                    AppendResult("探测异常：" + ex.Message + "\r\n");
                }
                finally
                {
                    _probing = false;
                    try { BeginInvoke((MethodInvoker)delegate { SetButtonsEnabled(true); }); } catch { }
                }
            });
            worker.IsBackground = true;
            worker.Start();
        }

        private static int[] BuildProbeOrder()
        {
            List<int> order = new List<int> { 0x65 };
            for (int c = 0; c <= 0xFF; c++)
            {
                if (c != 0x65) order.Add(c);
            }
            return order.ToArray();
        }

        private static byte[] ReadOne(HidSession s, int loops)
        {
            for (int i = 0; i < loops; i++)
            {
                byte[] g = s.Read(100);
                if (g != null) return g;
            }
            return null;
        }

        private void AppendResult(string text)
        {
            if (IsDisposed) return;
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    if (_result.TextLength > 6000) _result.Text = "";
                    _result.AppendText(text);
                });
            }
            catch { }
        }

        private void SetButtonsEnabled(bool enabled)
        {
            _test.Enabled = enabled;
            _probe.Enabled = enabled;
            _diag.Enabled = enabled;
            _save.Enabled = enabled;
        }

        private void TestRead(object sender, EventArgs e)
        {
            _result.Text = "";
            CustomDeviceRule rule;
            try
            {
                rule = CollectRule();
            }
            catch (Exception ex)
            {
                _result.Text = ex.Message;
                return;
            }

            try
            {
                int frameLen = rule.ReportLength;
                if (rule.Mode == BatteryQueryMode.Listen)
                {
                    using (HidSession s = new HidSession(rule.MatchPath, frameLen, frameLen, true))
                    {
                        byte[] got = WaitFrame(s, false);
                        ShowResult(rule, got);
                    }
                }
                else
                {
                    byte[] payload = rule.BuildRequest();
                    if (payload.Length == 0)
                    {
                        _result.Text = "查询报文为空";
                        return;
                    }
                    using (HidSession s = new HidSession(rule.MatchPath, frameLen, frameLen, false))
                    {
                        s.Flush(30);
                        byte[] frame = Normalize(payload, frameLen);
                        if (!s.Write(frame, 200))
                        {
                            DeviceItem sel = _device.SelectedItem as DeviceItem;
                            int outLen = sel != null ? sel.Device.OutputReportLength : 0;
                            if (outLen <= 0)
                            {
                                _result.Text = "写入失败：该接口不支持写入（输出长度为 0）。\r\n请回到第 1 步，在下拉中选择同一设备带“输出≥64”的接口；或把获取方式改为“被动监听”。";
                            }
                            else if (payload.Length > frameLen)
                            {
                                _result.Text = "写入失败：查询报文超过报告长度（" + frameLen + "）被截断。请调大报告长度或精简报文。";
                            }
                            else
                            {
                                _result.Text = "写入失败：设备暂不接受数据（可能被其他程序占用或已休眠）。可稍后重试，或改用被动监听。";
                            }
                            return;
                        }
                        byte[] got = WaitFrame(s, true);
                        ShowResult(rule, got);
                    }
                }
            }
            catch (Exception ex)
            {
                _result.Text = "测试失败：" + ex.Message;
            }
        }

        private static byte[] WaitFrame(HidSession s, bool written)
        {
            int loops = written ? 10 : 10;
            for (int i = 0; i < loops; i++)
            {
                byte[] got = s.Read(200);
                if (got != null) return got;
            }
            return null;
        }

        private void ShowResult(CustomDeviceRule rule, byte[] got)
        {
            if (got == null)
            {
                _result.Text = "设备未应答（可能已关机或休眠）。";
                return;
            }

            int n = Math.Min(got.Length, 16);
            byte[] part = new byte[n];
            Array.Copy(got, part, n);

            _result.Text = "原始回复（前 16 字节）：\r\n" + HexUtil.Encode(part) + "\r\n";
            if (got.Length > rule.BatteryIndex)
            {
                int percent = got[rule.BatteryIndex];
                _result.Text += "电量字节[" + rule.BatteryIndex + "] = " + percent +
                                (percent >= 0 && percent <= 100 ? "  →  " + percent + "%" : "（超出 0-100，请核对索引）") + "\r\n";
            }
            else
            {
                _result.Text += "电量字节索引超出回复长度。\r\n";
            }
            if (rule.ChargingIndex >= 0 && got.Length > rule.ChargingIndex)
            {
                _result.Text += "充电字节[" + rule.ChargingIndex + "] = " + got[rule.ChargingIndex] + "\r\n";
            }
        }

        private void SaveDevice(object sender, EventArgs e)
        {
            CustomDeviceRule rule;
            try
            {
                rule = CollectRule();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
                return;
            }

            DeviceItem sel = _device.SelectedItem as DeviceItem;
            if (rule.Mode == BatteryQueryMode.Query && sel != null && sel.Device.OutputReportLength <= 0)
            {
                MessageBox.Show("当前选中的接口为只读，主动查询无法写入。\r\n请在第 1 步选择带“输出≥64”的接口，或把获取方式改为被动监听。");
                return;
            }

            if (rule.Mode == BatteryQueryMode.Query && rule.BuildRequest().Length == 0)
            {
                MessageBox.Show("主动查询模式下查询报文不能为空。");
                return;
            }

            try
            {
                List<CustomDeviceRule> rules = DeviceConfigStore.Load();
                rules.RemoveAll(x => string.Equals(x.MatchPath, rule.MatchPath, StringComparison.OrdinalIgnoreCase));
                rules.Add(rule);
                DeviceConfigStore.Save(rules);

                MessageBox.Show("设备已保存到：\r\n" + DeviceConfigStore.ConfigPath() +
                                "\r\n\r\n下次刷新即自动匹配并显示电量。", "保存成功");
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("保存失败：" + ex.Message);
            }
        }

        private static byte[] Normalize(byte[] data, int frameLen)
        {
            if (data.Length == frameLen) return data;
            if (data.Length > frameLen)
            {
                byte[] t = new byte[frameLen];
                Array.Copy(data, t, frameLen);
                return t;
            }
            byte[] p = new byte[frameLen];
            Array.Copy(data, p, data.Length);
            return p;
        }

        // ------------------------------------------------------------- item

        private sealed class DeviceItem
        {
            public readonly HidDeviceInfo Device;

            public DeviceItem(HidDeviceInfo device)
            {
                Device = device;
            }

            public override string ToString()
            {
                string name = string.IsNullOrEmpty(Device.ProductName) ? "(无产品名)" : Device.ProductName;
                string rw = Device.OutputReportLength > 0 ? "可写" : "只读";
                return Device.VendorId.ToString("X4") + ":" + Device.ProductId.ToString("X4") +
                       "  页" + Device.UsagePage.ToString("X4") +
                       "  " + name +
                       "  输入" + Device.InputReportLength + "/输出" + Device.OutputReportLength +
                       "  " + rw;
            }
        }
    }
}
