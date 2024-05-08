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
using System.Collections.Generic;
using System.Linq;


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
        static ANT_Channel[] channels = new ANT_Channel[8];
        static ANT_ReferenceLibrary.ChannelType channelType;
        static byte[] txBuffer = { 0, 0, 0, 0, 0, 0, 0, 0 };
        static bool bDone;
        static bool bDisplay;
        static bool bBroadcasting;
        static int iIndex = 0;
        private static StreamWriter Consolee;


        static void ListenForAntPlusData(string deviceDescription)
        {
            try
            {
                device0 = new ANT_Device();   // Create a device instance using the automatic constructor (automatic detection of USB device number and baud rate)
                // device0.deviceResponse += new ANT_Device.dDeviceResponseHandler(DeviceResponse);    // Add device response function to receive protocol event messages
                device0.enableRxExtendedMessages(true);
                byte[] userNetworkKey = { 0xB9, 0xA5, 0x21, 0xFB, 0xBD, 0x72, 0xC3, 0x45 };
                device0.setNetworkKey(0, userNetworkKey);
                for (int i = 0; i < 8; i++)
                {
                    channels[i] = device0.getChannel(i);    // Get channel from ANT device
                    channels[i].channelResponse += new dChannelResponseHandler(ChannelResponse);  // Add channel response function to receive channel event messages
                                                                                                  //channel0.rawChannelResponse += new dRawChannelResponseHandler(rawChannelResponse);
                    channels[i].assignChannel(ANT_ReferenceLibrary.ChannelType.BASE_Slave_Receive_0x00, 0, 500);
                    channels[i].setChannelID(0, false, 0, 0);
                    channels[i].setChannelFreq(57);
                    channels[i].setChannelPeriod(8050);
                    var timeout = 2.5 / 2.5;
                    int timeoutValue = (int)timeout;
                    channels[i].setChannelSearchTimeout((byte)timeoutValue);
                    channels[i].openChannel();
                }
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
                                        //Console.WriteLine("Search timed out, Restarting");
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
                            if (response.isExtended())
                            {
                                var id = response.getDeviceIDfromExt();
                                Console.WriteLine($"Device id is: {id.deviceNumber}");
                            }
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


        static void rawChannelResponse(ANT_Device.ANTMessage message, ushort msize)
        {
            int id = message.msgID;
            byte[] contents = message.ucharBuf;
            Console.WriteLine($"recieved response with id {id}");
            if(message.msgID == 64)
            {
                if (contents[2] == 1)
                {
                    Console.WriteLine("Search Timeout, Restarting");
                    //ResetChannel();
                }
            }
            if (contents[6] == 0)
            {
                int deviceNum = ((contents[6] & 0xff) << 24) + ((contents[5] & 0xff) << 16) + ((contents[13] & 0xff) << 8) + (contents[12] & 0xff);
                Console.WriteLine($"Extended Device Num: {deviceNum}");
            }
            else
            {
                int deviceNum = ((contents[13] & 0xff) << 8) + (contents[12] & 0xff);
                Console.WriteLine($"Device Num: {deviceNum}");
            }
            if (message.msgID == 78)
            {
                LogChannelResponse(contents);
                int pageNo = contents[1];
                Console.WriteLine($"Page No Recieved {pageNo}");
                List<int> validPageNumbers = new List<int> { 0, 128, 2, 4, 130, 132 };

                if(validPageNumbers.Contains(pageNo))
                {
                    Console.WriteLine($"Hear Rate Sensor, Ppm: {contents[8]}");
                }
                if(pageNo == 16)
                {
                    byte etbf = contents[2];
                    if (etbf > 19 && etbf < 25)
                    {
                        return;
                    }
                    byte byte6 = contents[7];
                    byte byte7 = contents[8];
                    ushort combinedValue = (ushort)((byte7 << 8) | byte6);
                    int power = combinedValue;
                    Console.WriteLine($"Power Sensor, Cadence: {contents[4]}, Power{power}");
                }
                if(pageNo == 25)
                {
                    byte byte5 = contents[6];
                    byte byte6 = contents[7];
                    byte firstFourBitsByte6 = (byte)(byte6 & 0x0F);
                    ushort combinedValue = (ushort)((firstFourBitsByte6 << 8) | byte5);
                    int power = combinedValue;
                    Console.WriteLine($"Fitness Equipment, Cadence: {contents[3]}, Power{power}");
                }
            }
            for (int i = 0; i < contents.Length; i++)
            {
                Console.WriteLine($"{i}: {contents[i]}");
            }
        }
        static void ResetChannel(byte channelNo)
        {
            //Console.WriteLine($"resetting channel: {channelNo}");
            ANT_Channel channel0 = channels[channelNo];
            channel0.assignChannel(ANT_ReferenceLibrary.ChannelType.BASE_Slave_Receive_0x00, 0, 500);
            channel0.setChannelID(0, false, 0, 0);
            channel0.setChannelFreq(57);
            channel0.setChannelPeriod(8050);
            byte[] userNetworkKey = { 0xB9, 0xA5, 0x21, 0xFB, 0xBD, 0x72, 0xC3, 0x45 };
            //byte[] userNetworkKey = { 0, 0, 0, 0, 0, 0, 0, 0 };
            device0.setNetworkKey(0, userNetworkKey);
            var timeout = 2.5 / 2.5;
            int timeoutValue = (int)timeout;
            channel0.setChannelSearchTimeout((byte)timeoutValue);
            channel0.openChannel();
            //AntPlus_Service.eventLog.WriteEntry("Opened channel, Ready to Listen");
        }

        static void LogChannelResponse(byte[] contents)
        {
            string combinedContents = string.Join(", ", contents.Select(b => b.ToString()));
            Consolee.WriteLine($"Contents: {combinedContents}");
            Consolee.Flush();
        }

    }
}
