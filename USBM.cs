#nullable disable

using Microsoft.Win32.SafeHandles;
using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace co2_level_exporter
{
    // Decompiled from https://www.tindie.com/products/fero_ke/voltmeter-with-usb-interface/
    sealed class USBM
    {
        internal const uint DIGCF_PRESENT = 2;
        internal const uint DIGCF_DEVICEINTERFACE = 16;
        internal const short FILE_ATTRIBUTE_NORMAL = 128;
        internal const short INVALID_HANDLE_VALUE = -1;
        internal const uint GENERIC_READ = 2147483648;
        internal const uint GENERIC_WRITE = 1073741824;
        internal const uint CREATE_NEW = 1;
        internal const uint CREATE_ALWAYS = 2;
        internal const uint OPEN_EXISTING = 3;
        internal const uint FILE_SHARE_READ = 1;
        internal const uint FILE_SHARE_WRITE = 2;
        internal const uint WM_DEVICECHANGE = 537;
        internal const uint DBT_DEVICEARRIVAL = 32768;
        internal const uint DBT_DEVICEREMOVEPENDING = 32771;
        internal const uint DBT_DEVICEREMOVECOMPLETE = 32772;
        internal const uint DBT_CONFIGCHANGED = 24;
        internal const uint DBT_DEVTYP_DEVICEINTERFACE = 5;
        internal const uint DEVICE_NOTIFY_WINDOW_HANDLE = 0;
        internal const uint ERROR_SUCCESS = 0;
        internal const uint ERROR_NO_MORE_ITEMS = 259;
        internal const uint SPDRP_HARDWAREID = 1;
        private bool AttachedState;
        private SafeFileHandle WriteHandleToUSBDevice;
        private SafeFileHandle ReadHandleToUSBDevice;
        private string DevicePath;
        public float ADCValue;
        private int ADC;
        private int signbit = 131072;
        public USBM.MyDeviceInfo InfoDevice;
        private Guid InterfaceClassGuid = new Guid(1293833650U, (ushort)61807, (ushort)4559, (byte)136, (byte)203, (byte)0, (byte)17, (byte)17, (byte)0, (byte)0, (byte)48);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr SetupDiGetClassDevs(
          ref Guid ClassGuid,
          IntPtr Enumerator,
          IntPtr hwndParent,
          uint Flags);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool SetupDiEnumDeviceInterfaces(
          IntPtr DeviceInfoSet,
          IntPtr DeviceInfoData,
          ref Guid InterfaceClassGuid,
          uint MemberIndex,
          ref USBM.SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool SetupDiEnumDeviceInfo(
          IntPtr DeviceInfoSet,
          uint MemberIndex,
          ref USBM.SP_DEVINFO_DATA DeviceInterfaceData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool SetupDiGetDeviceRegistryProperty(
          IntPtr DeviceInfoSet,
          ref USBM.SP_DEVINFO_DATA DeviceInfoData,
          uint Property,
          ref uint PropertyRegDataType,
          IntPtr PropertyBuffer,
          uint PropertyBufferSize,
          ref uint RequiredSize);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool SetupDiGetDeviceInterfaceDetail(
          IntPtr DeviceInfoSet,
          ref USBM.SP_DEVICE_INTERFACE_DATA DeviceInterfaceData,
          IntPtr DeviceInterfaceDetailData,
          uint DeviceInterfaceDetailDataSize,
          ref uint RequiredSize,
          IntPtr DeviceInfoData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool SetupDiGetDeviceInterfaceDetail(
          IntPtr DeviceInfoSet,
          ref USBM.SP_DEVICE_INTERFACE_DATA DeviceInterfaceData,
          IntPtr DeviceInterfaceDetailData,
          uint DeviceInterfaceDetailDataSize,
          IntPtr RequiredSize,
          IntPtr DeviceInfoData);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr RegisterDeviceNotification(
          IntPtr hRecipient,
          IntPtr NotificationFilter,
          uint Flags);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(
          string lpFileName,
          uint dwDesiredAccess,
          uint dwShareMode,
          IntPtr lpSecurityAttributes,
          uint dwCreationDisposition,
          uint dwFlagsAndAttributes,
          IntPtr hTemplateFile);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool WriteFile(
          SafeFileHandle hFile,
          byte[] lpBuffer,
          uint nNumberOfBytesToWrite,
          ref uint lpNumberOfBytesWritten,
          IntPtr lpOverlapped);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool ReadFile(
          SafeFileHandle hFile,
          IntPtr lpBuffer,
          uint nNumberOfBytesToRead,
          ref uint lpNumberOfBytesRead,
          IntPtr lpOverlapped);

        public bool OpenDevice()
        {
            if (this.CheckIfPresentAndGetUSBDevicePath())
            {
                this.WriteHandleToUSBDevice = USBM.CreateFile(this.DevicePath, 1073741824U, 3U, IntPtr.Zero, 3U, 0U, IntPtr.Zero);
                uint lastWin32Error1 = (uint)Marshal.GetLastWin32Error();
                this.ReadHandleToUSBDevice = USBM.CreateFile(this.DevicePath, 2147483648U, 3U, IntPtr.Zero, 3U, 0U, IntPtr.Zero);
                uint lastWin32Error2 = (uint)Marshal.GetLastWin32Error();
                if (lastWin32Error1 == 0U && lastWin32Error2 == 0U)
                {
                    this.AttachedState = true;
                }
                else
                {
                    this.AttachedState = false;
                    if (lastWin32Error1 == 0U)
                        this.WriteHandleToUSBDevice.Close();
                    if (lastWin32Error2 == 0U)
                        this.ReadHandleToUSBDevice.Close();
                }
            }
            else
            {
                this.AttachedState = false;
            }
            this.InfoDevice = this.GetInfoForDevice();
            return this.AttachedState;
        }

        private bool CheckIfPresentAndGetUSBDevicePath()
        {
            try
            {
                IntPtr zero1 = IntPtr.Zero;
                USBM.SP_DEVICE_INTERFACE_DATA DeviceInterfaceData = new USBM.SP_DEVICE_INTERFACE_DATA();
                USBM.SP_DEVICE_INTERFACE_DETAIL_DATA interfaceDetailData = new USBM.SP_DEVICE_INTERFACE_DETAIL_DATA();
                USBM.SP_DEVINFO_DATA spDevinfoData = new USBM.SP_DEVINFO_DATA();
                uint MemberIndex = 0;
                uint PropertyRegDataType = 0;
                uint RequiredSize1 = 0;
                uint RequiredSize2 = 0;
                uint RequiredSize3 = 0;
                IntPtr zero2 = IntPtr.Zero;
                uint num1 = 0;
                string str = "Vid_04d8&Pid_fc39";
                IntPtr classDevs = USBM.SetupDiGetClassDevs(ref this.InterfaceClassGuid, IntPtr.Zero, IntPtr.Zero, 18U);
                if (!(classDevs != IntPtr.Zero))
                    return false;
                do
                {
                    DeviceInterfaceData.cbSize = (uint)Marshal.SizeOf((object)DeviceInterfaceData);
                    if (USBM.SetupDiEnumDeviceInterfaces(classDevs, IntPtr.Zero, ref this.InterfaceClassGuid, MemberIndex, ref DeviceInterfaceData))
                    {
                        if (Marshal.GetLastWin32Error() == 259)
                        {
                            USBM.SetupDiDestroyDeviceInfoList(classDevs);
                            return false;
                        }
                        spDevinfoData.cbSize = (uint)Marshal.SizeOf((object)spDevinfoData);
                        USBM.SetupDiEnumDeviceInfo(classDevs, MemberIndex, ref spDevinfoData);
                        USBM.SetupDiGetDeviceRegistryProperty(classDevs, ref spDevinfoData, 1U, ref PropertyRegDataType, IntPtr.Zero, 0U, ref RequiredSize1);
                        IntPtr num2 = Marshal.AllocHGlobal((int)RequiredSize1);
                        USBM.SetupDiGetDeviceRegistryProperty(classDevs, ref spDevinfoData, 1U, ref PropertyRegDataType, num2, RequiredSize1, ref RequiredSize2);
                        string stringUni = Marshal.PtrToStringUni(num2);
                        Marshal.FreeHGlobal(num2);
                        string lowerInvariant = stringUni.ToLowerInvariant();
                        str = str.ToLowerInvariant();
                        if (lowerInvariant.Contains(str))
                        {
                            // Just... it just is. I don't get it.
                            interfaceDetailData.cbSize = 8;
                            USBM.SetupDiGetDeviceInterfaceDetail(classDevs, ref DeviceInterfaceData, IntPtr.Zero, 0U, ref RequiredSize3, IntPtr.Zero);
                            IntPtr zero3 = IntPtr.Zero;
                            IntPtr detailDataReal = Marshal.AllocHGlobal((int)RequiredSize3);
                            Marshal.StructureToPtr(interfaceDetailData, detailDataReal, false);
                            if (USBM.SetupDiGetDeviceInterfaceDetail(classDevs, ref DeviceInterfaceData, detailDataReal, RequiredSize3, IntPtr.Zero, IntPtr.Zero))
                            {
                                // Starts right after the 4 byte size.
                                this.DevicePath = Marshal.PtrToStringUni(new IntPtr(detailDataReal.ToInt64() + 4));
                                USBM.SetupDiDestroyDeviceInfoList(classDevs);
                                Marshal.FreeHGlobal(detailDataReal);
                                return true;
                            }
                            Marshal.GetLastWin32Error();
                            USBM.SetupDiDestroyDeviceInfoList(classDevs);
                            Marshal.FreeHGlobal(detailDataReal);
                            return false;
                        }
                        ++MemberIndex;
                        ++num1;
                    }
                    else
                    {
                        uint lastWin32Error = (uint)Marshal.GetLastWin32Error();
                        USBM.SetupDiDestroyDeviceInfoList(classDevs);
                        return false;
                    }
                }
                while (num1 != 10000000U);
                return false;
            }
            catch
            {
                return false;
            }
        }

        public float GetMeasuredValue()
        {
            byte[] lpBuffer = new byte[65];
            byte[] INBuffer = new byte[65];
            uint lpNumberOfBytesWritten = 0;
            uint lpNumberOfBytesRead = 0;
            try
            {
                if (this.AttachedState)
                {
                    lpBuffer[0] = (byte)0;
                    lpBuffer[1] = (byte)55;
                    for (uint index = 2; index < 65U; ++index)
                        lpBuffer[index] = byte.MaxValue;
                    if (USBM.WriteFile(this.WriteHandleToUSBDevice, lpBuffer, 65U, ref lpNumberOfBytesWritten, IntPtr.Zero))
                    {
                        INBuffer[0] = (byte)0;
                        if (this.ReadFileManagedBuffer(this.ReadHandleToUSBDevice, INBuffer, 65U, ref lpNumberOfBytesRead, IntPtr.Zero))
                        {
                            if (INBuffer[1] == (byte)55)
                            {
                                if (this.InfoDevice.UnitVersion == 5)
                                {
                                    if (this.InfoDevice.Subtype == 1)
                                    {
                                        this.ADC = ((int)INBuffer[2] << 16) + ((int)INBuffer[3] << 8) + (int)INBuffer[4];
                                        if ((this.ADC & this.signbit) != 0)
                                            this.ADC -= 16777216;
                                        this.ADC = (int)((double)this.ADC * this.InfoDevice.CalibValue);
                                        this.ADCValue = (float)this.ADC;
                                        this.ADCValue /= 1000f;
                                        this.ADCValue = (float)Math.Round((double)this.ADCValue, 3);
                                        return this.ADCValue;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            return 0.0f;
        }

        internal USBM.MyDeviceInfo GetInfoForDevice()
        {
            byte[] lpBuffer = new byte[65];
            byte[] INBuffer = new byte[65];
            uint lpNumberOfBytesWritten = 0;
            uint lpNumberOfBytesRead = 0;
            uint num1 = 0;
            uint num2 = 0;
            uint num3 = 0;
            uint num4 = 0;
            uint num5 = 0;
            USBM.MyDeviceInfo myDeviceInfo = new USBM.MyDeviceInfo();
            try
            {
                if (this.AttachedState)
                {
                    lpBuffer[0] = (byte)0;
                    lpBuffer[1] = byte.MaxValue;
                    lpBuffer[2] = (byte)55;
                    for (uint index = 3; index < 65U; ++index)
                        lpBuffer[index] = byte.MaxValue;
                    if (USBM.WriteFile(this.WriteHandleToUSBDevice, lpBuffer, 65U, ref lpNumberOfBytesWritten, IntPtr.Zero))
                    {
                        INBuffer[0] = (byte)0;
                        if (this.ReadFileManagedBuffer(this.ReadHandleToUSBDevice, INBuffer, 65U, ref lpNumberOfBytesRead, IntPtr.Zero))
                        {
                            num1 = ((uint)INBuffer[9] << 8) + (uint)INBuffer[8];
                            num2 = ((uint)INBuffer[9] << 8) + (uint)INBuffer[8];
                            num3 = ((uint)INBuffer[17] << 8) + (uint)INBuffer[16];
                            num4 = ((uint)INBuffer[19] << 8) + (uint)INBuffer[18];
                            num5 = ((uint)INBuffer[21] << 8) + (uint)INBuffer[20];
                        }
                    }
                    myDeviceInfo.CalibValue = (double)num1 / 10000.0;
                    myDeviceInfo.CalibValue1 = 1.0 + ((double)num2 - 30000.0) * 1E-05;
                    myDeviceInfo.CalibValue2 = 1.0 + ((double)num3 - 30000.0) * 1E-05;
                    myDeviceInfo.CalibValue3 = 1.0 + ((double)num4 - 30000.0) * 1E-05;
                    myDeviceInfo.CalibValue4 = 1.0 + ((double)num5 - 30000.0) * 1E-05;
                    myDeviceInfo.Subtype = (int)INBuffer[7];
                    myDeviceInfo.UnitVersion = (int)INBuffer[6];
                }
            }
            catch (Exception)
            {
            }
            return myDeviceInfo;
        }

        private bool ReadFileManagedBuffer(
          SafeFileHandle hFile,
          byte[] INBuffer,
          uint nNumberOfBytesToRead,
          ref uint lpNumberOfBytesRead,
          IntPtr lpOverlapped)
        {
            IntPtr num = IntPtr.Zero;
            try
            {
                num = Marshal.AllocHGlobal((int)nNumberOfBytesToRead);
                if (USBM.ReadFile(hFile, num, nNumberOfBytesToRead, ref lpNumberOfBytesRead, lpOverlapped))
                {
                    Marshal.Copy(num, INBuffer, 0, (int)lpNumberOfBytesRead);
                    Marshal.FreeHGlobal(num);
                    return true;
                }
                Marshal.FreeHGlobal(num);
                return false;
            }
            catch
            {
                if (num != IntPtr.Zero)
                    Marshal.FreeHGlobal(num);
                return false;
            }
        }

        private void ANxVoltage_lbl_Click(object sender, EventArgs e)
        {
        }

        internal struct SP_DEVICE_INTERFACE_DATA
        {
            internal uint cbSize;
            internal Guid InterfaceClassGuid;
            internal uint Flags;
            internal IntPtr Reserved;
        }

        internal struct SP_DEVICE_INTERFACE_DETAIL_DATA
        {
            internal uint cbSize;
        }

        internal struct SP_DEVINFO_DATA
        {
            internal uint cbSize;
            internal Guid ClassGuid;
            internal uint DevInst;
            internal IntPtr Reserved;
        }

        public struct MyDeviceInfo
        {
            internal double CalibValue;
            internal double CalibValue1;
            internal double CalibValue2;
            internal double CalibValue3;
            internal double CalibValue4;
            public int Subtype;
            public int UnitVersion;
        }
    }
}
