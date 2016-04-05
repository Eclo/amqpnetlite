﻿//  ------------------------------------------------------------------------------------
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
    using Microsoft.SPOT.Net.Security;
    using Eclo.NETMF.SIM800H;
    static class Extensions
    {
        public const int TransferFramePrefixSize = 29;

        // Frame extensions

        public static void WriteFrame(this GprsSocket socket, byte frameType, ushort channel, ulong code, List fields)
        {
            ByteBuffer buffer = new ByteBuffer(64, true);

            // frame header
            buffer.Append(FixedWidth.UInt);
            AmqpBitConverter.WriteUByte(buffer, 2);
            AmqpBitConverter.WriteUByte(buffer, (byte)frameType);
            AmqpBitConverter.WriteUShort(buffer, channel);

            // command
            AmqpBitConverter.WriteUByte(buffer, FormatCode.Described);
            Encoder.WriteULong(buffer, code, true);
            AmqpBitConverter.WriteUByte(buffer, FormatCode.List32);
            int sizeOffset = buffer.WritePos;
            buffer.Append(8);
            AmqpBitConverter.WriteInt(buffer.Buffer, sizeOffset + 4, fields.Count);
            for (int i = 0; i < fields.Count; i++)
            {
                Encoder.WriteObject(buffer, fields[i]);
            }
            AmqpBitConverter.WriteInt(buffer.Buffer, sizeOffset, buffer.Length - sizeOffset);
            AmqpBitConverter.WriteInt(buffer.Buffer, 0, buffer.Length); // frame size
            socket.Send(buffer.Buffer, buffer.Offset, buffer.Length);
        }

        public static void WriteTransferFrame(this GprsSocket socket, uint deliveryId, bool settled,
            ByteBuffer buffer, int maxFrameSize)
        {
            // payload should have bytes reserved for frame header and transfer
            int frameSize = Math.Min(buffer.Length + TransferFramePrefixSize, maxFrameSize);
            int payloadSize = frameSize - TransferFramePrefixSize;
            int offset = buffer.Offset - TransferFramePrefixSize;
            int pos = offset;

            // frame size
            buffer.Buffer[pos++] = (byte)(frameSize >> 24);
            buffer.Buffer[pos++] = (byte)(frameSize >> 16);
            buffer.Buffer[pos++] = (byte)(frameSize >> 8);
            buffer.Buffer[pos++] = (byte)frameSize;

            // DOF, type and channel
            buffer.Buffer[pos++] = 0x02;
            buffer.Buffer[pos++] = 0x00;
            buffer.Buffer[pos++] = 0x00;
            buffer.Buffer[pos++] = 0x00;

            // transfer(list8-size,count)
            buffer.Buffer[pos++] = 0x00;
            buffer.Buffer[pos++] = 0x53;
            buffer.Buffer[pos++] = 0x14;
            buffer.Buffer[pos++] = 0xc0;
            buffer.Buffer[pos++] = 0x10;
            buffer.Buffer[pos++] = 0x06;

            buffer.Buffer[pos++] = 0x43; // handle

            buffer.Buffer[pos++] = 0x70; // delivery id: uint
            buffer.Buffer[pos++] = (byte)(deliveryId >> 24);
            buffer.Buffer[pos++] = (byte)(deliveryId >> 16);
            buffer.Buffer[pos++] = (byte)(deliveryId >> 8);
            buffer.Buffer[pos++] = (byte)deliveryId;

            buffer.Buffer[pos++] = 0xa0; // delivery tag: bin8
            buffer.Buffer[pos++] = 0x04;
            buffer.Buffer[pos++] = (byte)(deliveryId >> 24);
            buffer.Buffer[pos++] = (byte)(deliveryId >> 16);
            buffer.Buffer[pos++] = (byte)(deliveryId >> 8);
            buffer.Buffer[pos++] = (byte)deliveryId;

            buffer.Buffer[pos++] = 0x43; // message-format
            buffer.Buffer[pos++] = settled ? (byte)0x41 : (byte)0x42;   // settled
            buffer.Buffer[pos++] = buffer.Length > payloadSize ? (byte)0x41 : (byte)0x42;   // more

            socket.Send(buffer.Buffer, offset, frameSize);
            buffer.Complete(payloadSize);
        }

        public static void ReadFrame(this GprsSocket socket, out byte frameType, out ushort channel,
            out ulong code, out List fields, out ByteBuffer payload)
        {
            Microsoft.SPOT.Debug.Print("ReadFrame");


            byte[] headerBuffer = socket.ReadFixedSizeBuffer(8);
            int size = AmqpBitConverter.ReadInt(headerBuffer, 0);
            Microsoft.SPOT.Debug.Print("size: " + size);

            frameType = headerBuffer[5];    // TOOD: header EXT
            Microsoft.SPOT.Debug.Print("ftype: " + frameType);

            channel = (ushort)(headerBuffer[6] << 8 | headerBuffer[7]);
            Microsoft.SPOT.Debug.Print("channel: " + channel);

            size -= 8;
            if (size > 0)
            {
                byte[] frameBuffer = socket.ReadFixedSizeBuffer(size);
                ByteBuffer buffer = new ByteBuffer(frameBuffer, 0, size, size);
                Fx.AssertAndThrow(ErrorCode.ClientHandlInUse, Encoder.ReadFormatCode(buffer) == FormatCode.Described);

                code = Encoder.ReadULong(buffer, Encoder.ReadFormatCode(buffer));
                fields = Encoder.ReadList(buffer,Encoder.ReadFormatCode(buffer));
                if (buffer.Length > 0)
                {
                    payload = new ByteBuffer(buffer.Buffer, buffer.Offset, buffer.Length, buffer.Length);
                }
                else
                {
                    payload = null;
                }
            }
            else
            {
                code = 0;
                fields = null;
                payload = null;
            }
        }

        public static List ReadFrameBody(this GprsSocket socket, byte frameType, ushort channel, ulong code)
        {
            Microsoft.SPOT.Debug.Print("ReadFrameBody");

            byte t;
            ushort c;
            ulong d;
            List f;
            ByteBuffer p;
            socket.ReadFrame(out t, out c, out d, out f, out p);
            Fx.AssertAndThrow(ErrorCode.ClientWaitTimeout, t == frameType);
            Fx.AssertAndThrow(ErrorCode.ClientInitializeWrongBodyCount, c == channel);
            Fx.AssertAndThrow(ErrorCode.ClientInitializeWrongSymbol, d == code);
            Fx.AssertAndThrow(ErrorCode.ClientInitializeHeaderCheckFailed, f != null);
            Fx.AssertAndThrow(ErrorCode.ClientInitializeSaslFailed, p == null);
            return f;
        }

        // Transport extensions

        public static byte[] ReadFixedSizeBuffer(this GprsSocket socket, int size)
        {
            Microsoft.SPOT.Debug.Print("ReadFixedSizeBuffer: " + size);

            // sanity check
            if(size > 512)
            {
                Microsoft.SPOT.Debug.Print("WTF?!");
            }

            byte[] buffer = new byte[size];
            int offset = 0;
            while (size > 0)
            {
                int bytes = socket.Receive(buffer, offset, size);
                offset += bytes;
                size -= bytes;
            }
            return buffer;
        }

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

        public static Symbol[] GetSymbolMultiple(object multiple)
        {
            Symbol[] array = multiple as Symbol[];
            if (array != null)
            {
                return array;
            }

            Symbol symbol = multiple as Symbol;
            if (symbol != null)
            {
                return new Symbol[] { symbol };
            }

            throw new Exception("object is not a multiple type");
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
            return socket.Read(buffer, offset, count);
        }
    }
}