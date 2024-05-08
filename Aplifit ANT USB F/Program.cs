using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Diagnostics;
using System.Threading;
using System.Management;
using ANT_Managed_Library;
using System.IO;
using Newtonsoft.Json;
using System.Text;
using System.Collections.Generic;
using System.Linq;


using System.Drawing;

namespace Aplifit_ANT_USB_F
{
    class Program
    {
        private static EventLog eventLog;
        static ManagementEventWatcher watcher;
        static ManagementEventWatcher disconnectionWatcher;
        private static string logName = "AplifitLog";
        private static string logSource = "AplifitUSBANT";
        private static string logFilePath = "ApliftUSBANT.log";
        static private FileSystemWatcher configWatcher;
        static private string configFilePath = @"config.txt";
        static string installationDirectory = AppDomain.CurrentDomain.BaseDirectory;
        private static StreamWriter logWriter;
        private static ANT_Device device = null;
        private static ANT_Channel channel = null;

        private static int roomNo = 0;
        private static int groupNo = 0;
        private static string deviceId = null;
        private static SocketIOClient.SocketIO client;
        static string hardwareId = @"USB\\VID_0FCF&PID_1009";
        static string infPath = @"ant_usb2_drivers\ANT_LibUsb.inf";
        static bool driverInstalled = false;

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        /*static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }*/
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Create and configure the NotifyIcon
            NotifyIcon trayIcon = new NotifyIcon()
            {
                Icon = SystemIcons.Application,
                Visible = true
            };

            // Add context menu items
            ContextMenu trayMenu = new ContextMenu();
            trayMenu.MenuItems.Add("Open Settings", (sender, e) => OpenSettings());
            trayMenu.MenuItems.Add("Exit", (sender, e) => Exit());

            // Attach context menu to the NotifyIcon
            trayIcon.ContextMenu = trayMenu;


            Program.client = new SocketIOClient.SocketIO("https://app-pre.aplifitplay.com/lessons");
            //InitializeComponent();
            Program.eventLog = new EventLog();
            Program.logWriter = new StreamWriter(Path.Combine(Program.installationDirectory, Program.logFilePath), true);
            ReadConfigFile();
            Program.OnStart();

            // Run the application
            Application.Run();
        }

        static void OpenSettings()
        {
            //SettingsForm settingsForm = new SettingsForm();
            //settingsForm.ShowDialog();

        }

        static void Exit()
        {
            // Clean up and exit
            Application.Exit();
        }

        protected static void OnStart()
        {

            configWatcher = new FileSystemWatcher(Path.GetDirectoryName(installationDirectory), Path.GetFileName(configFilePath));
            configWatcher.Changed += OnConfigChange;
            configWatcher.EnableRaisingEvents = true;

            var disconnectionQuery = new WqlEventQuery("SELECT * FROM __InstanceDeletionEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_PnPEntity'");
            disconnectionWatcher = new ManagementEventWatcher(disconnectionQuery);
            disconnectionWatcher.EventArrived += new EventArrivedEventHandler(DeviceDisconnectedEvent);
            disconnectionWatcher.Start();

            HandleAttachedDevices();

            WqlEventQuery query = new WqlEventQuery("SELECT * FROM __InstanceCreationEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_PnPEntity'");
            watcher = new ManagementEventWatcher(query);
            watcher.EventArrived += new EventArrivedEventHandler(DeviceAttachedEvent);
            watcher.Start();

            //Program.eventLog.WriteEntry("Service started successfully", EventLogEntryType.Information);
            Program.logWriter.WriteLine("Service Started Successfully: Latest");
            Program.logWriter.Flush();
        }

        protected static void OnStop()
        {
            Program.watcher.Stop();
            Program.logWriter.WriteLine("Service Stopped Successfully");
            Program.logWriter.Flush();
        }

