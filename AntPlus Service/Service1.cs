﻿using System;
using System.Diagnostics;
using System.ServiceProcess;
using System.Threading;
using System.Management;
using ANT_Managed_Library;
using System.IO;
using Newtonsoft.Json;
using System.Text;
using System.Collections.Generic;
using System.Linq;

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
        private static ANT_Device device = null;
        private static ANT_Channel channel = null;

        private static int roomNo = 0;
        private static int groupNo = 0;
        private static SocketIOClient.SocketIO client;
        string hardwareId = @"USB\\VID_0FCF&PID_1009";
        string infPath = @"ant_usb2_drivers\ANT_LibUsb.inf";
        bool driverInstalled = false;
        static ANT_Channel[] channels = new ANT_Channel[8];

        public AntPlus_Service()
        {
            AntPlus_Service.client = new SocketIOClient.SocketIO("https://app.aplifitplay.com/lessons");
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
            AntPlus_Service.logWriter.WriteLine("Service Started Successfully: Latest");
            AntPlus_Service.logWriter.Flush();
        }

        protected override void OnStop()
        {
            this.watcher.Stop();
            AntPlus_Service.logWriter.WriteLine("Service Stopped Successfully");
            AntPlus_Service.logWriter.Flush();
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
                    AntPlus_Service.logWriter.WriteLine($"Device Detected: (ID: {deviceId})");
                    AntPlus_Service.logWriter.Flush();
                    ListenForAntPlusData(deviceId);
                    isDeviceFound = true;
                    break;
                }
            }
            if (!isDeviceFound)
            {
                AntPlus_Service.logWriter.WriteLine("No Device Detected, Setting to Null");
                AntPlus_Service.logWriter.Flush();
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
                if (device != null)
                {
                    AntPlus_Service.logWriter.WriteLine("Device is not null, returning");
                    AntPlus_Service.logWriter.Flush();
                    return;
                }
                
                AntPlus_Service.logWriter.WriteLine($"Device attached: {deviceDescription} (ID: {deviceId})");
                AntPlus_Service.logWriter.Flush();
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
                    AntPlus_Service.logWriter.WriteLine($"Device detached: {deviceDescription} (ID: {deviceId})");
                    AntPlus_Service.logWriter.Flush();
                    HandleAttachedDevices();
                }
            }catch (Exception ex)
            {
                AntPlus_Service.logWriter.WriteLine($"Error occured while reading disconnected device info: {ex.Message}");
                AntPlus_Service.logWriter.Flush();
            }

        }
        static void ListenForAntPlusData(string deviceId)
        {

            try
            {
                device = new ANT_Device();   // Create a device instance using the automatic constructor (automatic detection of USB device number and baud rate)
                device.enableRxExtendedMessages(true);
                // device0.deviceResponse += new ANT_Device.dDeviceResponseHandler(DeviceResponse);    // Add device response function to receive protocol event messages
                byte[] userNetworkKey = { 0xB9, 0xA5, 0x21, 0xFB, 0xBD, 0x72, 0xC3, 0x45 };
                //byte[] userNetworkKey = { 0, 0, 0, 0, 0, 0, 0, 0 };
                device.setNetworkKey(0, userNetworkKey);
                for(int i = 0; i < 8; i++)
                {
                    channels[i] = device.getChannel(i);    // Get channel from ANT device
                    channels[i].channelResponse += new dChannelResponseHandler(ChannelResponse);  // Add channel response function to receive channel event messages
                                                                                                  //channel.rawChannelResponse += new dRawChannelResponseHandler(rawChannelResponse);
                    channels[i].assignChannel(ANT_ReferenceLibrary.ChannelType.BASE_Slave_Receive_0x00, 0, 500);
                    channels[i].setChannelID(0, false, 0, 0);
                    channels[i].setChannelFreq(57);
                    channels[i].setChannelPeriod(8070);

                    var timeout = 2.5 / 2.5;
                    int timeoutValue = (int)timeout;
                    channels[i].setChannelSearchTimeout((byte)timeoutValue);
                    channels[i].openChannel();
                }
                
                //AntPlus_Service.eventLog.WriteEntry("Opened channel, Ready to Listen");
                AntPlus_Service.logWriter.WriteLine("Opened channel, Ready to Listen");
                AntPlus_Service.logWriter.Flush();
                AntPlus_Service.logWriter.WriteLine(device);
                AntPlus_Service.logWriter.Flush();
            }
            catch(Exception e)
            {
                AntPlus_Service.logWriter.WriteLine($"Listening on device failed with error {e.Message}");
                AntPlus_Service.logWriter.Flush();
            }
        }

        static void ResetChannel(byte channelNo)
        {
            ANT_Channel channel = channels[channelNo];
            channel.assignChannel(ANT_ReferenceLibrary.ChannelType.BASE_Slave_Receive_0x00, 0, 500);
            channel.setChannelID(0, false, 0, 0);
            channel.setChannelFreq(57);
            channel.setChannelPeriod(8070);
            byte[] userNetworkKey = { 0xB9, 0xA5, 0x21, 0xFB, 0xBD, 0x72, 0xC3, 0x45 };
            //byte[] userNetworkKey = { 0, 0, 0, 0, 0, 0, 0, 0 };
            //device.setNetworkKey(0, userNetworkKey);
            var timeout = 2.5 / 2.5;
            int timeoutValue = (int)timeout;
            channel.setChannelSearchTimeout((byte)timeoutValue);
            channel.openChannel();
            //AntPlus_Service.eventLog.WriteEntry("Opened channel, Ready to Listen");
            //AntPlus_Service.logWriter.WriteLine("Resetted channel, Ready to Listen");
            //AntPlus_Service.logWriter.Flush();
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
                                AntPlus_Service.logWriter.WriteLine("Message is Extended");
                                did = response.getDeviceIDfromExt().deviceNumber;
                            }
                            else
                            {
                                AntPlus_Service.logWriter.WriteLine("Message is not Extended");
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
                                    AntPlus_Service.logWriter.WriteLine("None Relevant Data Recievved");
                                    AntPlus_Service.logWriter.Flush();
                                    var _contents = response.getDataPayload();
                                    AntPlus_Service.logWriter.WriteLine($"Page No: {_contents[0]}");
                                    AntPlus_Service.logWriter.Flush();
                                    AntPlus_Service.logWriter.WriteLine($"Other Values: {_contents[1]}, {_contents[2]}, {_contents[3]}, {_contents[4]},{_contents[5]},{_contents[6]},{_contents[7]},");
                                    AntPlus_Service.logWriter.Flush();
                                }
                            }else
                            {
                                AntPlus_Service.logWriter.WriteLine("None Relevant Data Recievved");
                                AntPlus_Service.logWriter.Flush();
                                var _contents = response.getDataPayload();
                                AntPlus_Service.logWriter.WriteLine($"Page No: {_contents[0]}");
                                AntPlus_Service.logWriter.Flush();
                                AntPlus_Service.logWriter.WriteLine($"Other Values: {_contents[1]}, {_contents[2]}, {_contents[3]}, {_contents[4]},{_contents[5]},{_contents[6]},{_contents[7]},");
                                AntPlus_Service.logWriter.Flush();
                            }
                            break;
                        }
                }
            }
            catch (Exception ex)
            {
                AntPlus_Service.logWriter.WriteLine($"Channel response processing failed with error {ex.Message}");
                AntPlus_Service.logWriter.Flush();
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

        static void rawChannelResponse(ANT_Device.ANTMessage message, ushort msize)
        {
            int id = message.msgID;
            byte[] contents = message.ucharBuf;
            AntPlus_Service.logWriter.WriteLine($"recieved response with id {id}");
            AntPlus_Service.logWriter.Flush();
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
                        AntPlus_Service.logWriter.WriteLine($"Extended Device Num: {deviceNum}");
                        AntPlus_Service.logWriter.Flush();
                    }
                    else
                    {
                        deviceNum = ((contents[11] & 0xff) << 8) + (contents[10] & 0xff);
                        AntPlus_Service.logWriter.WriteLine($"Device Num: {deviceNum}");
                        AntPlus_Service.logWriter.Flush();
                    }
                    int pageNo = contents[1];
                    AntPlus_Service.logWriter.WriteLine($"Page No Recieved {pageNo}");
                    AntPlus_Service.logWriter.Flush();
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
                            AntPlus_Service.logWriter.WriteLine("None Relevant Data Recieved");
                            string combinedContents = string.Join(", ", contents.Select(b => b.ToString()));
                            AntPlus_Service.logWriter.WriteLine($"Page No: {contents[1]}");
                            AntPlus_Service.logWriter.WriteLine($"Other Values: {combinedContents}");
                            AntPlus_Service.logWriter.Flush();
                        }
                    }
                    else
                    {
                        AntPlus_Service.logWriter.WriteLine($"None Relevant Data Recieved");
                        string combinedContents = string.Join(", ", contents.Select(b => b.ToString()));
                        AntPlus_Service.logWriter.WriteLine($"Page No: {contents[1]}");
                        AntPlus_Service.logWriter.WriteLine($"Other Values: {combinedContents}");
                        AntPlus_Service.logWriter.Flush();
                    }
                }
                else
                {
                    AntPlus_Service.logWriter.WriteLine($"Invalid Message Id Recieved: {id}");
                    string combinedContents = string.Join(", ", contents.Select(b => b.ToString()));
                    AntPlus_Service.logWriter.WriteLine($"Page No: {contents[1]}");
                    AntPlus_Service.logWriter.WriteLine($"Other Values: {combinedContents}");
                    AntPlus_Service.logWriter.Flush();
                }

            }
            catch (Exception ex)
            {
                AntPlus_Service.logWriter.WriteLine($"Channel response processing failed with error {ex.Message}");
                AntPlus_Service.logWriter.Flush();
            }
        }

        static void LogChannelResponse(byte[] contents)
        {
            string combinedContents = string.Join(", ", contents.Select(b => b.ToString()));
            AntPlus_Service.logWriter.WriteLine($"Contents: {combinedContents}");
            AntPlus_Service.logWriter.Flush();
        }
    }
}
