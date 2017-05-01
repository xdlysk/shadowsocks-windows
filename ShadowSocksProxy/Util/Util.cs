using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using ShadowSocksProxy.Controller;

namespace ShadowSocksProxy.Util
{
    public struct BandwidthScaleInfo
    {
        public float value;
        public string unitName;
        public long unit;

        public BandwidthScaleInfo(float value, string unitName, long unit)
        {
            this.value = value;
            this.unitName = unitName;
            this.unit = unit;
        }
    }

    public static class Utils
    {
        private static string _tempPath;

        // return path to store temporary files
        public static string GetTempPath()
        {
            if (_tempPath == null)
                try
                {
                    Directory.CreateDirectory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ss_win_temp"));
                    // don't use "/", it will fail when we call explorer /select xxx/ss_win_temp\xxx.log
                    _tempPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ss_win_temp");
                }
                catch (Exception e)
                {
                    Logging.Error(e);
                    throw;
                }
            return _tempPath;
        }

        // return a full path with filename combined which pointed to the temporary directory
        public static string GetTempPath(string filename)
        {
            return Path.Combine(GetTempPath(), filename);
        }

        public static void ReleaseMemory(bool removePages)
        {
            // release any unused pages
            // making the numbers look good in task manager
            // this is totally nonsense in programming
            // but good for those users who care
            // making them happier with their everyday life
            // which is part of user experience
            GC.Collect(GC.MaxGeneration);
            GC.WaitForPendingFinalizers();
            if (removePages)
                SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle,
                    (UIntPtr) 0xFFFFFFFF,
                    (UIntPtr) 0xFFFFFFFF);
        }

        public static string UnGzip(byte[] buf)
        {
            var buffer = new byte[1024];
            int n;
            using (var sb = new MemoryStream())
            {
                using (var input = new GZipStream(new MemoryStream(buf),
                    CompressionMode.Decompress,
                    false))
                {
                    while ((n = input.Read(buffer, 0, buffer.Length)) > 0)
                        sb.Write(buffer, 0, n);
                }
                return Encoding.UTF8.GetString(sb.ToArray());
            }
        }

        public static string FormatBandwidth(long n)
        {
            var result = GetBandwidthScale(n);
            return $"{result.value:0.##}{result.unitName}";
        }

        public static string FormatBytes(long bytes)
        {
            const long K = 1024L;
            const long M = K*1024L;
            const long G = M*1024L;
            const long T = G*1024L;
            const long P = T*1024L;
            const long E = P*1024L;

            if (bytes >= P*990)
                return (bytes/(double) E).ToString("F5") + "EiB";
            if (bytes >= T*990)
                return (bytes/(double) P).ToString("F5") + "PiB";
            if (bytes >= G*990)
                return (bytes/(double) T).ToString("F5") + "TiB";
            if (bytes >= M*990)
                return (bytes/(double) G).ToString("F4") + "GiB";
            if (bytes >= M*100)
                return (bytes/(double) M).ToString("F1") + "MiB";
            if (bytes >= M*10)
                return (bytes/(double) M).ToString("F2") + "MiB";
            if (bytes >= K*990)
                return (bytes/(double) M).ToString("F3") + "MiB";
            if (bytes > K*2)
                return (bytes/(double) K).ToString("F1") + "KiB";
            return bytes + "B";
        }

        /// <summary>
        ///     Return scaled bandwidth
        /// </summary>
        /// <param name="n">Raw bandwidth</param>
        /// <returns>
        ///     The BandwidthScaleInfo struct
        /// </returns>
        public static BandwidthScaleInfo GetBandwidthScale(long n)
        {
            long scale = 1;
            float f = n;
            var unit = "B";
            if (f > 1024)
            {
                f = f/1024;
                scale <<= 10;
                unit = "KiB";
            }
            if (f > 1024)
            {
                f = f/1024;
                scale <<= 10;
                unit = "MiB";
            }
            if (f > 1024)
            {
                f = f/1024;
                scale <<= 10;
                unit = "GiB";
            }
            if (f > 1024)
            {
                f = f/1024;
                scale <<= 10;
                unit = "TiB";
            }
            return new BandwidthScaleInfo(f, unit, scale);
        }

        public static RegistryKey OpenRegKey(string name, bool writable, RegistryHive hive = RegistryHive.CurrentUser)
        {
            // we are building x86 binary for both x86 and x64, which will
            // cause problem when opening registry key
            // detect operating system instead of CPU
            if (name.IsNullOrEmpty()) throw new ArgumentException(nameof(name));
            try
            {
                var userKey = RegistryKey.OpenBaseKey(hive,
                        Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Registry32)
                    .OpenSubKey(name, writable);
                return userKey;
            }
            catch (ArgumentException ae)
            {
                Logging.LogUsefulException(ae);
                return null;
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                return null;
            }
        }

        public static bool IsWinVistaOrHigher()
        {
            return Environment.OSVersion.Version.Major > 5;
        }

        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessWorkingSetSize(IntPtr process,
            UIntPtr minimumWorkingSetSize, UIntPtr maximumWorkingSetSize);


        // See: https://msdn.microsoft.com/en-us/library/hh925568(v=vs.110).aspx
        public static bool IsSupportedRuntimeVersion()
        {
            /*
             * +-----------------------------------------------------------------+----------------------------+
             * | Version                                                         | Value of the Release DWORD |
             * +-----------------------------------------------------------------+----------------------------+
             * | .NET Framework 4.6.2 installed on Windows 10 Anniversary Update | 394802                     |
             * | .NET Framework 4.6.2 installed on all other Windows OS versions | 394806                     |
             * +-----------------------------------------------------------------+----------------------------+
             */
            const int minSupportedRelease = 394802;

            const string subkey = @"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full\";
            using (var ndpKey = OpenRegKey(subkey, false, RegistryHive.LocalMachine))
            {
                if (ndpKey?.GetValue("Release") != null)
                {
                    var releaseKey = (int) ndpKey.GetValue("Release");

                    if (releaseKey >= minSupportedRelease)
                        return true;
                }
            }
            return false;
        }
    }
}