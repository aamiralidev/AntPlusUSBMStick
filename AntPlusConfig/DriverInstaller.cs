using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System;
using System.Runtime.InteropServices;
using System.Management;


namespace AntPlusConfig
{
    internal class DriverInstaller
    {
        // P/Invoke setup for SetupCopyOEMInf
        [DllImport("Setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] 
        public static extern bool SetupCopyOEMInf( 
            string SourceInfFileName, 
            string OEMSourceMediaLocation, 
            int OEMSourceMediaType, 
            int CopyStyle, 
            string DestinationInfFileName, 
            int DestinationInfFileNameSize, 
            int RequiredSize, 
            string DestinationInfFileNameComponent ); 
        // P/Invoke setup for DiInstallDriver
        [DllImport("newdev.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern bool DiInstallDriver( 
            IntPtr hwndParent, 
            string FullInfPath, 
            int Flags, 
            out bool NeedReboot ); 
        // Constants for SetupCopyOEMInf and DiInstallDriver - adjust as necessary
        const int DIIRFLAG_FORCE_INF = 0x00000002; 
        const int SPOST_NONE = 0; 
        const int SP_COPY_NEWER_OR_SAME = 0x00000004;
        public static void InstallDriver(string infPath)
        {
            bool result = SetupCopyOEMInf(infPath, null, SPOST_NONE, SP_COPY_NEWER_OR_SAME, null, 0, 0, null);
            if (!result)
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "SetupCopyOEMInf failed.");
            }
            bool needReboot;
            result = DiInstallDriver(IntPtr.Zero, infPath, DIIRFLAG_FORCE_INF, out needReboot);
            if (!result)
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "DiInstallDriver failed.");
            }
        }

        public static bool IsDriverInstalled(string hardwareId)
        {

            string query = $"SELECT * FROM Win32_PnPEntity WHERE DeviceID LIKE '%{hardwareId}%'";
            using (var searcher = new ManagementObjectSearcher(query))
            {
                using (var collection = searcher.Get())
                {
                    if (collection.Count > 0)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
