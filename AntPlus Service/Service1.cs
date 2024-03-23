using System;
using System.Diagnostics;
using System.ServiceProcess;
using System.Threading;
using System.Management;
using ANT_Managed_Library;
using System.IO;
using Newtonsoft.Json;
using System.Text;

namespace AntPlus_Service
{
    public partial class AntPlus_Service : ServiceBase
    {
        private static EventLog eventLog;
        ManagementEventWatcher watcher;
        ManagementEventWatcher disconnectionWatcher;
        private static string logName = "AplifitLog";
        private static string logSource = "AplifitUSBANT";
        private static string logFilePath = "ApliftUSBANT.log";
        private FileSystemWatcher configWatcher;
        private string configFilePath = @"config.txt";
        string installationDirectory = AppDomain.CurrentDomain.BaseDirectory;
        private static StreamWriter logWriter;

        private static int roomNo = 0;
        private static int groupNo = 0;
        private static string deviceId = null;
        private static SocketIOClient.SocketIO client;
        string hardwareId = @"USB\\VID_0FCF&PID_1009";
        string infPath = @"ant_usb2_drivers\ANT_LibUsb.inf";
        bool driverInstalled = false;
        /*        private int channelFreq = 57;
                private byte[] networkKey = { 0xB9, 0xA5, 0x21, 0xFB, 0xBD, 0x72, 0xC3, 0x45 };
                private int networkNumber = 0; */
        public AntPlus_Service()
        {
            AntPlus_Service.client = new SocketIOClient.SocketIO("https://app-pre.aplifitplay.com/lessons");
            InitializeComponent();
            AntPlus_Service.eventLog = new EventLog();
            AntPlus_Service.logWriter = new StreamWriter(Path.Combine(this.installationDirectory, AntPlus_Service.logFilePath), true);
            if (!EventLog.SourceExists(AntPlus_Service.logSource))
            {
                EventLog.CreateEventSource(AntPlus_Service.logSource, AntPlus_Service.logName);
            }
            else
            {
                AntPlus_Service.logName = EventLog.LogNameFromSourceName(logSource, ".");
            }
            AntPlus_Service.eventLog.Source = AntPlus_Service.logSource;
            AntPlus_Service.eventLog.Log = AntPlus_Service.logName;
            ReadConfigFile();
            //ReadConfigFile();
            /*string query = $"SELECT * FROM Win32_PnPEntity WHERE DeviceID LIKE '%{hardwareId}%'";
            try
            {
                driverInstalled = DriverInstaller.IsDriverInstalled(hardwareId);
                if (true)
                {
                    DriverInstaller.InstallDriver(Path.Combine(this.installationDirectory, this.infPath));
                    AntPlus_Service.eventLog.WriteEntry("Installed Driver From Path + " + Path.Combine(this.installationDirectory, this.infPath));
                }else
                {
                    AntPlus_Service.eventLog.WriteEntry("Driver is already installed");
                }
            }
            catch (Win32Exception ex)
            {
                AntPlus_Service.eventLog.WriteEntry($"Error Installing Driver with query: {query}, Error: {ex.Message} + {ex.ErrorCode}, infPath {Path.Combine(this.installationDirectory, this.infPath)}");
            }
            catch (Exception ex)
            {
                AntPlus_Service.eventLog.WriteEntry($"Error Installing Driver with query: {query}, Error: {ex.Message} + {ExceptionDataToString(ex)}, infPath {Path.Combine(this.installationDirectory, this.infPath)}" );
            } */

        }

        protected override void OnStart(string[] args)
        {
            
            /*workerThread = new Thread(WorkerThreadMethod);
            workerThread.Start();*/
            

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

            //AntPlus_Service.eventLog.WriteEntry("Service started successfully", EventLogEntryType.Information);
            AntPlus_Service.logWriter.WriteLine("Service Started Successfully");
            AntPlus_Service.logWriter.Flush();
        }

        protected override void OnStop()
        {
            //AntPlus_Service.eventLog.WriteEntry("Service stopping...", EventLogEntryType.Information);
            /*stopWorkerThread = true;
            workerThread.Join(); */
            this.watcher.Stop();
            AntPlus_Service.logWriter.WriteLine("Service Stopped Successfully");
            AntPlus_Service.logWriter.Flush();
        }