        private static void HandleAttachedDevices()
        {
            var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE DeviceID LIKE '%USB%'");
            bool isDeviceFound = false;
            foreach (var device in searcher.Get())
            {
                string deviceId = device["DeviceID"].ToString();
                // Filter for devices with the ANT+ VID (and optionally PID)if (deviceId.Contains(antVid))
                if (deviceId.Contains("VID_0FCF") && deviceId.Contains("PID_1009"))
                {
                    Program.logWriter.WriteLine($"Device Detected: (ID: {deviceId})");
                    Program.logWriter.Flush();
                    ListenForAntPlusData(deviceId);
                    isDeviceFound = true;
                    break;
                }
            }
            if (!isDeviceFound)
            {
                Program.logWriter.WriteLine("No Device Detected, Setting to Null");
                Program.logWriter.Flush();
                device = null;
            }
        }
        static void DeviceAttachedEvent(object sender, EventArrivedEventArgs e)
        {
            // Extract device details from the event
            ManagementBaseObject targetInstance = (ManagementBaseObject)e.NewEvent["TargetInstance"];
            string deviceId = (string)targetInstance["DeviceID"];
            string deviceDescription = (string)targetInstance["Description"];

            if (deviceId.Contains("VID_0FCF") && deviceId.Contains("PID_1009"))
            {
                if (device != null)
                {
                    Program.logWriter.WriteLine("Device is not null, returning");
                    Program.logWriter.Flush();
                    return;
                }

                Program.logWriter.WriteLine($"Device attached: {deviceDescription} (ID: {deviceId})");
                Program.logWriter.Flush();
                ListenForAntPlusData(deviceId);
            }
        }

        static void DeviceDisconnectedEvent(object sender, EventArrivedEventArgs e)
        {
            // Extract device details from the event
            try
            {
                ManagementBaseObject targetInstance = (ManagementBaseObject)e.NewEvent["TargetInstance"];
                string deviceId = (string)targetInstance["DeviceID"];
                string deviceDescription = (string)targetInstance["Description"];

                if (deviceId.Contains("VID_0FCF") && deviceId.Contains("PID_1009"))
                {
                    Program.logWriter.WriteLine($"Device detached: {deviceDescription} (ID: {deviceId})");
                    Program.logWriter.Flush();
                    HandleAttachedDevices();
                }
            }
            catch (Exception ex)
            {
                Program.logWriter.WriteLine($"Error occured while reading disconnected device info: {ex.Message}");
                Program.logWriter.Flush();
            }

        }
        static void ListenForAntPlusData(string deviceId)
        {

            try
            {
                Program.deviceId = ExtractDeviceId(deviceId);
                device = new ANT_Device();   // Create a device instance using the automatic constructor (automatic detection of USB device number and baud rate)
                // device0.deviceResponse += new ANT_Device.dDeviceResponseHandler(DeviceResponse);    // Add device response function to receive protocol event messages

                channel = device.getChannel(0);    // Get channel from ANT device
                //channel.channelResponse += new dChannelResponseHandler(ChannelResponse);  // Add channel response function to receive channel event messages
                channel.rawChannelResponse += new dRawChannelResponseHandler(rawChannelResponse);
                channel.assignChannel(ANT_ReferenceLibrary.ChannelType.BASE_Slave_Receive_0x00, 0, 500);
                channel.setChannelID(0, false, 0, 0);
                channel.setChannelFreq(57);
                channel.setChannelPeriod(8070);
                byte[] userNetworkKey = { 0xB9, 0xA5, 0x21, 0xFB, 0xBD, 0x72, 0xC3, 0x45 };
                //byte[] userNetworkKey = { 0, 0, 0, 0, 0, 0, 0, 0 };
                device.setNetworkKey(0, userNetworkKey);
                var timeout = 5 / 2.5;
                int timeoutValue = (int)timeout;
                channel.setChannelSearchTimeout((byte)timeoutValue);
                channel.openChannel();
                //Program.eventLog.WriteEntry("Opened channel, Ready to Listen");
                Program.logWriter.WriteLine("Opened channel, Ready to Listen");
                Program.logWriter.Flush();
                Program.logWriter.WriteLine(device);
                Program.logWriter.Flush();
            }
            catch (Exception e)
            {
                Program.logWriter.WriteLine($"Listening on device failed with error {e.Message}");
                Program.logWriter.Flush();
            }
        }

