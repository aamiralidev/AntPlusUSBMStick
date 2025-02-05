using System;
using System.ServiceProcess;
using System.Management;
using ANT_Managed_Library;
using System.IO;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using Serilog;


namespace AntPlus_Service
{
    public partial class AntPlus_Service : ServiceBase
    {
        ManagementEventWatcher watcher;
        ManagementEventWatcher disconnectionWatcher;
        private static string logFilePath = "logs\\ApliftUSBANT-.log";
        private FileSystemWatcher configWatcher;
        private string configFilePath = @"config.txt";
        string installationDirectory = AppDomain.CurrentDomain.BaseDirectory;
        private static StreamWriter logWriter;
        private static ANT_Device device = null;
        private static ANT_Channel channel = null;

        private static int roomNo = 0;
        private static int groupNo = 0;
        private static SocketIOClient.SocketIO client;
        string hardwareId = @"USB\\VID_0FCF&PID_1009";
        string infPath = @"ant_usb2_drivers\ANT_LibUsb.inf";
        bool driverInstalled = false;
        static ANT_Channel[] channels = new ANT_Channel[8];
        static Dictionary<uint, ANT_Channel[]> antDevices = new Dictionary<uint, ANT_Channel[]>();

        public AntPlus_Service()
        {
            Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(this.installationDirectory, AntPlus_Service.logFilePath),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                buffered: false)
            .CreateLogger();
            AntPlus_Service.client = new SocketIOClient.SocketIO("https://app.aplifitplay.com/lessons");
            //AntPlus_Service.client = new SocketIOClient.SocketIO("https://app-pre.aplifitplay.com/lessons");
            InitializeComponent();

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

            Log.Information("Service Started Successfully: Latest");
        }

        protected override void OnStop()
        {
            this.watcher.Stop();
            this.disconnectionWatcher.Stop();
            Log.Information("Service Stopped Successfully");
        }

        private static void HandleAttachedDevices()
        {
            var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE DeviceID LIKE '%USB%'");
            bool isDeviceFound = false;
            foreach (var device in searcher.Get())
            {
                string deviceId = device["DeviceID"].ToString();
                // Filter for devices with the ANT+ VID (and optionally PID)if (deviceId.Contains(antVid))
                if(deviceId.Contains("VID_0FCF") && deviceId.Contains("PID_1009"))
                {
                    Log.Information($"Device Detected: (ID: {deviceId})");
                    ListenForAntPlusData(deviceId);
                    isDeviceFound = true;
                    //break;
                }
            }
            if (!isDeviceFound)
            {
                Log.Information("No Device Detected, Setting to Null");
                device = null;
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
                /*if (device != null)
                {
                    Log.Information("Device is not null, returning");
                    
                    return;
                }*/

                Log.Information($"Device attached: {deviceDescription} (ID: {deviceId})");
                ListenForAntPlusData(deviceId);
            }
        }

        static void DeviceDisconnectedEvent(object sender, EventArrivedEventArgs e)
        {
            // Extract device details from the event
            try
            {
                ManagementBaseObject targetInstance = (ManagementBaseObject)e.NewEvent["TargetInstance"];
                string deviceDescription = (string)targetInstance["DeviceID"];

                if (deviceDescription.Contains("VID_0FCF") && deviceDescription.Contains("PID_1009"))
                {
                    Log.Information($"Device detached: {deviceDescription}");

                    string[] parts = deviceDescription.Split('\\');
                    string lastPart = parts[parts.Length - 1];
                    if (uint.TryParse(lastPart, out uint deviceId))
                    {
                        if (antDevices.ContainsKey(deviceId))
                        {
                            antDevices.Remove(deviceId);
                            Log.Information($"Device with Id {deviceDescription} is successfully removed.");
                            
                        }
                        else
                        {
                            Log.Information($"Device {deviceDescription} was not found in connected devices.");
                            
                        }
                    }
                    else
                    {
                        Log.Information($"Failed to extract deviceId from {deviceDescription})");
                        
                    }
         
                }
            }catch (Exception ex)
            {
                Log.Information($"Error occured while reading disconnected device info: {ex.Message}");
                
            }

        }

        static bool AddAntDevice(ANT_Device device)
        {
            uint deviceId = device.getSerialNumber();
            if (!antDevices.ContainsKey(deviceId))
            {
                antDevices[deviceId] = new ANT_Channel[8];
                return true;
            }
            Log.Information($"device with id {deviceId} already exists");
            return false;
        }

