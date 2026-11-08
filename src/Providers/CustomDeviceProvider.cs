using System;
using System.Collections.Generic;
using Voltide.Core;
using Voltide.Interop;

namespace Voltide.Providers
{
    /// <summary>
    /// 用户自定义设备的通用适配器。
    ///
    /// 不含任何硬编码协议：设备匹配规则与电量报文全部来自 devices.json
    /// （由“添加设备”向导生成）。支持两种获取方式：
    ///
    ///   Query（主动查询）：打开可读写会话，发送规则里的十六进制报文，
    ///                     读取中断 IN 回复，按 BatteryIndex 取电量百分比。
    ///   Listen（被动监听）：以只读会话读取设备主动上报的输入报告，不注入任何字节。
    ///
    /// 性能：每轮刷新只在 Discover 时读一次配置、枚举由 DeviceManager 统一传入；
    /// Read 复用 Discover 建立的 path 索引，不再重复读文件或重复枚举设备树。
    /// </summary>
    public class CustomDeviceProvider : IDeviceProvider
    {
        public string Name { get { return "自定义设备"; } }

        // 单轮刷新内的缓存，由 Discover 填充、Read 读取。DeviceManager 对刷新串行化，
        // 二者在同一后台线程顺序执行，无需加锁。
        private Dictionary<string, HidDeviceInfo> _capsByPath = new Dictionary<string, HidDeviceInfo>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, CustomDeviceRule> _ruleByPath = new Dictionary<string, CustomDeviceRule>(StringComparer.OrdinalIgnoreCase);

        public List<DeviceInfo> Discover(List<HidDeviceInfo> catalog)
        {
            // 重建本轮缓存，避免上一轮的设备/规则残留。
            _capsByPath = new Dictionary<string, HidDeviceInfo>(catalog.Count, StringComparer.OrdinalIgnoreCase);
            _ruleByPath = new Dictionary<string, CustomDeviceRule>(StringComparer.OrdinalIgnoreCase);

            foreach (HidDeviceInfo h in catalog)
            {
                if (!string.IsNullOrEmpty(h.Path)) _capsByPath[h.Path] = h;
            }

            List<DeviceInfo> found = new List<DeviceInfo>();
            List<CustomDeviceRule> rules = DeviceConfigStore.Load();

            for (int r = 0; r < rules.Count; r++)
            {
                CustomDeviceRule rule = rules[r];
                HidDeviceInfo match = FindMatch(rule, catalog);
                if (match == null) continue;

                // 关联规则与实际接口，供 Read 直接取用。
                _ruleByPath[match.Path] = rule;

                DeviceInfo d = new DeviceInfo();
                d.Id = "custom-" + r + "-" + rule.VendorId.ToString("X4") + rule.ProductId.ToString("X4");
                d.Brand = "自定义";
                d.Model = rule.Name;
                d.Name = rule.Name;
                d.ShortModel = rule.Name;
                d.Kind = rule.Kind;
                d.VendorId = match.VendorId;
                d.ProductId = match.ProductId;
                d.HidPath = match.Path;
                d.ProviderName = Name;

                found.Add(d);
            }

            return found;
        }

        public DeviceReading Read(DeviceInfo device)
        {
            DeviceReading r = new DeviceReading();
            r.UpdatedAt = DateTime.Now;
            r.Online = false;
            r.Level = BatteryLevel.Unknown;
            r.Detail = "自定义设备";

            CustomDeviceRule rule;
            if (!_ruleByPath.TryGetValue(device.HidPath, out rule) || rule == null)
            {
                // 兜底：缓存未命中（理论上不会发生），直接从配置按 VID/PID 找一次。
                rule = FindRuleFallback(device);
                if (rule == null)
                {
                    r.Error = "规则缺失";
                    return r;
                }
            }

            int frameLen = rule.ReportLength;
            HidDeviceInfo caps;
            if (_capsByPath.TryGetValue(device.HidPath, out caps) && caps != null)
            {
                frameLen = Math.Max(frameLen, Math.Max(caps.InputReportLength, caps.OutputReportLength));
            }

            try
            {
                if (rule.Mode == BatteryQueryMode.Listen)
                {
                    ReadPassive(rule, device, frameLen, r);
                }
                else
                {
                    ReadActive(rule, device, frameLen, r);
                }
            }
            catch (Exception ex)
            {
                r.Sleeping = true;
                r.Error = "设备未应答（可能已关机或休眠）";
                r.Detail = ex.Message;
            }

            return r;
        }

        // ------------------------------------------------------------- active