        static void ResetChannel()
        {
            channel.assignChannel(ANT_ReferenceLibrary.ChannelType.BASE_Slave_Receive_0x00, 0, 500);
            channel.setChannelID(0, false, 0, 0);
            channel.setChannelFreq(57);
            channel.setChannelPeriod(8070);
            byte[] userNetworkKey = { 0xB9, 0xA5, 0x21, 0xFB, 0xBD, 0x72, 0xC3, 0x45 };
            //byte[] userNetworkKey = { 0, 0, 0, 0, 0, 0, 0, 0 };
            device.setNetworkKey(0, userNetworkKey);
            var timeout = 5 / 2.5;
            int timeoutValue = (int)timeout;
            channel.setChannelSearchTimeout((byte)timeoutValue);
            channel.openChannel();
            //Program.eventLog.WriteEntry("Opened channel, Ready to Listen");
            Program.logWriter.WriteLine("Resetted channel, Ready to Listen");
            Program.logWriter.Flush();
        }

        static async void ChannelResponse(ANT_Response response)
        {
            try
            {
                switch ((ANT_ReferenceLibrary.ANTMessageID)response.responseID)
                {
                    case ANT_ReferenceLibrary.ANTMessageID.RESPONSE_EVENT_0x40:
                        {
                            switch (response.getChannelEventCode())
                            {
                                case ANT_ReferenceLibrary.ANTEventID.EVENT_RX_SEARCH_TIMEOUT_0x01:
                                    {
                                        //channel.openChannel();
                                        ResetChannel();
                                        break;
                                    }
                            }
                            break;
                        }
                    case ANT_ReferenceLibrary.ANTMessageID.BROADCAST_DATA_0x4E:
                    case ANT_ReferenceLibrary.ANTMessageID.ACKNOWLEDGED_DATA_0x4F:
                    case ANT_ReferenceLibrary.ANTMessageID.BURST_DATA_0x50:
                    case ANT_ReferenceLibrary.ANTMessageID.EXT_BROADCAST_DATA_0x5D:
                    case ANT_ReferenceLibrary.ANTMessageID.EXT_ACKNOWLEDGED_DATA_0x5E:
                    case ANT_ReferenceLibrary.ANTMessageID.EXT_BURST_DATA_0x5F:
                        {
                            var contents = response.getDataPayload();
                            var did = Program.deviceId;
                            if (response.isExtended())
                            {
                                Program.logWriter.WriteLine("Message is Extended");
                                did = response.getDeviceIDfromExt().ToString();
                            }
                            else
                            {
                                Program.logWriter.WriteLine("Message is not Extended");
                            }
                            if (contents[0] == 0 || contents[0] == 128 || contents[0] == 2 || contents[0] == 4 || contents[0] == 130 || contents[0] == 132)
                            {
                                var eventData = new
                                {
                                    id = 0,
                                    deviceId = did,
                                    roomId = Program.roomNo,
                                    groupId = Program.groupNo,
                                    deviceType = "HeartRateMonitor",
                                    dataType = "Ppm",
                                    value = contents[7],
                                    date = DateTime.Now,
                                };
                                string jsonData = JsonConvert.SerializeObject(eventData);
                                UploadData(jsonData);
                            }
                            else if (contents[0] == 25)
                            {
                                var eventData = new
                                {
                                    id = 0,
                                    deviceId = did,
                                    roomId = Program.roomNo,
                                    groupId = Program.groupNo,
                                    deviceType = "FitnessEquipment",
                                    dataType = "Cadence",
                                    value = contents[2],
                                    date = DateTime.Now,
                                };
                                string jsonData = JsonConvert.SerializeObject(eventData);
                                UploadData(jsonData);
                                byte byte5 = contents[5];
                                byte byte6 = contents[6];
                                byte firstFourBitsByte6 = (byte)(byte6 & 0x0F);
                                ushort combinedValue = (ushort)((firstFourBitsByte6 << 8) | byte5);
                                int power = combinedValue;
                                var newEventData = new
                                {
                                    id = 0,
                                    deviceId = did,
                                    roomId = Program.roomNo,
                                    groupId = Program.groupNo,
                                    deviceType = "FitnessEquipment",
                                    dataType = "Power",
                                    value = power,
                                    date = DateTime.Now,
                                };
                                jsonData = JsonConvert.SerializeObject(newEventData);
                                UploadData(jsonData);
                            }
                            else if (contents[0] == 16)
                            {
                                byte etbf = contents[1];
                                if (etbf < 19 || etbf > 25)
                                {
                                    var eventData = new
                                    {
                                        id = 0,
                                        deviceId = did,
                                        roomId = Program.roomNo,
                                        groupId = Program.groupNo,
                                        deviceType = "BikePowerSensor",
                                        dataType = "Cadence",
                                        value = contents[3],
                                        date = DateTime.Now,
                                    };
                                    string jsonData = JsonConvert.SerializeObject(eventData);
                                    UploadData(jsonData);
                                    byte byte6 = contents[6];
                                    byte byte7 = contents[7];
                                    ushort combinedValue = (ushort)((byte7 << 8) | byte6);
                                    int power = combinedValue;
                                    var newEventData = new
                                    {
                                        id = 0,
                                        deviceId = did,
                                        roomId = Program.roomNo,
                                        groupId = Program.groupNo,
                                        deviceType = "BikePowerSensor",
                                        dataType = "Power",
                                        value = power,
                                        date = DateTime.Now,
                                    };
                                    jsonData = JsonConvert.SerializeObject(newEventData);
                                    UploadData(jsonData);
                                }
                                else
                                {
                                    Program.logWriter.WriteLine("None Relevant Data Recievved");
                                    Program.logWriter.Flush();
                                    var _contents = response.getDataPayload();
                                    Program.logWriter.WriteLine($"Page No: {_contents[0]}");
                                    Program.logWriter.Flush();
                                    Program.logWriter.WriteLine($"Other Values: {_contents[1]}, {_contents[2]}, {_contents[3]}, {_contents[4]},{_contents[5]},{_contents[6]},{_contents[7]},");
                                    Program.logWriter.Flush();
                                }
                            }
                            else
                            {
                                Program.logWriter.WriteLine("None Relevant Data Recievved");
                                Program.logWriter.Flush();
                                var _contents = response.getDataPayload();
                                Program.logWriter.WriteLine($"Page No: {_contents[0]}");
                                Program.logWriter.Flush();
                                Program.logWriter.WriteLine($"Other Values: {_contents[1]}, {_contents[2]}, {_contents[3]}, {_contents[4]},{_contents[5]},{_contents[6]},{_contents[7]},");
                                Program.logWriter.Flush();
                            }
                            break;
                        }
                }
            }
            catch (Exception ex)
            {
                Program.logWriter.WriteLine($"Channel response processing failed with error {ex.Message}");
                Program.logWriter.Flush();
            }
        }

