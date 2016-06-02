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

namespace Amqp
{
    using System;
    using Amqp.Types;
    using Eclo.NETMF.SIM800H;
    static class Extensions
    {
        // Helper methods

        public static GprsSocket Connect(string host, int port, bool useSsl)
        {
            GprsSocket socket = null;
            SocketException exception = null;

            socket = new GprsSocket(ProtocolType.Tcp, useSsl);
            try
            {
                socket.Connect(host, port);
                exception = null;
            }
            catch (SocketException socketException)
            {
                exception = socketException;
                socket = null;
            }

            if (exception != null)
            {
                throw exception;
            }

            return socket;
        }

        public static void Write(this GprsSocket socket, byte[] buffer, int offset, int count)
        {
            socket.Send(buffer, offset, count);
        }

        public static void Flush(this GprsSocket socket)
        {
        }

        public static int Read(this GprsSocket socket, byte[] buffer, int offset, int count)
        {
            return socket.Receive(buffer, offset, count);
        }
    }
}