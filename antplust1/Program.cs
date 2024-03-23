/*
This software is subject to the license described in the License.txt file
included with this software distribution. You may not use this file except
in compliance with this license.

Copyright (c) Dynastream Innovations Inc. 2016
All rights reserved.
*/

//////////////////////////////////////////////////////////////////////////
// To use the managed library, you must:
// 1. Import ANT_NET.dll as a reference
// 2. Reference the ANT_Managed_Library namespace
// 3. Include the following files in the working directory of your application:
//  - DSI_CP310xManufacturing_3_1.dll
//  - DSI_SiUSBXp_3_1.dll
//  - ANT_WrappedLib.dll
//  - ANT_NET.dll
//////////////////////////////////////////////////////////////////////////

#define ENABLE_EXTENDED_MESSAGES // Un - coment to enable extended messages

using System;
using System.Text;
using ANT_Managed_Library;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Management;
using System.IO;

namespace ANT_Console_Demo
{
    class demo
    {
        static readonly byte CHANNEL_TYPE_INVALID = 2;

        static readonly byte USER_ANT_CHANNEL = 0;         // ANT Channel to use
        static readonly ushort USER_DEVICENUM = 49;        // Device number    
        static readonly byte USER_DEVICETYPE = 1;          // Device type
        static readonly byte USER_TRANSTYPE = 1;           // Transmission type

        static readonly byte USER_RADIOFREQ = 35;          // RF Frequency + 2400 MHz
        static readonly ushort USER_CHANNELPERIOD = 8192;  // Channel Period (8192/32768)s period = 4Hz

        static readonly byte[] USER_NETWORK_KEY = { 0, 0, 0, 0, 0, 0, 0, 0 };
        static readonly byte USER_NETWORK_NUM = 0;         // The network key is assigned to this network number

        static ANT_Device device0;
        static ANT_Channel channel0;
        static ANT_ReferenceLibrary.ChannelType channelType;
        static byte[] txBuffer = { 0, 0, 0, 0, 0, 0, 0, 0 };
        static bool bDone;
        static bool bDisplay;
        static bool bBroadcasting;
        static int iIndex = 0;
        private static StreamWriter Consolee;


        ////////////////////////////////////////////////////////////////////////////////
        // Main
        //
        // Usage:
        //
        // c:\demo_net.exe [channel_type]
        //
        // ... where
        // channel_type:  Master = 0, Slave = 1
        //
        // ... example
        //
        // c:\demo_net.exe 0
        // 
        // will connect to an ANT USB stick open a Master channel
        //
        // If optional arguements are not supplied, user will 
        // be prompted to enter these after the program starts
        //
        ////////////////////////////////////////////////////////////////////////////////