        private static void ReadActive(CustomDeviceRule rule, DeviceInfo device, int frameLen, DeviceReading r)
        {
            using (HidSession session = new HidSession(device.HidPath, frameLen, frameLen, false))
            {
                session.Flush(40);

                byte[] request = NormalizeFrame(rule.BuildRequest(), frameLen);
                if (request.Length == 0)
                {
                    r.Error = "查询报文为空";
                    return;
                }

                byte[] reply = null;
                for (int attempt = 0; attempt < 2 && reply == null; attempt++)
                {
                    if (!session.Write(request, 200)) continue;

                    for (int i = 0; i < 10; i++)
                    {
                        byte[] got = session.Read(200);
                        if (got == null) break;
                        if (got.Length <= rule.BatteryIndex) continue;
                        reply = got;
                        break;
                    }
                }

                if (reply == null)
                {
                    r.Sleeping = true;
                    r.Error = "设备未应答（可能已关机或休眠）";
                    return;
                }

                ApplyReply(rule, reply, r);
            }
        }

        private static void ReadPassive(CustomDeviceRule rule, DeviceInfo device, int frameLen, DeviceReading r)
        {
            using (HidSession session = new HidSession(device.HidPath, frameLen, frameLen, true))
            {
                byte[] reply = null;
                // 被动等待最多约 2 秒，读取设备主动上报的报告。
                for (int i = 0; i < 10; i++)
                {
                    byte[] got = session.Read(200);
                    if (got != null && got.Length > rule.BatteryIndex)
                    {
                        reply = got;
                        break;
                    }
                }

                if (reply == null)
                {
                    r.Sleeping = true;
                    r.Error = "暂未收到设备上报";
                    return;
                }

                ApplyReply(rule, reply, r);
            }
        }

        private static void ApplyReply(CustomDeviceRule rule, byte[] reply, DeviceReading r)
        {
            int percent = reply[rule.BatteryIndex];
            if (percent < 0 || percent > 100)
            {
                r.Online = true;
                r.Error = "读数异常（电量字节超出 0-100）";
                r.Raw = SafeHex(reply, 8);
                return;
            }

            r.Raw = SafeHex(reply, 8);
            r.Percent = percent;
            r.Level = BatteryMath.FromPercent(percent);
            r.Online = true;

            if (rule.ChargingIndex >= 0 && reply.Length > rule.ChargingIndex)
            {
                r.Charging = reply[rule.ChargingIndex] != 0;
                r.ChargingKnown = true;
            }
            else
            {
                r.ChargingKnown = false;
            }
        }

        // ------------------------------------------------------------- lookup

        private static HidDeviceInfo FindMatch(CustomDeviceRule rule, List<HidDeviceInfo> catalog)
        {
            HidDeviceInfo best = null;
            int bestScore = int.MinValue;

            foreach (HidDeviceInfo h in catalog)
            {
                if (!string.IsNullOrEmpty(rule.MatchPath))
                {
                    if (!string.Equals(h.Path, rule.MatchPath, StringComparison.OrdinalIgnoreCase)) continue;
                }
                else
                {
                    if (h.VendorId != rule.VendorId || h.ProductId != rule.ProductId) continue;
                    if (h.InputReportLength < rule.ReportLength) continue;
                }

                // 优先选择可读写、报告长度最贴近规则的接口。
                int score = h.InputReportLength + h.OutputReportLength;
                if (h.OutputReportLength > 0) score += 1000;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = h;
                }
            }
            return best;
        }

        private static CustomDeviceRule FindRuleFallback(DeviceInfo device)
        {
            foreach (CustomDeviceRule rule in DeviceConfigStore.Load())
            {
                if (rule.VendorId == device.VendorId && rule.ProductId == device.ProductId)
                {
                    if (string.IsNullOrEmpty(rule.MatchPath) ||
                        string.Equals(rule.MatchPath, device.HidPath, StringComparison.OrdinalIgnoreCase))
                        return rule;
                }
            }
            return null;
        }

        private static byte[] NormalizeFrame(byte[] request, int frameLen)
        {
            if (request.Length == frameLen) return request;
            if (request.Length > frameLen)
            {
                byte[] trimmed = new byte[frameLen];
                Array.Copy(request, trimmed, frameLen);
                return trimmed;
            }
            byte[] padded = new byte[frameLen];
            Array.Copy(request, padded, request.Length);
            return padded;
        }

        private static string SafeHex(byte[] data, int max)
        {
            int n = Math.Min(data.Length, max);
            byte[] part = new byte[n];
            Array.Copy(data, part, n);
            return HexUtil.Encode(part);
        }
    }
}