        private static void ReadConfigFile()
        {

            try
            {
                string configData = File.ReadAllText(Path.Combine(Program.installationDirectory, Program.configFilePath));
                string[] parts = configData.Split('\n');
                Program.roomNo = int.Parse(parts[0]);
                Program.groupNo = int.Parse(parts[1]);
                /*this.channelFreq = int.Parse(parts[1]);
                this.networkNumber = int.Parse(parts[1]); */
            }
            catch (Exception ex)
            {
                Program.logWriter.WriteLine($"Error Reading Config File: {ex.Message}");
                Program.logWriter.Flush();
                /*using (EventLog eventLog = new EventLog(Program.logName))
                {
                    eventLog.Source = Program.logSource;
                    eventLog.WriteEntry($"Error Reading Config File: {ex.Message}", EventLogEntryType.Error);
                }*/
            }
        }

        private static async void UploadData(string jsonData)
        {
            try
            {
                if (!Program.client.Connected)
                {
                    await Program.client.ConnectAsync();
                }
                Program.logWriter.WriteLine($"Trying to send Data: {jsonData}");
                Program.logWriter.Flush();
                /*using (EventLog eventLog = new EventLog(Program.logName))
                {
                    eventLog.Source = Program.logSource;
                    eventLog.WriteEntry($"Trying to send Data: {jsonData}", EventLogEntryType.Information);
                }*/
                await Program.client.EmitAsync("antenna-data", response => {
                    Program.logWriter.WriteLine($"Data sent, recieved response: {response}");
                    Program.logWriter.Flush();
                    /*using (EventLog eventLog = new EventLog(Program.logName))
                    {
                        eventLog.Source = Program.logSource;
                        eventLog.WriteEntry($"Data sent, recieved response: {response}", EventLogEntryType.Information);
                    }*/
                }, jsonData);

            }
            catch (Exception ex)
            {
                Program.logWriter.WriteLine($"Error Sending Data: {jsonData}, Error: {ex.Message}");
                Program.logWriter.Flush();
                /*using (EventLog eventLog = new EventLog(Program.logName))
                {
                    eventLog.Source = Program.logSource;
                    eventLog.WriteEntry($"Error Sending Data: {jsonData}, Error: {ex.Message}", EventLogEntryType.Error);
                }*/
            }
        }