        static void ListenForAntPlusData(string deviceDescription)
        {
            try
            {
                device0 = new ANT_Device();   // Create a device instance using the automatic constructor (automatic detection of USB device number and baud rate)
                // device0.deviceResponse += new ANT_Device.dDeviceResponseHandler(DeviceResponse);    // Add device response function to receive protocol event messages

                channel0 = device0.getChannel(0);    // Get channel from ANT device
                channel0.channelResponse += new dChannelResponseHandler(ChannelResponse);  // Add channel response function to receive channel event messages
                
                channel0.assignChannel(ANT_ReferenceLibrary.ChannelType.BASE_Slave_Receive_0x00, 0, 500);
                channel0.setChannelID(0, false, 0, 0);
                channel0.setChannelFreq(57);
                channel0.setChannelPeriod(8070);
                byte[] userNetworkKey = { 0xB9, 0xA5, 0x21, 0xFB, 0xBD, 0x72, 0xC3, 0x45 };
                //byte[] userNetworkKey = { 0, 0, 0, 0, 0, 0, 0, 0 };
                device0.setNetworkKey(0, userNetworkKey);
                var timeout = 30 / 2.5;
                int timeoutValue = (int)timeout;
                channel0.setChannelSearchTimeout((byte)timeoutValue);
                
                channel0.openChannel();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        static void HandleAttachedDevices()
        {
            var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_PnPEntity WHERE DeviceID LIKE '%USB%'");
            foreach (var device in searcher.Get())
            {
                string deviceId = device["DeviceID"].ToString();
                // Filter for devices with the ANT+ VID (and optionally PID)if (deviceId.Contains(antVid))
                if (deviceId.Contains("VID_0FCF") && deviceId.Contains("PID_1009"))
                {
                    ListenForAntPlusData("");
                    Console.WriteLine($"Listening on device with id: {deviceId}");
                }
            }
        }

        static async Task Main(string[] args)
        {
            Consolee = new StreamWriter("aplifit.log", true);
            //HandleAttachedDevices();
            ListenForAntPlusData("");
            Console.WriteLine("listening on connected devices");
            Console.ReadKey();
            

            /*byte ucChannelType = CHANNEL_TYPE_INVALID;

            if (args.Length > 0)
            {
                ucChannelType = byte.Parse(args[0]);
            }

            try
            {
                Init();
                Start(ucChannelType);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Demo failed with exception: \n" + ex.Message);
            }*/
            /*
            var client = new SocketIOClient.SocketIO("https://app-pre.aplifitplay.com/lessons");

            client.OnConnected += async (sender, e) =>
            {
                await SendDataToServer(client);
            };

            client.On("ackEvent", response =>
            {
                Console.WriteLine($"Server response: {response}");
            });
            


            Console.WriteLine("Trying to connect async");
            await client.ConnectAsync();
            Console.ReadKey(); */
            /*            Console.WriteLine("creating device");
                        ANT_Device device0 = new ANT_Device();   // Create a device instance using the automatic constructor (automatic detection of USB device number and baud rate)
                                                                 // device0.deviceResponse += new ANT_Device.dDeviceResponseHandler(DeviceResponse);    // Add device response function to receive protocol event messages
                        Console.WriteLine("created device");
                        ANT_Channel channel0 = device0.getChannel(0);    // Get channel from ANT device
                        channel0.channelResponse += new dChannelResponseHandler(ChannelResponse);  // Add channel response function to receive channel event messages

                        channel0.assignChannel(ANT_ReferenceLibrary.ChannelType.BASE_Slave_Receive_0x00, 0, 500);
                        channel0.setChannelID(0, false, 0, 0);
                        channel0.setChannelFreq(57);
                        channel0.setChannelPeriod(8070);
                        byte[] userNetworkKey = { 0xB9, 0xA5, 0x21, 0xFB, 0xBD, 0x72, 0xC3, 0x45 };
                        //byte[] userNetworkKey = { 0, 0, 0, 0, 0, 0, 0, 0 };
                        device0.setNetworkKey(0, userNetworkKey);
                        Console.WriteLine("opening channel");
                        channel0.openChannel();
                        Console.WriteLine("channel opened"); */
        }

        static async Task SendDataToServer(SocketIOClient.SocketIO client)
        {
            // Data to send to the server
            var eventData = new
            {
                id = 0,
                deviceId = 0,
            };
            string jsonData = JsonConvert.SerializeObject(eventData);
            // Send the data with a specific event name and listen for acknowledgement
            await client.EmitAsync("antenna-data", response =>
            {
                Console.WriteLine($"Acknowledgement received: {response}");
                Console.ReadKey();
            }, jsonData);

            Console.WriteLine("Data sent to server");
        }




        ////////////////////////////////////////////////////////////////////////////////
        // Init
        //
        // Initialize demo parameters.
        //
        ////////////////////////////////////////////////////////////////////////////////
        static void Init()
        {
            try
            {
                Console.WriteLine("Attempting to connect to an ANT USB device...");
                device0 = new ANT_Device();   // Create a device instance using the automatic constructor (automatic detection of USB device number and baud rate)
                //device0.deviceResponse += new ANT_Device.dDeviceResponseHandler(DeviceResponse);    // Add device response function to receive protocol event messages

                channel0 = device0.getChannel(USER_ANT_CHANNEL);    // Get channel from ANT device
                channel0.channelResponse += new dChannelResponseHandler(ChannelResponse);  // Add channel response function to receive channel event messages
                Console.WriteLine("Initialization was successful!");
            }
            catch (Exception ex)
            {
                if (device0 == null)    // Unable to connect to ANT
                {
                    throw new Exception("Could not connect to any device.\n" +
                    "Details: \n   " + ex.Message);
                }
                else
                {
                    throw new Exception("Error connecting to ANT: " + ex.Message);
                }
            }
        }


        ////////////////////////////////////////////////////////////////////////////////
        // Start
        //
        // Start the demo program.
        // 
        // ucChannelType_:  ANT Channel Type. 0 = Master, 1 = Slave
        //                  If not specified, 2 is passed in as invalid.
        ////////////////////////////////////////////////////////////////////////////////
        

        ////////////////////////////////////////////////////////////////////////////////
        // ConfigureANT
        //
        // Resets the system, configures the ANT channel and starts the demo
        ////////////////////////////////////////////////////////////////////////////////
        private static void ConfigureANT()
        {
            Console.WriteLine("Resetting module...");
            device0.ResetSystem();     // Soft reset
            System.Threading.Thread.Sleep(500);    // Delay 500ms after a reset

            // If you call the setup functions specifying a wait time, you can check the return value for success or failure of the command
            // This function is blocking - the thread will be blocked while waiting for a response.
            // 500ms is usually a safe value to ensure you wait long enough for any response
            // If you do not specify a wait time, the command is simply sent, and you have to monitor the protocol events for the response,
            Console.WriteLine("Setting network key...");
            if (device0.setNetworkKey(USER_NETWORK_NUM, USER_NETWORK_KEY, 500))
                Console.WriteLine("Network key set");
            else
                throw new Exception("Error configuring network key");

            Console.WriteLine("Assigning channel...");
            if (channel0.assignChannel(channelType, USER_NETWORK_NUM, 500))
                Console.WriteLine("Channel assigned");
            else
                throw new Exception("Error assigning channel");

            Console.WriteLine("Setting Channel ID...");
            if (channel0.setChannelID(USER_DEVICENUM, false, USER_DEVICETYPE, USER_TRANSTYPE, 500))  // Not using pairing bit
                Console.WriteLine("Channel ID set");
            else
                throw new Exception("Error configuring Channel ID");

            Console.WriteLine("Setting Radio Frequency...");
            if (channel0.setChannelFreq(USER_RADIOFREQ, 500))
                Console.WriteLine("Radio Frequency set");
            else
                throw new Exception("Error configuring Radio Frequency");

            Console.WriteLine("Setting Channel Period...");
            if (channel0.setChannelPeriod(USER_CHANNELPERIOD, 500))
                Console.WriteLine("Channel Period set");
            else
                throw new Exception("Error configuring Channel Period");

            Console.WriteLine("Opening channel...");
            bBroadcasting = true;
            if (channel0.openChannel(500))
            {
                Console.WriteLine("Channel opened");
            }
            else
            {
                bBroadcasting = false;
                throw new Exception("Error opening channel");
            }

#if (ENABLE_EXTENDED_MESSAGES)
            // Extended messages are not supported in all ANT devices, so
            // we will not wait for the response here, and instead will monitor 
            // the protocol events
            Console.WriteLine("Enabling extended messages...");
            device0.enableRxExtendedMessages(true);
#endif
        }

        static void ChannelResponse(ANT_Response response)
        {
            Consolee.WriteLine($"Timestamp: {DateTime.Now}");
            Consolee.Flush();
            Consolee.WriteLine("accepting response");
            Consolee.WriteLine($"{response.responseID}");
            //if(response.responseID == 64) { return; }
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
                                        Console.WriteLine($"Recieved EVENT TX 0X03");
                                        break;
                                    }
                                case ANT_ReferenceLibrary.ANTEventID.EVENT_RX_SEARCH_TIMEOUT_0x01:
                                    {
                                        Console.WriteLine("Search timed out, Restarting");
                                        channel0.openChannel();
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
                            if (true)
                            {
                                
                                var contents = response.getDataPayload();
                                Consolee.WriteLine($"Data Recieved: {contents.Length}");
                                if (contents[0] == 25)
                                {
                                    Consolee.WriteLine("reading fe trainer data");
                                    byte byte5 = contents[5];
                                    byte byte6 = contents[6];
                                    byte firstFourBitsByte6 = (byte)(byte6 & 0x0F);
                                    ushort combinedValue = (ushort)((firstFourBitsByte6 << 8) | byte5);
                                    Consolee.WriteLine($"Power: {combinedValue}");
                                    Consolee.WriteLine($"Cadence: {contents[2]}");
                                } else if (contents[0] == 16)
                                {
                                    if (contents[1] < 19 || contents[1] > 25)
                                    {
                                        byte byte6 = contents[6];
                                        byte byte7 = contents[7];
                                        ushort combinedValue = (ushort)((byte7 << 8) | byte6);
                                        Consolee.WriteLine($"Power: {combinedValue}");
                                        Consolee.WriteLine($"Cadence: {contents[3]}");
                                    }
                                    else
                                    {
                                        Consolee.WriteLine("This is fitness equipment");
                                    }
                                    
                                } else if (contents[0] == 0 || contents[0] == 128)
                                {
                                    Consolee.WriteLine($"bpm: {contents[7]}");
                                }
                                else
                                {
                                    Consolee.WriteLine($"Contents: {BitConverter.ToString(contents)}");
                                for (int i = 0; i < contents.Length; i++)
                                {
                                    Consolee.WriteLine($"{i}:{contents[i]}");
                                }
                                }
                                
                            }
                            else
                            {
                                /*string[] ac = { "|", "/", "_", "\\" };
                                Console.Write("Rx: " + ac[iIndex++] + "\r");
                                iIndex &= 3;*/
                            }
                            break;
                        }
                    default:
                        {
                            Consolee.WriteLine($"Default case: {response.responseID}");
                            break;
                        }
                }
            }
            catch (Exception ex)
            {
                Consolee.WriteLine(ex.ToString());
            }
        }



       

    }
}