        private void HandleAttachedDevices()
        {
            var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE DeviceID LIKE '%USB%'");
            foreach (var device in searcher.Get())
            {
                string deviceId = device["DeviceID"].ToString();
                // Filter for devices with the ANT+ VID (and optionally PID)if (deviceId.Contains(antVid))
                if(deviceId.Contains("VID_0FCF") && deviceId.Contains("PID_1009"))
                {
                    AntPlus_Service.logWriter.WriteLine($"Device Detected: (ID: {deviceId})");
                    AntPlus_Service.logWriter.Flush();
                    //AntPlus_Service.eventLog.WriteEntry($"Device detected: (ID: {deviceId})", EventLogEntryType.Information);
                    //Thread listenThread = new Thread(() => ListenForAntPlusData(""));
                    //listenThread.Start();
                    ListenForAntPlusData(deviceId);
                }
            }
        }
        static void DeviceAttachedEvent(object sender, EventArrivedEventArgs e)
        {
            // Extract device details from the event
            ManagementBaseObject targetInstance = (ManagementBaseObject)e.NewEvent["TargetInstance"];
            string deviceId = (string)targetInstance["DeviceID"];
            string deviceDescription = (string)targetInstance["Description"];

            if(deviceId.Contains("VID_0FCF") && deviceId.Contains("PID_1009"))
            {
                ListenForAntPlusData(deviceId);
                AntPlus_Service.logWriter.WriteLine($"Device attached: {deviceDescription} (ID: {deviceId})");
                AntPlus_Service.logWriter.Flush();
                using (EventLog eventLog = new EventLog(AntPlus_Service.logName))
                {
                    //eventLog.Source = AntPlus_Service.logSource;
                    //eventLog.WriteEntry($"Device attached: {deviceDescription} (ID: {deviceId})", EventLogEntryType.Information);
                    //Thread listenThread = new Thread(() => ListenForAntPlusData(deviceDescription));
                    //listenThread.Start();
                    
                }
            }
            // Log device details to Windows Event Log

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
                    AntPlus_Service.logWriter.WriteLine($"Device detached: {deviceDescription} (ID: {deviceId})");
                    AntPlus_Service.logWriter.Flush();
                    /*using (EventLog eventLog = new EventLog(AntPlus_Service.logName))
                    {
                        eventLog.Source = AntPlus_Service.logSource;
                        eventLog.WriteEntry($"Device detached: {deviceDescription} (ID: {deviceId})", EventLogEntryType.Information);
                    }*/
                }
            }catch (Exception ex)
            {
                AntPlus_Service.logWriter.WriteLine($"Error occured while reading disconnected device info: {ex.Message}");
                AntPlus_Service.logWriter.Flush();
                /*using (EventLog eventLog = new EventLog(AntPlus_Service.logName))
                {
                    eventLog.Source = AntPlus_Service.logSource;
                    eventLog.WriteEntry($"Error occured while reading disconnected device info: {ex.Message}", EventLogEntryType.Information);
                }*/
            }
            // Log device details to Windows Event Log

        }
        static void ListenForAntPlusData(string deviceId)
        {

            try
            {
                AntPlus_Service.deviceId = ExtractDeviceId(deviceId);
                ANT_Device device0 = new ANT_Device();   // Create a device instance using the automatic constructor (automatic detection of USB device number and baud rate)
                // device0.deviceResponse += new ANT_Device.dDeviceResponseHandler(DeviceResponse);    // Add device response function to receive protocol event messages

                ANT_Channel channel0 = device0.getChannel(0);    // Get channel from ANT device
                channel0.channelResponse += new dChannelResponseHandler(ChannelResponse);  // Add channel response function to receive channel event messages
                
                channel0.assignChannel(ANT_ReferenceLibrary.ChannelType.BASE_Slave_Receive_0x00, 0, 500);
                channel0.setChannelID(0, false, 0, 0);
                channel0.setChannelFreq(57);
                channel0.setChannelPeriod(8070);
                byte[] userNetworkKey = { 0xB9, 0xA5, 0x21, 0xFB, 0xBD, 0x72, 0xC3, 0x45 };
                //byte[] userNetworkKey = { 0, 0, 0, 0, 0, 0, 0, 0 };
                device0.setNetworkKey(0, userNetworkKey);
                
                channel0.openChannel();
                //AntPlus_Service.eventLog.WriteEntry("Opened channel, Ready to Listen");
                AntPlus_Service.logWriter.WriteLine("Opened channel, Ready to Listen");
                AntPlus_Service.logWriter.Flush();
            }
            catch(Exception e)
            {
                AntPlus_Service.logWriter.WriteLine($"Listening on device failed with error {e.Message}");
                AntPlus_Service.logWriter.Flush();
                /*using (EventLog eventLog = new EventLog(AntPlus_Service.logName))
                {
                    eventLog.Source = AntPlus_Service.logSource;
                    eventLog.WriteEntry($"Listening on device failed with error {e.Message}", EventLogEntryType.Error);
                }*/
            }
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
                                case ANT_ReferenceLibrary.ANTEventID.EVENT_TX_0x03:
                                    {
                                        AntPlus_Service.logWriter.WriteLine($"Recieved EVENT TX 0X03");
                                        AntPlus_Service.logWriter.Flush();
                                        /*using (EventLog eventLog = new EventLog(AntPlus_Service.logName))
                                        {
                                            eventLog.Source = AntPlus_Service.logSource;
                                            eventLog.WriteEntry($"Recieved EVENT TX 0X03", EventLogEntryType.Information);
                                        }*/
                                        break;
                                    }
                            }
                            break;
                        }
                    case ANT_ReferenceLibrary.ANTMessageID.BROADCAST_DATA_0x4E:
                    case ANT_ReferenceLibrary.ANTMessageID.ACKNOWLEDGED_DATA_0x4F:
                    case ANT_ReferenceLibrary.ANTMessageID.BURST_DATA_0x50:
                        {
                            if (response.isExtended())
                            {
                                AntPlus_Service.logWriter.WriteLine("Message is Extended");
                                AntPlus_Service.logWriter.Flush();
                                //AntPlus_Service.eventLog.WriteEntry("Message is Extended", EventLogEntryType.Information);
                            }
                            var contents = response.getDataPayload();
                            if (contents[0] == 0 || contents[0] == 128)
                            {
                                var eventData = new
                                {
                                    id = 0,
                                    deviceId = AntPlus_Service.deviceId,
                                    roomNo = AntPlus_Service.roomNo,
                                    groupNo = AntPlus_Service.groupNo,
                                    deviceType = "HeartRateMonitor",
                                    dataType = "Ppm",
                                    value = contents[7],
                                    date = DateTime.Now,
                                };
                                string jsonData = JsonConvert.SerializeObject(eventData);
                                UploadData(jsonData);
                            }else if (contents[0] == 25)
                            {
                                var eventData = new
                                {
                                    id = 0,
                                    deviceId = AntPlus_Service.deviceId,
                                    roomNo = AntPlus_Service.roomNo,
                                    groupNo = AntPlus_Service.groupNo,
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
                                    deviceId = AntPlus_Service.deviceId,
                                    roomNo = AntPlus_Service.roomNo,
                                    groupNo = AntPlus_Service.groupNo,
                                    deviceType = "FitnessEquipment",
                                    dataType = "Power",
                                    value = power,
                                    date = DateTime.Now,
                                };
                                jsonData = JsonConvert.SerializeObject(newEventData);
                                UploadData(jsonData);
                            }else if (contents[0] == 16)
                            {
                                byte etbf = contents[1];
                                if(etbf < 19 || etbf > 25)
                                {
                                    var eventData = new
                                    {
                                        id = 0,
                                        deviceId = AntPlus_Service.deviceId,
                                        roomNo = AntPlus_Service.roomNo,
                                        groupNo = AntPlus_Service.groupNo,
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
                                        deviceId = AntPlus_Service.deviceId,
                                        roomNo = AntPlus_Service.roomNo,
                                        groupNo = AntPlus_Service.groupNo,
                                        deviceType = "BikePowerSensor",
                                        dataType = "Power",
                                        value = power,
                                        date = DateTime.Now,
                                    };
                                    jsonData = JsonConvert.SerializeObject(newEventData);
                                    UploadData(jsonData);
                                }
                            }
                            //eventLog.WriteEntry($"Data Recieved: {dtid.ToString()}", EventLogEntryType.Information);
                            //UploadData(0, BitConverter.ToString(response.getDataPayload()));
                            break;
                        }
                    case ANT_ReferenceLibrary.ANTMessageID.EXT_BROADCAST_DATA_0x5D:
                    case ANT_ReferenceLibrary.ANTMessageID.EXT_ACKNOWLEDGED_DATA_0x5E:
                    case ANT_ReferenceLibrary.ANTMessageID.EXT_BURST_DATA_0x5F:
                        {
                            using (EventLog eventLog = new EventLog(AntPlus_Service.logName))
                            {
                                var dtid = response.getDeviceIDfromExt().deviceTypeID;
                                eventLog.Source = AntPlus_Service.logSource;
                                eventLog.WriteEntry($"Data Recieved: {BitConverter.ToString(response.getDataPayload())}", EventLogEntryType.Information);
                                eventLog.WriteEntry($"device type Recieved: {Convert.ToInt32(dtid)}", EventLogEntryType.Information);
                                UploadData(dtid, BitConverter.ToString(response.getDataPayload()));
                            }
                            break;
                        }
                }
            }
            catch (Exception ex)
            {
                AntPlus_Service.logWriter.WriteLine($"Channel response processing failed with error {ex.Message}");
                AntPlus_Service.logWriter.Flush();
                /*using (EventLog eventLog = new EventLog(AntPlus_Service.logName))
                {
                    eventLog.Source = AntPlus_Service.logSource;
                    eventLog.WriteEntry($"Channel response processing failed with error {ex.Message}", EventLogEntryType.Error);
                }*/
            }
        }

        private void ReadConfigFile()
        {
            
            try
            {
                string configData = File.ReadAllText(Path.Combine(this.installationDirectory, this.configFilePath));
                string[] parts = configData.Split('\n');
                AntPlus_Service.roomNo = int.Parse(parts[0]);
                AntPlus_Service.groupNo = int.Parse(parts[1]);
                /*this.channelFreq = int.Parse(parts[1]);
                this.networkNumber = int.Parse(parts[1]); */
            }
            catch (Exception ex)
            {
                AntPlus_Service.logWriter.WriteLine($"Error Reading Config File: {ex.Message}");
                AntPlus_Service.logWriter.Flush();
                /*using (EventLog eventLog = new EventLog(AntPlus_Service.logName))
                {
                    eventLog.Source = AntPlus_Service.logSource;
                    eventLog.WriteEntry($"Error Reading Config File: {ex.Message}", EventLogEntryType.Error);
                }*/
            }
        }

        private static async void UploadData(string jsonData)
        {
            try
            {
                if (!AntPlus_Service.client.Connected)
                {
                    await AntPlus_Service.client.ConnectAsync();
                }
                AntPlus_Service.logWriter.WriteLine($"Trying to send Data: {jsonData}");
                AntPlus_Service.logWriter.Flush();
                /*using (EventLog eventLog = new EventLog(AntPlus_Service.logName))
                {
                    eventLog.Source = AntPlus_Service.logSource;
                    eventLog.WriteEntry($"Trying to send Data: {jsonData}", EventLogEntryType.Information);
                }*/
                await AntPlus_Service.client.EmitAsync("antenna-data", response => {
                    AntPlus_Service.logWriter.WriteLine($"Data sent, recieved response: {response}");
                    AntPlus_Service.logWriter.Flush();
                    /*using (EventLog eventLog = new EventLog(AntPlus_Service.logName))
                    {
                        eventLog.Source = AntPlus_Service.logSource;
                        eventLog.WriteEntry($"Data sent, recieved response: {response}", EventLogEntryType.Information);
                    }*/
                }, jsonData);

            }
            catch (Exception ex)
            {
                AntPlus_Service.logWriter.WriteLine($"Error Sending Data: {jsonData}, Error: {ex.Message}");
                AntPlus_Service.logWriter.Flush();
                /*using (EventLog eventLog = new EventLog(AntPlus_Service.logName))
                {
                    eventLog.Source = AntPlus_Service.logSource;
                    eventLog.WriteEntry($"Error Sending Data: {jsonData}, Error: {ex.Message}", EventLogEntryType.Error);
                }*/
            }
        }

        private static async void UploadData(int dtid, string data)
        {
            var eventData = new
            {
                roomNo = AntPlus_Service.roomNo,
                groupNo = AntPlus_Service.groupNo,
                deviceId = dtid,
                value = data,
                date = DateTime.Now,
            };
            string jsonData = JsonConvert.SerializeObject(eventData);
            try
            {
                if (!AntPlus_Service.client.Connected)
                {
                    await AntPlus_Service.client.ConnectAsync();
                }
                using (EventLog eventLog = new EventLog(AntPlus_Service.logName))
                {
                    eventLog.Source = AntPlus_Service.logSource;
                    eventLog.WriteEntry($"Trying to send Data: {jsonData}", EventLogEntryType.Information);
                }
                await AntPlus_Service.client.EmitAsync("antenna-data", response => {
                    using (EventLog eventLog = new EventLog(AntPlus_Service.logName))
                    {
                        eventLog.Source = AntPlus_Service.logSource;
                        eventLog.WriteEntry($"Data sent, recieved response: {response}", EventLogEntryType.Information);
                    }
                }, jsonData);

            }
            catch (Exception ex)
            {
                using (EventLog eventLog = new EventLog(AntPlus_Service.logName))
                {
                    eventLog.Source = AntPlus_Service.logSource;
                    eventLog.WriteEntry($"Error Sending Data: {jsonData}, Error: {ex.Message}", EventLogEntryType.Error);
                }
            }
            
        }

        private void OnConfigChange(object sender, FileSystemEventArgs e)
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
    }
}