        static void ListenForAntPlusData(string deviceDescription)
        {

            try
            {
                device = new ANT_Device();   // Create a device instance using the automatic constructor (automatic detection of USB device number and baud rate)
                device.enableRxExtendedMessages(true);
                // device0.deviceResponse += new ANT_Device.dDeviceResponseHandler(DeviceResponse);    // Add device response function to receive protocol event messages
                byte[] userNetworkKey = { 0xB9, 0xA5, 0x21, 0xFB, 0xBD, 0x72, 0xC3, 0x45 };
                //byte[] userNetworkKey = { 0, 0, 0, 0, 0, 0, 0, 0 };
                device.setNetworkKey(0, userNetworkKey);
                AddAntDevice(device);
                uint deviceId = device.getSerialNumber();
                for(int i = 0; i < 8; i++)
                {
                    antDevices[deviceId][i] = device.getChannel(i);    // Get channel from ANT device
                    antDevices[deviceId][i].channelResponse += new dChannelResponseHandler(ChannelResponse);  // Add channel response function to receive channel event messages
                                                                                                              //channel.rawChannelResponse += new dRawChannelResponseHandler(rawChannelResponse);
                    antDevices[deviceId][i].assignChannel(ANT_ReferenceLibrary.ChannelType.BASE_Slave_Receive_0x00, 0, 500);
                    antDevices[deviceId][i].setChannelID(0, false, 0, 0);
                    antDevices[deviceId][i].setChannelFreq(57);
                    antDevices[deviceId][i].setChannelPeriod(8070);

                    var timeout = 2.5 / 2.5;
                    int timeoutValue = (int)timeout;
                    antDevices[deviceId][i].setChannelSearchTimeout((byte)timeoutValue);
                    antDevices[deviceId][i].openChannel();
                }
                
                //AntPlus_Service.eventLog.WriteEntry("Opened channel, Ready to Listen");
                Log.Information("Opened channel, Ready to Listen");
                
                Log.Information(device.ToString());
                
            }
            catch(Exception e)
            {
                Log.Information($"Listening on device failed with error {e.Message}");
                
            }
        }