        private static async void UploadData(int dtid, string data)
        {
            var eventData = new
            {
                roomNo = Program.roomNo,
                groupNo = Program.groupNo,
                deviceId = dtid,
                value = data,
                date = DateTime.Now,
            };
            string jsonData = JsonConvert.SerializeObject(eventData);
            try
            {
                if (!Program.client.Connected)
                {
                    await Program.client.ConnectAsync();
                }
                using (EventLog eventLog = new EventLog(Program.logName))
                {
                    eventLog.Source = Program.logSource;
                    eventLog.WriteEntry($"Trying to send Data: {jsonData}", EventLogEntryType.Information);
                }
                await Program.client.EmitAsync("antenna-data", response => {
                    using (EventLog eventLog = new EventLog(Program.logName))
                    {
                        eventLog.Source = Program.logSource;
                        eventLog.WriteEntry($"Data sent, recieved response: {response}", EventLogEntryType.Information);
                    }
                }, jsonData);

            }
            catch (Exception ex)
            {
                using (EventLog eventLog = new EventLog(Program.logName))
                {
                    eventLog.Source = Program.logSource;
                    eventLog.WriteEntry($"Error Sending Data: {jsonData}, Error: {ex.Message}", EventLogEntryType.Error);
                }
            }

        }

        private static void OnConfigChange(object sender, FileSystemEventArgs e)
        {
            ReadConfigFile();
        }

        public static string ExceptionDataToString(Exception ex)
        {
            if (ex.Data == null || ex.Data.Count == 0)
            {
                return "No additional data.";
            }
            StringBuilder sb = new StringBuilder();
            foreach (System.Collections.DictionaryEntry entry in ex.Data)
            {
                sb.AppendFormat("{0}: {1}{2}", entry.Key, entry.Value, Environment.NewLine);
            }
            return sb.ToString();
        }

        public static string ExtractDeviceId(string deviceId)
        {
            char[] separators = { '\\' }; // Define separator as '\'
            string[] parts = deviceId.Split(separators, StringSplitOptions.RemoveEmptyEntries); // Split the input string

            // Get the last part of the resulting array
            string desiredPart = parts[parts.Length - 1];

            return desiredPart;
        }

