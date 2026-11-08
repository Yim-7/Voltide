using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Voltide.Core
{
    /// <summary>设备电量的获取方式。</summary>
    public enum BatteryQueryMode
    {
        /// <summary>主动发送查询报文并读取回复（最准确，推荐）。</summary>
        Query = 0,

        /// <summary>不发送任何数据，只读取设备主动上报的输入报告。</summary>
        Listen = 1
    }

    /// <summary>
    /// 一条用户自定义设备规则。由“添加设备”向导生成，持久化到 devices.json，
    /// 供 <see cref="Voltide.Providers.CustomDeviceProvider"/> 使用。
    /// </summary>
    public class CustomDeviceRule
    {
        public string Name = "我的设备";
        public ushort VendorId;
        public ushort ProductId;

        /// <summary>报告长度（含首字节 Report ID），常见为 64。</summary>
        public int ReportLength = 64;

        public BatteryQueryMode Mode = BatteryQueryMode.Query;

        /// <summary>主动查询时发送的报文（十六进制，可含空格），如 55 65 00 00。</summary>
        public string RequestHex = "";

        /// <summary>电量百分比所在的字节索引（从 0 开始）。</summary>
        public int BatteryIndex = 3;

        /// <summary>充电状态字节索引；-1 表示不读取。</summary>
        public int ChargingIndex = -1;

        /// <summary>设备类别，默认耳机。</summary>
        public DeviceKind Kind = DeviceKind.Headset;

        /// <summary>可选：精确匹配的 HID 路径；为空时按 VID/PID 匹配。</summary>
        public string MatchPath = "";

        /// <summary>把十六进制字符串解析为字节；忽略空格与常见分隔符。</summary>
        public byte[] BuildRequest()
        {
            return HexUtil.Decode(RequestHex);
        }
    }

    /// <summary>十六进制工具。</summary>
    public static class HexUtil
    {
        public static byte[] Decode(string text)
        {
            if (string.IsNullOrEmpty(text)) return new byte[0];

            List<byte> bytes = new List<byte>();
            int hi = -1;

            foreach (char raw in text)
            {
                int v = HexDigit(raw);
                if (v < 0) continue;

                if (hi < 0) hi = v;
                else
                {
                    bytes.Add((byte)(hi * 16 + v));
                    hi = -1;
                }
            }
            return bytes.ToArray();
        }

        public static string Encode(byte[] data)
        {
            if (data == null || data.Length == 0) return "";
            StringBuilder sb = new StringBuilder(data.Length * 3);
            for (int i = 0; i < data.Length; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(data[i].ToString("X2", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        private static int HexDigit(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }
    }

    /// <summary>加载并保存 devices.json。</summary>
    public static class DeviceConfigStore
    {
        public const string FileName = "devices.json";

        public static string ConfigPath()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            if (string.IsNullOrEmpty(dir)) dir = Environment.CurrentDirectory;
            return Path.Combine(dir, FileName);
        }

        // 配置缓存：以文件的最后写入时间（UTC）为版本，未变更就复用解析结果，
        // 避免每次刷新都读盘并重新解析 JSON。Load 返回副本，调用方的增删不影响缓存。
        private static readonly object _cacheLock = new object();
        private static List<CustomDeviceRule> _cache;
        private static DateTime _cacheMtime = DateTime.MinValue;
        private static bool _cacheReady;

        public static List<CustomDeviceRule> Load()
        {
            lock (_cacheLock)
            {
                string path = ConfigPath();
                bool exists = File.Exists(path);
                DateTime mtime = exists ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;

                if (_cacheReady && mtime == _cacheMtime)
                    return new List<CustomDeviceRule>(_cache);

                List<CustomDeviceRule> rules = new List<CustomDeviceRule>();
                if (exists)
                {
                    try
                    {
                        string json = File.ReadAllText(path, Encoding.UTF8);
                        List<object> arr = MiniJson.Parse(json) as List<object>;
                        if (arr != null)
                        {
                            foreach (object item in arr)
                            {
                                Dictionary<string, object> o = item as Dictionary<string, object>;
                                if (o != null) rules.Add(ReadRule(o));
                            }
                        }
                    }
                    catch
                    {
                        // 配置损坏时返回空列表，保证主程序仍可启动。
                        rules = new List<CustomDeviceRule>();
                    }
                }

                _cache = rules;
                _cacheMtime = mtime;
                _cacheReady = true;
                return new List<CustomDeviceRule>(rules);
            }
        }

        public static void Save(List<CustomDeviceRule> rules)
        {
            lock (_cacheLock)
            {
                List<object> arr = new List<object>();
                foreach (CustomDeviceRule r in rules) arr.Add(WriteRule(r));

                string json = MiniJson.Serialize(arr, true);
                File.WriteAllText(ConfigPath(), json, new UTF8Encoding(false));

                // 写入后文件 mtime 已变，令缓存失效，下次 Load 按新版本重建。
                _cacheReady = false;
            }
        }

        private static CustomDeviceRule ReadRule(Dictionary<string, object> o)
        {
            CustomDeviceRule r = new CustomDeviceRule();
            r.Name = GetString(o, "Name", r.Name);
            r.VendorId = (ushort)GetInt(o, "VendorId", 0);
            r.ProductId = (ushort)GetInt(o, "ProductId", 0);
            r.ReportLength = GetInt(o, "ReportLength", r.ReportLength);
            r.Mode = GetInt(o, "Mode", 0) == 1 ? BatteryQueryMode.Listen : BatteryQueryMode.Query;
            r.RequestHex = GetString(o, "RequestHex", "");
            r.BatteryIndex = GetInt(o, "BatteryIndex", r.BatteryIndex);
            r.ChargingIndex = GetInt(o, "ChargingIndex", r.ChargingIndex);
            r.Kind = (DeviceKind)GetInt(o, "Kind", (int)DeviceKind.Headset);
            r.MatchPath = GetString(o, "MatchPath", "");
            return r;
        }

        private static Dictionary<string, object> WriteRule(CustomDeviceRule r)
        {
            return new Dictionary<string, object>
            {
                { "Name", r.Name },
                { "VendorId", (int)r.VendorId },
                { "ProductId", (int)r.ProductId },
                { "ReportLength", r.ReportLength },
                { "Mode", (int)r.Mode },
                { "RequestHex", r.RequestHex },
                { "BatteryIndex", r.BatteryIndex },
                { "ChargingIndex", r.ChargingIndex },
                { "Kind", (int)r.Kind },
                { "MatchPath", r.MatchPath ?? "" }
            };
        }

        private static string GetString(Dictionary<string, object> o, string key, string fallback)
        {
            object v;
            return o.TryGetValue(key, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : fallback;
        }

        private static int GetInt(Dictionary<string, object> o, string key, int fallback)
        {
            object v;
            if (!o.TryGetValue(key, out v) || v == null) return fallback;
            try { return Convert.ToInt32(v, CultureInfo.InvariantCulture); }
            catch { return fallback; }
        }
    }

    /// <summary>
    /// 极简 JSON 解析/序列化，仅支持对象、数组、字符串、数字、布尔与 null，
    /// 避免为配置文件额外引用 System.Web.Extensions。
    /// </summary>
    internal static class MiniJson
    {
        public static object Parse(string json)
        {
            int i = 0;
            object value;
            if (!ParseValue(json, ref i, out value)) throw new FormatException("无效 JSON");
            return value;
        }

        public static string Serialize(object value, bool pretty)
        {
            StringBuilder sb = new StringBuilder();
            WriteValue(sb, value, pretty ? 0 : -1, 0);
            return sb.ToString();
        }

        // ------------------------------------------------------------- parse

        private static bool ParseValue(string s, ref int i, out object value)
        {
            SkipWs(s, ref i);
            value = null;
            if (i >= s.Length) return false;

            char c = s[i];
            if (c == '{') return ParseObject(s, ref i, out value);
            if (c == '[') return ParseArray(s, ref i, out value);
            if (c == '"') { string str; if (ParseString(s, ref i, out str)) { value = str; return true; } return false; }
            if (c == 't' || c == 'f') return ParseBool(s, ref i, out value);
            if (c == 'n') return ParseNull(s, ref i, out value);
            return ParseNumber(s, ref i, out value);
        }

        private static bool ParseObject(string s, ref int i, out object value)
        {
            Dictionary<string, object> obj = new Dictionary<string, object>();
            i++;
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; value = obj; return true; }

            while (true)
            {
                SkipWs(s, ref i);
                string key;
                if (!ParseString(s, ref i, out key)) { value = null; return false; }
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') { value = null; return false; }
                i++;
                object child;
                if (!ParseValue(s, ref i, out child)) { value = null; return false; }
                obj[key] = child;

                SkipWs(s, ref i);
                if (i >= s.Length) { value = null; return false; }
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; break; }
                value = null;
                return false;
            }
            value = obj;
            return true;
        }

        private static bool ParseArray(string s, ref int i, out object value)
        {
            List<object> arr = new List<object>();
            i++;
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; value = arr; return true; }

            while (true)
            {
                object child;
                if (!ParseValue(s, ref i, out child)) { value = null; return false; }
                arr.Add(child);
                SkipWs(s, ref i);
                if (i >= s.Length) { value = null; return false; }
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; break; }
                value = null;
                return false;
            }
            value = arr;
            return true;
        }

        private static bool ParseString(string s, ref int i, out string value)
        {
            StringBuilder sb = new StringBuilder();
            i++;
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') { value = sb.ToString(); return true; }
                if (c == '\\')
                {
                    if (i >= s.Length) break;
                    char e = s[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 > s.Length) { value = null; return false; }
                            int code = int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                            sb.Append((char)code);
                            i += 4;
                            break;
                        default: sb.Append(e); break;
                    }
                }
                else sb.Append(c);
            }
            value = null;
            return false;
        }

        private static bool ParseNumber(string s, ref int i, out object value)
        {
            int start = i;
            if (s[i] == '-') i++;
            bool isFloat = false;
            while (i < s.Length)
            {
                char c = s[i];
                if (c >= '0' && c <= '9') i++;
                else if (c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-') { isFloat = true; i++; }
                else break;
            }
            string num = s.Substring(start, i - start);
            if (isFloat)
            {
                double d;
                if (double.TryParse(num, NumberStyles.Any, CultureInfo.InvariantCulture, out d)) { value = d; return true; }
            }
            else
            {
                long l;
                if (long.TryParse(num, NumberStyles.Integer, CultureInfo.InvariantCulture, out l)) { value = l; return true; }
            }
            value = null;
            return false;
        }

        private static bool ParseBool(string s, ref int i, out object value)
        {
            if (s.Substring(i).StartsWith("true", StringComparison.Ordinal)) { i += 4; value = true; return true; }
            if (s.Substring(i).StartsWith("false", StringComparison.Ordinal)) { i += 5; value = false; return true; }
            value = null;
            return false;
        }

        private static bool ParseNull(string s, ref int i, out object value)
        {
            if (s.Substring(i).StartsWith("null", StringComparison.Ordinal)) { i += 4; value = null; return true; }
            value = null;
            return false;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        // ------------------------------------------------------------ write

        private static void WriteValue(StringBuilder sb, object value, int indent, int depth)
        {
            bool pretty = indent >= 0;
            string pad = pretty ? new string(' ', indent * (depth + 1)) : "";
            string closePad = pretty ? new string(' ', indent * depth) : "";
            string nl = pretty ? "\n" : "";

            if (value == null) { sb.Append("null"); return; }

            string str = value as string;
            if (str != null) { WriteString(sb, str); return; }

            if (value is bool) { sb.Append((bool)value ? "true" : "false"); return; }

            if (value is int || value is long || value is short || value is byte
                || value is uint || value is ushort || value is ulong || value is sbyte)
            {
                sb.Append(Convert.ToInt64(value, CultureInfo.InvariantCulture)
                          .ToString(CultureInfo.InvariantCulture));
                return;
            }
            if (value is float || value is double || value is decimal)
            {
                sb.Append(Convert.ToDouble(value, CultureInfo.InvariantCulture)
                          .ToString("R", CultureInfo.InvariantCulture));
                return;
            }

            Dictionary<string, object> obj = value as Dictionary<string, object>;
            if (obj != null)
            {
                sb.Append('{').Append(nl);
                int n = 0;
                foreach (KeyValuePair<string, object> kv in obj)
                {
                    if (n > 0) sb.Append(',').Append(nl);
                    sb.Append(pad);
                    WriteString(sb, kv.Key);
                    sb.Append(pretty ? ": " : ":");
                    WriteValue(sb, kv.Value, indent, depth + 1);
                    n++;
                }
                sb.Append(nl).Append(closePad).Append('}');
                return;
            }

            List<object> arr = value as List<object>;
            if (arr != null)
            {
                sb.Append('[').Append(nl);
                for (int n = 0; n < arr.Count; n++)
                {
                    if (n > 0) sb.Append(',').Append(nl);
                    sb.Append(pad);
                    WriteValue(sb, arr[n], indent, depth + 1);
                }
                sb.Append(nl).Append(closePad).Append(']');
                return;
            }

            WriteString(sb, Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        private static void WriteString(StringBuilder sb, string value)
        {
            sb.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20)
                        {
                            sb.Append("\\u");
                            sb.Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
