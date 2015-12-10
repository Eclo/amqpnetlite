//  ------------------------------------------------------------------------------------
//  Copyright (c) Microsoft Corporation
//  All rights reserved. 
//  
//  Licensed under the Apache License, Version 2.0 (the ""License""); you may not use this 
//  file except in compliance with the License. You may obtain a copy of the License at 
//  http://www.apache.org/licenses/LICENSE-2.0  
//  
//  THIS CODE IS PROVIDED *AS IS* BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, 
//  EITHER EXPRESS OR IMPLIED, INCLUDING WITHOUT LIMITATION ANY IMPLIED WARRANTIES OR 
//  CONDITIONS OF TITLE, FITNESS FOR A PARTICULAR PURPOSE, MERCHANTABLITY OR 
//  NON-INFRINGEMENT. 
// 
//  See the Apache Version 2.0 License for specific language governing permissions and 
//  limitations under the License.
//  ------------------------------------------------------------------------------------

using System;
using Amqp;
using Microsoft.SPOT;
using System.Threading;
using Eclo.NETMF.SIM800H;
using Microsoft.SPOT.Hardware;
using System.IO.Ports;

namespace Device.SmallMemory
{
    public class Program
    {
        // this example program connects to an Azure IoT hub sends a couple of messages and waits for messages from


        // replace with IoT Hub name
        const string iotHubName = "<replace>";

        // replace with device name 
        const string device = "<replace>";

        // user/pass to be authenticated with Azure IoT hub
        // if using a shared access signature like SharedAccessSignature sr=myhub.azure-devices.net&sig=H4Rm2%2bjdBr84lq5KOddD9YpOSC8s7ZSe9SygErVuPe8%3d&se=1444444444&skn=iothubowner
        // user will be iothubowner and password the complete SAS string 
        const string authUser = "<replace>";
        const string authPassword = "<replace>";


        public static void Main()
        {
            InitializeSIM800H();

            Microsoft.SPOT.Debug.EnableGCMessages(false);

            // loop forever and output available RAM each 5 seconds
            while (true)
            {
                //Microsoft.SPOT.Debug.Print("Free RAM: " + Microsoft.SPOT.Debug.GC(false).ToString());

                Thread.Sleep(5000);
            };

        }

        static void Send()
        {
            const int nMsgs = 2;

            Client client = new Client(iotHubName + ".azure-devices.net", 5671, true, authUser + "@sas.root." + iotHubName, authPassword);

            int count = 0;
            ManualResetEvent done = new ManualResetEvent(false);
            Receiver receiver = client.GetReceiver("devices/" + device + "/messages/deviceBound");
            receiver.Start(20, (r, m) =>
            {
                r.Accept(m);
                if (++count >= nMsgs) done.Set();
            });




            Sender sender = client.GetSender("devices/" + device + "/messages/events");
            for (int i = 0; i < nMsgs; i++)
            {
                sender.Send(new Message() { Body = Guid.NewGuid().ToString() });
                Thread.Sleep(1000);
            }

            done.WaitOne(30000, false);

            while(true)
            {
                Thread.Sleep(5000);
            }

            sender.Close();
            receiver.Close();
            client.Close();
        }