        static void rawChannelResponse(ANT_Device.ANTMessage message, ushort msize)
        {
            int id = message.msgID;
            byte[] contents = message.ucharBuf;
            Program.logWriter.WriteLine($"recieved response with id {id}");
            Program.logWriter.Flush();
            try
            {
                if (id == 64)
                {
                    if (contents[2] == 1)
                    {
                        //channel.openChannel();
                        ResetChannel();
                    }
                    else
                    {
                        return;
                    }
                }
                List<int> validMessageIds = new List<int> { 78, 79, 80, 93, 94, 95 };
                if (validMessageIds.Contains(id))
                {
                    LogChannelResponse(contents);
                    int deviceNum = 0;
                    if (contents[6] == 0)
                    {
                        deviceNum = ((contents[6] & 0xff) << 24) + ((contents[5] & 0xff) << 16) + ((contents[11] & 0xff) << 8) + (contents[10] & 0xff);
                        Console.WriteLine($"Extended Device Num: {deviceNum}");
                        Program.logWriter.WriteLine($"Extended Device Num: {deviceNum}");
                        Program.logWriter.Flush();
                    }
                    else
                    {
                        deviceNum = ((contents[11] & 0xff) << 8) + (contents[10] & 0xff);
                        Program.logWriter.WriteLine($"Device Num: {deviceNum}");
                        Program.logWriter.Flush();
                    }
                    int pageNo = contents[1];
                    Program.logWriter.WriteLine($"Page No Recieved {pageNo}");
                    Program.logWriter.Flush();
                    List<int> validPageNumbers = new List<int> { 0, 128, 2, 4, 130, 132 };
                    if (validPageNumbers.Contains(pageNo))
                    {
                        var eventData = new
                        {
                            id = 0,
                            deviceId = deviceNum,
                            roomId = Program.roomNo,
                            groupId = Program.groupNo,
                            deviceType = "HeartRateMonitor",
                            dataType = "Ppm",
                            value = contents[8],
                            date = DateTime.Now,
                        };
                        string jsonData = JsonConvert.SerializeObject(eventData);
                        UploadData(jsonData);
                    }
                    else if (pageNo == 25)
                    {
                        var eventData = new
                        {
                            id = 0,
                            deviceId = deviceNum,
                            roomId = Program.roomNo,
                            groupId = Program.groupNo,
                            deviceType = "FitnessEquipment",
                            dataType = "Cadence",
                            value = contents[3],
                            date = DateTime.Now,
                        };
                        string jsonData = JsonConvert.SerializeObject(eventData);
                        UploadData(jsonData);
                        byte byte6 = contents[6];
                        byte byte7 = contents[7];
                        byte firstFourBitsByte6 = (byte)(byte7 & 0x0F);
                        ushort combinedValue = (ushort)((firstFourBitsByte6 << 8) | byte6);
                        int power = combinedValue;
                        var newEventData = new
                        {
                            id = 0,
                            deviceId = deviceNum,
                            roomId = Program.roomNo,
                            groupId = Program.groupNo,
                            deviceType = "FitnessEquipment",
                            dataType = "Power",
                            value = power,
                            date = DateTime.Now,
                        };
                        jsonData = JsonConvert.SerializeObject(newEventData);
                        UploadData(jsonData);
                    }
                    else if (pageNo == 16)
                    {
                        byte etbf = contents[2];
                        if (etbf < 19 || etbf > 25)
                        {
                            var eventData = new
                            {
                                id = 0,
                                deviceId = deviceNum,
                                roomId = Program.roomNo,
                                groupId = Program.groupNo,
                                deviceType = "BikePowerSensor",
                                dataType = "Cadence",
                                value = contents[4],
                                date = DateTime.Now,
                            };
                            string jsonData = JsonConvert.SerializeObject(eventData);
                            UploadData(jsonData);
                            byte byte7 = contents[7];
                            byte byte8 = contents[8];
                            ushort combinedValue = (ushort)((byte8 << 8) | byte7);
                            int power = combinedValue;
                            var newEventData = new
                            {
                                id = 0,
                                deviceId = deviceNum,
                                roomId = Program.roomNo,
                                groupId = Program.groupNo,
                                deviceType = "BikePowerSensor",
                                dataType = "Power",
                                value = power,
                                date = DateTime.Now,
                            };
                            jsonData = JsonConvert.SerializeObject(newEventData);
                            UploadData(jsonData);
                        }
                        else
                        {
                            Program.logWriter.WriteLine("None Relevant Data Recieved");
                            string combinedContents = string.Join(", ", contents.Select(b => b.ToString()));
                            Program.logWriter.WriteLine($"Page No: {contents[1]}");
                            Program.logWriter.WriteLine($"Other Values: {combinedContents}");
                            Program.logWriter.Flush();
                        }
                    }
                    else
                    {
                        Program.logWriter.WriteLine($"None Relevant Data Recieved");
                        string combinedContents = string.Join(", ", contents.Select(b => b.ToString()));
                        Program.logWriter.WriteLine($"Page No: {contents[1]}");
                        Program.logWriter.WriteLine($"Other Values: {combinedContents}");
                        Program.logWriter.Flush();
                    }
                }
                else
                {
                    Program.logWriter.WriteLine($"Invalid Message Id Recieved: {id}");
                    string combinedContents = string.Join(", ", contents.Select(b => b.ToString()));
                    Program.logWriter.WriteLine($"Page No: {contents[1]}");
                    Program.logWriter.WriteLine($"Other Values: {combinedContents}");
                    Program.logWriter.Flush();
                }

            }
            catch (Exception ex)
            {
                Program.logWriter.WriteLine($"Channel response processing failed with error {ex.Message}");
                Program.logWriter.Flush();
            }
        }

        static void LogChannelResponse(byte[] contents)
        {
            string combinedContents = string.Join(", ", contents.Select(b => b.ToString()));
            Program.logWriter.WriteLine($"Contents: {combinedContents}");
            Program.logWriter.Flush();
        }
    }
}