        static void ResetChannel(byte channelNo)
        {
            foreach (var kvp in antDevices)
            {
                ANT_Channel channel = kvp.Value[channelNo];
                channel.assignChannel(ANT_ReferenceLibrary.ChannelType.BASE_Slave_Receive_0x00, 0, 500);
                channel.setChannelID(0, false, 0, 0);
                channel.setChannelFreq(57);
                channel.setChannelPeriod(8070);
                byte[] userNetworkKey = { 0xB9, 0xA5, 0x21, 0xFB, 0xBD, 0x72, 0xC3, 0x45 };
                var timeout = 2.5 / 2.5;
                int timeoutValue = (int)timeout;
                channel.setChannelSearchTimeout((byte)timeoutValue);
                channel.openChannel();
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
                                case ANT_ReferenceLibrary.ANTEventID.EVENT_RX_SEARCH_TIMEOUT_0x01:
                                    {
                                        //channel.openChannel();
                                        
                                        ResetChannel(response.antChannel);
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
                            var did = 0;
                            if (response.isExtended())
                            {
                                Log.Information("Message is Extended");
                                did = response.getDeviceIDfromExt().deviceNumber;
                            }
                            else
                            {
                                Log.Information("Message is not Extended");
                            }
                            if (contents[0] == 0 || contents[0] == 128 || contents[0] == 2 || contents[0] == 4 || contents[0] == 130 || contents[0] == 132)
                            {
                                var eventData = new
                                {
                                    id = 0,
                                    deviceId = did,
                                    roomId = AntPlus_Service.roomNo,
                                    groupId = AntPlus_Service.groupNo,
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
                                    roomId = AntPlus_Service.roomNo,
                                    groupId = AntPlus_Service.groupNo,
                                    deviceType = "FitnessEquipment",
                                    dataType = "rpm",
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
                                    roomId = AntPlus_Service.roomNo,
                                    groupId = AntPlus_Service.groupNo,
                                    deviceType = "FitnessEquipment",
                                    dataType = "Watt",
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
                                        deviceId = did,
                                        roomId = AntPlus_Service.roomNo,
                                        groupId = AntPlus_Service.groupNo,
                                        deviceType = "BikePowerSensor",
                                        dataType = "rpm",
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
                                        roomId = AntPlus_Service.roomNo,
                                        groupId = AntPlus_Service.groupNo,
                                        deviceType = "BikePowerSensor",
                                        dataType = "Watt",
                                        value = power,
                                        date = DateTime.Now,
                                    };
                                    jsonData = JsonConvert.SerializeObject(newEventData);
                                    UploadData(jsonData);
                                }
                                else
                                {
                                    Log.Information("None Relevant Data Recievved");
                                    
                                    var _contents = response.getDataPayload();
                                    Log.Information($"Page No: {_contents[0]}");
                                    
                                    Log.Information($"Other Values: {_contents[1]}, {_contents[2]}, {_contents[3]}, {_contents[4]},{_contents[5]},{_contents[6]},{_contents[7]},");
                                    
                                }
                            }else
                            {
                                Log.Information("None Relevant Data Recievved");
                                
                                var _contents = response.getDataPayload();
                                Log.Information($"Page No: {_contents[0]}");
                                
                                Log.Information($"Other Values: {_contents[1]}, {_contents[2]}, {_contents[3]}, {_contents[4]},{_contents[5]},{_contents[6]},{_contents[7]},");
                                
                            }
                            break;
                        }
                }
            }
            catch (Exception ex)
            {
                Log.Information($"Channel response processing failed with error {ex.Message}");
                
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
            }
            catch (Exception ex)
            {
                Log.Information($"Error Reading Config File: {ex.Message}");
                
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
                Log.Information($"Trying to send Data: {jsonData}");
                
                await AntPlus_Service.client.EmitAsync("antenna-data", response => {
                    Log.Information($"Data sent, recieved response: {response}");
                    
                }, jsonData);

            }
            catch (Exception ex)
            {
                Log.Information($"Error Sending Data: {jsonData}, Error: {ex.Message}");
                
            }
        }

        private void OnConfigChange(object sender, FileSystemEventArgs e)
        {
            ReadConfigFile();
        }

        static void rawChannelResponse(ANT_Device.ANTMessage message, ushort msize)
        {
            int id = message.msgID;
            byte[] contents = message.ucharBuf;
            Log.Information($"recieved response with id {id}");
            
            try
            {
                if (id == 64)
                {
                    if (contents[2] == 1)
                    {
                     //channel.openChannel();
                        //ResetChannel();
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
                        Log.Information($"Extended Device Num: {deviceNum}");
                        
                    }
                    else
                    {
                        deviceNum = ((contents[11] & 0xff) << 8) + (contents[10] & 0xff);
                        Log.Information($"Device Num: {deviceNum}");
                        
                    }
                    int pageNo = contents[1];
                    Log.Information($"Page No Recieved {pageNo}");
                    
                    List<int> validPageNumbers = new List<int> { 0, 128, 2, 4, 130, 132 };
                    if (validPageNumbers.Contains(pageNo))
                    {
                        var eventData = new
                        {
                            id = 0,
                            deviceId = deviceNum,
                            roomId = AntPlus_Service.roomNo,
                            groupId = AntPlus_Service.groupNo,
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
                            roomId = AntPlus_Service.roomNo,
                            groupId = AntPlus_Service.groupNo,
                            deviceType = "FitnessEquipment",
                            dataType = "rpm",
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
                            roomId = AntPlus_Service.roomNo,
                            groupId = AntPlus_Service.groupNo,
                            deviceType = "FitnessEquipment",
                            dataType = "Watt",
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
                                roomId = AntPlus_Service.roomNo,
                                groupId = AntPlus_Service.groupNo,
                                deviceType = "BikePowerSensor",
                                dataType = "rpm",
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
                                roomId = AntPlus_Service.roomNo,
                                groupId = AntPlus_Service.groupNo,
                                deviceType = "BikePowerSensor",
                                dataType = "Watt",
                                value = power,
                                date = DateTime.Now,
                            };
                            jsonData = JsonConvert.SerializeObject(newEventData);
                            UploadData(jsonData);
                        }
                        else
                        {
                            Log.Information("None Relevant Data Recieved");
                            string combinedContents = string.Join(", ", contents.Select(b => b.ToString()));
                            Log.Information($"Page No: {contents[1]}");
                            Log.Information($"Other Values: {combinedContents}");
                            
                        }
                    }
                    else
                    {
                        Log.Information($"None Relevant Data Recieved");
                        string combinedContents = string.Join(", ", contents.Select(b => b.ToString()));
                        Log.Information($"Page No: {contents[1]}");
                        Log.Information($"Other Values: {combinedContents}");
                        
                    }
                }
                else
                {
                    Log.Information($"Invalid Message Id Recieved: {id}");
                    string combinedContents = string.Join(", ", contents.Select(b => b.ToString()));
                    Log.Information($"Page No: {contents[1]}");
                    Log.Information($"Other Values: {combinedContents}");
                    
                }

            }
            catch (Exception ex)
            {
                Log.Information($"Channel response processing failed with error {ex.Message}");
                
            }
        }

        static void LogChannelResponse(byte[] contents)
        {
            string combinedContents = string.Join(", ", contents.Select(b => b.ToString()));
            Log.Information($"Contents: {combinedContents}");
            
        }
    }
}