        static void InitializeSIM800H()
        {
            // initialization of the module is very simple 
            // we just need to pass a serial port and an output signal to control the "power key" signal

            // SIM800H serial port
            SerialPort sim800SerialPort = new SerialPort("COM2");

            // SIM800H signal for "power key"
            OutputPort sim800PowerKey = new OutputPort(Cpu.Pin.GPIO_Pin6, false);

            Microsoft.SPOT.Debug.Print("... Configuring SIM800H ...");

            // configure SIM800H device
            SIM800H.Configure(sim800PowerKey, sim800SerialPort);
            // we'll be needing only two sockets
            //SIM800H.MaxSockets = 2;
            
            // add event handler to be aware of network registration status changes
            SIM800H.GsmNetworkRegistrationChanged += SIM800H_GsmNetworkRegistrationChanged;

            // add event handler to be aware of GPRS network registration status changes
            SIM800H.GprsNetworkRegistrationChanged += SIM800H_GprsNetworkRegistrationChanged;

            // because we need Internet connection the access point configuration (APN) is mandatory
            // the configuration depends on what your network operator requires
            // it may be just the access point name or it may require an user and password too
            // AccessPointConfiguration class provides a number of convenient options to create a new APN configuration
            SIM800H.AccessPointConfiguration = AccessPointConfiguration.Parse("internet.vodafone.pt|vodafone|vodafone");

            // async call to power on module 
            // in this example we are setting up a callback on a separate method
            SIM800H.PowerOnAsync(PowerOnCompleted);
            Microsoft.SPOT.Debug.Print("... Power on sequence started ...");
        }
        private static void PowerOnCompleted(IAsyncResult result)
        {
            // check result
            if (((PowerOnAsyncResult)result).Result == PowerStatus.On)
            {
                Debug.Print("... Power on sequence completed...");
            }
            else
            {
                // something went wrong...
                Debug.Print("### Power on sequence FAILED ###");
            }
        }

        private static void SIM800H_GsmNetworkRegistrationChanged(NetworkRegistrationState networkState)
        {
        }

        private static void SIM800H_GprsNetworkRegistrationChanged(NetworkRegistrationState networkState)
        {
            if (networkState == NetworkRegistrationState.Registered)
            {
                // SIM800H is registered with GPRS network so we can request an Internet connection now

                // add event handler to know when we have an active Internet connection 
                // remove it first so we don't have duplicate calls in case a new successful registration occurs 
                SIM800H.GprsProvider.GprsIpAppsBearerStateChanged -= GprsProvider_GprsIpAppsBearerStateChanged;
                SIM800H.GprsProvider.GprsIpAppsBearerStateChanged += GprsProvider_GprsIpAppsBearerStateChanged;

                // async call to GPRS provider to open the GPRS bearer
                // we can set a callback here to get the result of that request and act accordingly
                // or we can manage this in the GprsIpAppsBearerStateChanged event handler that we've already setup during the configuration
                SIM800H.GprsProvider.OpenBearerAsync();
            }
        }

        private static void GprsProvider_GprsIpAppsBearerStateChanged(bool isOpen)
        {
            if (isOpen)
            {
                // launch a new thread to download weather data
                new Thread(() =>
                {
                    Thread.Sleep(1000);

                    UpdateRTCFromNetwork();

                    // async open GPRS connection to use sockets
                    SIM800H.GprsProvider.OpenGprsConnectionAsync((ar) =>
                    {
                        ConnectGprsAsyncResult result = (ConnectGprsAsyncResult)ar;
                        if (!(result.Result == ConnectGprsResult.Open ||
                            result.Result == ConnectGprsResult.AlreadyOpen))
                        {
                            // failed to open GPRS sockets connection
                            // TBD
                            Debug.Print("*** FAILED TO OPEN GPRS CONNECTION ***");
                        }
                        else
                        {
                            // OK with connecting to IoT hub now
                            Send();
                        }
                    });

                }).Start();
            }
        }
        static void UpdateRTCFromNetwork()
        {
            Debug.Print("... requesting time from NTP server ...");

            //////////////////////////////////////////////////////////////////////////////////////////////////////////////
            //////////////////////////////////////////////////////////////////////////////////////////////////////////////
            // the following code block uses an async call to SNTP client which should be OK for most of the use scenarios
            // check an alternative in the code block commented bellow 

            var result = SIM800H.SntpClient.SyncNetworkTimeAsync("time.nist.gov", TimeSpan.Zero).End();
            //check result
            if (result == SyncResult.SyncSuccessful)
            {
                // get current date time and update RTC
                DateTime rtcValue = SIM800H.GetDateTime();
                // set framework date time
                Utility.SetLocalTime(rtcValue);

                Debug.Print("!!! new time from NTP server: " + rtcValue.ToString());

                // done here, dispose SNTP client to free up memory
                SIM800H.SntpClient.Dispose();
                SIM800H.SntpClient = null;

                return;
            }
            else
            {
                Debug.Print("### failed to get time from NTP server ###");
            }
        }

    }
}
