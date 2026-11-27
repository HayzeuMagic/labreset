// Lab Reset - find the PCs that are switched on in the network. C# 5 / .NET Framework 4.5+ compatible.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace LabReset
{
    /// <summary>One computer that answered the scan.</summary>
    public class ScanResult
    {
        public string Ip;
        public string Name = "";     // Windows computer name (NetBIOS) or DNS name, if it told us
        public bool Ping, Sharing, Arp;

        /// <summary>File and Printer Sharing (port 445) answered, so Lab Reset can most likely shut it down.</summary>
        public bool LooksLikeLabPc { get { return Sharing; } }

        public string FoundBy
        {
            get
            {
                var l = new List<string>();
                if (Ping) l.Add("ping");
                if (Sharing) l.Add("file sharing");
                if (Arp && !Ping && !Sharing) l.Add("network only (ARP)");
                return string.Join(", ", l);
            }
        }
    }

    /// <summary>
    /// Scans IP addresses for computers that are switched on: ping, File and Printer Sharing (TCP 445),
    /// and on this PC's own subnet ARP, which even finds PCs whose firewall blocks everything else.
    /// </summary>
    public static class NetScan
    {
        public const int MaxHosts = 4096;

        [DllImport("iphlpapi.dll", ExactSpelling = true)]
        static extern int SendARP(uint destIp, uint srcIp, byte[] mac, ref int macLen);

        class Subnet { public uint Network, Mask; public int Bits; }

        static uint ToUint(IPAddress ip)
        {
            byte[] b = ip.GetAddressBytes();
            return ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
        }

        static IPAddress ToIp(uint v)
        {
            return new IPAddress(new[] { (byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v });
        }

        static uint MaskFor(int bits) { return bits <= 0 ? 0 : uint.MaxValue << (32 - bits); }

        /// <summary>Sort key for an IPv4 address string (non-IPs sort last).</summary>
        public static uint SortKey(string ip)
        {
            IPAddress a;
            return IPAddress.TryParse(ip, out a) && a.AddressFamily == AddressFamily.InterNetwork ? ToUint(a) : uint.MaxValue;
        }

        static List<Subnet> localSubnets;

        /// <summary>The IPv4 subnets this PC is connected to. VMware / VirtualBox / Hyper-V adapters are left out.</summary>
        static List<Subnet> Subnets()
        {
            if (localSubnets != null) return localSubnets;
            var all = new List<Subnet>();
            var withGateway = new List<Subnet>();
            try
            {
                foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    string d = (ni.Description + " " + ni.Name).ToLowerInvariant();
                    if (d.Contains("vmware") || d.Contains("virtualbox") || d.Contains("hyper-v") || d.Contains("vethernet")) continue;
                    IPInterfaceProperties props = ni.GetIPProperties();
                    bool gw = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
                    foreach (UnicastIPAddressInformation u in props.UnicastAddresses)
                    {
                        if (u.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        uint ip = ToUint(u.Address);
                        if ((ip >> 16) == 0xA9FE) continue; // 169.254.x.x = no network
                        int bits = 24;
                        if (u.IPv4Mask != null && !u.IPv4Mask.Equals(IPAddress.Any))
                        {
                            uint m = ToUint(u.IPv4Mask);
                            bits = 0;
                            while (bits < 32 && (m & (0x80000000u >> bits)) != 0) bits++;
                        }
                        if (bits < 22) bits = 24; // huge networks: just the 256 addresses around this PC
                        if (bits > 30) continue;
                        var s = new Subnet { Mask = MaskFor(bits), Bits = bits };
                        s.Network = ip & s.Mask;
                        if (all.Any(x => x.Network == s.Network && x.Bits == s.Bits)) continue;
                        all.Add(s);
                        if (gw) withGateway.Add(s);
                    }
                }
            }
            catch { }
            localSubnets = withGateway.Count > 0 ? withGateway : all;
            return localSubnets;
        }

        /// <summary>This PC's network(s), e.g. "10.3.3.0/24". Used as the default in the scan box.</summary>
        public static string DefaultRange()
        {
            List<Subnet> s = Subnets();
            if (s.Count == 0) return PcNames.LabPrefix.Substring(0, PcNames.LabPrefix.LastIndexOf('.') + 1) + "0/24";
            return string.Join(", ", s.Select(x => ToIp(x.Network) + "/" + x.Bits));
        }

        static bool OnLocalSubnet(uint ip)
        {
            return Subnets().Any(s => (ip & s.Mask) == s.Network);
        }

        /// <summary>
        /// Turns what the teacher typed into IP addresses. Accepts 10.3.4.0/24, 10.3.4.1-254, 10.3.4.{1-254}
        /// and single addresses, separated by commas or spaces.
        /// </summary>
        public static List<string> ParseTargets(string text, List<string> errors)
        {
            var result = new List<string>();
            var seen = new HashSet<string>();
            if (string.IsNullOrWhiteSpace(text)) return result;
            foreach (string raw in Regex.Replace(text, @"\{\s*(\d+)\s*-\s*(\d+)\s*\}", "{$1-$2}")
                                        .Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string tok = raw.Trim();
                var ips = new List<string>();
                Match cidr = Regex.Match(tok, @"^(\d{1,3}(?:\.\d{1,3}){3})/(\d{1,2})$");
                IPAddress baseIp;
                if (cidr.Success)
                {
                    int bits = int.Parse(cidr.Groups[2].Value);
                    if (!IPAddress.TryParse(cidr.Groups[1].Value, out baseIp) || bits > 32) { errors.Add(tok + ": not a valid network"); continue; }
                    if (bits < 20) { errors.Add(tok + ": network is too large (use /20 or smaller, max " + MaxHosts + " addresses)"); continue; }
                    uint mask = MaskFor(bits), net = ToUint(baseIp) & mask, last = net | ~mask;
                    uint first = net, end = last;
                    if (bits <= 30) { first = net + 1; end = last - 1; } // skip network and broadcast address
                    for (uint v = first; v <= end && v >= first; v++) ips.Add(ToIp(v).ToString());
                }
                else
                {
                    // 10.3.4.1-254  ->  10.3.4.{1-254}
                    Match dash = Regex.Match(tok, @"^(\d{1,3}\.\d{1,3}\.\d{1,3}\.)(\d{1,3})-(\d{1,3})$");
                    if (dash.Success) tok = dash.Groups[1].Value + "{" + dash.Groups[2].Value + "-" + dash.Groups[3].Value + "}";
                    var errs = new List<string>();
                    foreach (string n in PcNames.Expand(tok, errs))
                    {
                        IPAddress a;
                        if (IPAddress.TryParse(n, out a) && a.AddressFamily == AddressFamily.InterNetwork && Regex.IsMatch(n, @"^\d+(\.\d+){3}$")) ips.Add(n);
                        else errs.Add(n + ": not an IP address (scan needs IP addresses, e.g. 10.3.4.0/24)");
                    }
                    errors.AddRange(errs);
                }
                foreach (string ip in ips)
                {
                    if (result.Count >= MaxHosts) { errors.Add("Only the first " + MaxHosts + " addresses are scanned."); return result; }
                    if (seen.Add(ip)) result.Add(ip);
                }
            }
            return result;
        }

        // ------------------------------------------------------------------ probes
        /// <summary>A probe we stopped waiting for may still fail later; this keeps its error from being reported as unobserved.</summary>
        static void Observe(Task t)
        {
            t.ContinueWith(x => { var ignored = x.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        }

        static async Task<bool> PingAsync(IPAddress ip, int ms)
        {
            try
            {
                using (var p = new Ping())
                    return (await p.SendPingAsync(ip, ms).ConfigureAwait(false)).Status == IPStatus.Success;
            }
            catch { return false; }
        }

        static async Task<bool> PortOpenAsync(IPAddress ip, int port, int ms)
        {
            var c = new TcpClient();
            try
            {
                Task t = c.ConnectAsync(ip, port);
                if (await Task.WhenAny(t, Task.Delay(ms)).ConfigureAwait(false) != t)
                {
                    Observe(t);
                    return false;
                }
                return !t.IsFaulted && c.Connected;
            }
            catch { return false; }
            finally { c.Close(); }
        }

        static bool ArpAnswers(IPAddress ip)
        {
            try
            {
                var mac = new byte[6];
                int len = mac.Length;
                byte[] b = ip.GetAddressBytes();
                uint dest = BitConverter.ToUInt32(b, 0); // SendARP wants network byte order
                return SendARP(dest, 0, mac, ref len) == 0 && len > 0 && mac.Any(x => x != 0);
            }
            catch { return false; }
        }

        /// <summary>Asks the PC for its Windows computer name (NetBIOS name query, UDP 137). Returns null if it doesn't answer.</summary>
        static async Task<string> NetBiosNameAsync(IPAddress ip, int ms)
        {
            var req = new byte[50];
            req[0] = 0x4C; req[1] = 0x52;  // transaction id
            req[5] = 1;                     // 1 question
            req[12] = 0x20;                 // name length 32: "*" padded with zeros, half-byte encoded
            req[13] = (byte)'C'; req[14] = (byte)'K';
            for (int i = 15; i < 45; i++) req[i] = (byte)'A';
            req[46] = 0x00; req[47] = 0x21; // type NBSTAT
            req[48] = 0x00; req[49] = 0x01; // class IN
            var udp = new UdpClient();
            try
            {
                await udp.SendAsync(req, req.Length, new IPEndPoint(ip, 137)).ConfigureAwait(false);
                Task<UdpReceiveResult> rt = udp.ReceiveAsync();
                if (await Task.WhenAny(rt, Task.Delay(ms)).ConfigureAwait(false) != rt)
                {
                    Observe(rt);
                    return null;
                }
                byte[] r = rt.Result.Buffer;
                // header (12) + name (labels) + type, class (4) + ttl (4) + length (2), then the name table
                int p = 12;
                while (p < r.Length && r[p] != 0) p += r[p] + 1;
                p += 1 + 10;
                if (p >= r.Length) return null;
                int count = r[p++];
                for (int i = 0; i < count && p + 18 <= r.Length; i++, p += 18)
                {
                    byte suffix = r[p + 15];
                    bool group = (r[p + 16] & 0x80) != 0;
                    if (suffix == 0x00 && !group) return Encoding.ASCII.GetString(r, p, 15).Trim();
                }
                return null;
            }
            catch { return null; }
            finally { udp.Close(); }
        }

        static async Task<string> DnsNameAsync(IPAddress ip, int ms)
        {
            try
            {
                Task<IPHostEntry> t = Dns.GetHostEntryAsync(ip);
                if (await Task.WhenAny(t, Task.Delay(ms)).ConfigureAwait(false) != t)
                {
                    Observe(t);
                    return null;
                }
                string n = t.Result.HostName;
                return string.IsNullOrEmpty(n) || n == ip.ToString() ? null : n;
            }
            catch { return null; }
        }

        static async Task<ScanResult> ProbeAsync(string ipText)
        {
            IPAddress ip = IPAddress.Parse(ipText);
            Task<bool> ping = PingAsync(ip, 1000), smb = PortOpenAsync(ip, 445, 1000);
            await Task.WhenAll(ping, smb).ConfigureAwait(false);
            var r = new ScanResult { Ip = ipText, Ping = ping.Result, Sharing = smb.Result };
            if (!r.Ping && !r.Sharing && OnLocalSubnet(ToUint(ip)))
                r.Arp = await Task.Run(() => ArpAnswers(ip)).ConfigureAwait(false);
            if (!r.Ping && !r.Sharing && !r.Arp) return null;
            r.Name = await NetBiosNameAsync(ip, 1000).ConfigureAwait(false) ?? await DnsNameAsync(ip, 1500).ConfigureAwait(false) ?? "";
            return r;
        }

        /// <summary>
        /// Scans the addresses, many at a time. found is called for every computer that answers,
        /// done after every address (answered or not). Both are called on worker threads.
        /// </summary>
        public static async Task Scan(List<string> ips, CancellationToken ct, Action<ScanResult> found, Action done)
        {
            // ARP waits a few seconds per missing PC on a pool thread; make sure there are enough threads
            int w, io;
            ThreadPool.GetMinThreads(out w, out io);
            if (w < 80) ThreadPool.SetMinThreads(80, io);

            using (var gate = new SemaphoreSlim(64))
            {
                var tasks = new List<Task>();
                foreach (string ip in ips)
                {
                    if (PcNames.IsLocal(ip)) { done(); continue; }
                    try { await gate.WaitAsync(ct); }
                    catch (OperationCanceledException) { break; }
                    string target = ip;
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            ScanResult r = await ProbeAsync(target).ConfigureAwait(false);
                            if (r != null && !ct.IsCancellationRequested) found(r);
                        }
                        catch { }
                        finally { gate.Release(); done(); }
                    }));
                }
                await Task.WhenAll(tasks);
            }
            ct.ThrowIfCancellationRequested();
        }
    }
}
