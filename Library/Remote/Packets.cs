﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;

namespace XLibrary.Remote
{
    public static class PacketType
    {
        public const byte Padding = 0x10;
        public const byte Generic = 0x20;
        public const byte Dat = 0x30;
        public const byte Sync = 0x40;
    }

    public class GenericPacket : G2Packet
    {
        const byte Packet_Item = 0x10;
        const byte Packet_Key = 0x20;
        const byte Packet_Value = 0x30;


        public string Name;
        public Dictionary<string, string> Data;

        public GenericPacket()
        {
        }
        
        public GenericPacket(string name)
        {
            Name = name;
        }

        public override byte[] Encode(G2Protocol protocol)
        {
            lock (protocol.WriteSection)
            {
                var packet = protocol.WritePacket(null, PacketType.Generic, UTF8Encoding.UTF8.GetBytes(Name));

                if(Data != null)
                    foreach (var item in Data)
                    {
                        var keyValuePair = protocol.WritePacket(packet, Packet_Item, null);
                        protocol.WritePacket(keyValuePair, Packet_Key, UTF8Encoding.UTF8.GetBytes(item.Key));
                        protocol.WritePacket(keyValuePair, Packet_Value, UTF8Encoding.UTF8.GetBytes(item.Value));
                    }               

                return protocol.WriteFinish();
            }
        }

        public static GenericPacket Decode(G2Header root)
        {
            var generic = new GenericPacket();

            if (G2Protocol.ReadPayload(root))
                generic.Name = UTF8Encoding.UTF8.GetString(root.Data, root.PayloadPos, root.PayloadSize);

            G2Protocol.ResetPacket(root);

            G2Header child = new G2Header(root.Data);

            while (G2Protocol.ReadNextChild(root, child) == G2ReadResult.PACKET_GOOD)
            {
                if (child.Name != Packet_Item)
                    continue;
                
                if(generic.Data == null)
                    generic.Data = new Dictionary<string, string>();

                string key = null;
                string value = null;

                G2Header sub = new G2Header(child.Data);

                while (G2Protocol.ReadNextChild(child, sub) == G2ReadResult.PACKET_GOOD)
                {
                    if (!G2Protocol.ReadPayload(sub))
                        continue;

                    if (sub.Name == Packet_Key)
                        key = UTF8Encoding.UTF8.GetString(sub.Data, sub.PayloadPos, sub.PayloadSize);
                    else if(sub.Name == Packet_Value)
                        value = UTF8Encoding.UTF8.GetString(sub.Data, sub.PayloadPos, sub.PayloadSize);
                }

                generic.Data[key] = value;           
            }

            return generic;
        }
    }

    public class DatPacket : G2Packet
    {
        const byte Packet_Pos = 0x10;
        const byte Packet_Data = 0x20;

        public long Pos;
        public byte[] Data;

        public DatPacket()
        {
        }

        public DatPacket(long pos, byte[] data)
        {
            Pos = pos;
            Data = data;
        }

        public override byte[] Encode(G2Protocol protocol)
        {
            lock (protocol.WriteSection)
            {
                var dat = protocol.WritePacket(null, PacketType.Dat, null);

                protocol.WritePacket(dat, Packet_Pos, BitConverter.GetBytes(Pos));
                protocol.WritePacket(dat, Packet_Data, Data);

                return protocol.WriteFinish();
            }
        }

        public static DatPacket Decode(G2Header root)
        {
            var dat = new DatPacket();

            G2Header child = new G2Header(root.Data);

            while (G2Protocol.ReadNextChild(root, child) == G2ReadResult.PACKET_GOOD)
            {
                if (!G2Protocol.ReadPayload(child))
                    continue;

                switch (child.Name)
                {
                    case Packet_Pos:
                        dat.Pos = BitConverter.ToInt64(child.Data, child.PayloadPos);
                        break;

                    case Packet_Data:
                        dat.Data = Utilities.ExtractBytes(child.Data, child.PayloadPos, child.PayloadSize);
                        break;
                }
            }

            return dat;
        }
    }

    public class SyncPacket : G2Packet
    {
        const byte Packet_Hits = 0x10;

        public HashSet<int> Hits;

        public override byte[] Encode(G2Protocol protocol)
        {
            lock (protocol.WriteSection)
            {
                var sync = protocol.WritePacket(null, PacketType.Sync, null);

                protocol.WritePacket(sync, Packet_Hits, Hits.ToBytes());

                return protocol.WriteFinish();
            }
        }

        public static SyncPacket Decode(G2Header root)
        {
            var sync = new SyncPacket();

            G2Header child = new G2Header(root.Data);

            while (G2Protocol.ReadNextChild(root, child) == G2ReadResult.PACKET_GOOD)
            {
                if (!G2Protocol.ReadPayload(child))
                    continue;

                switch (child.Name)
                {
                    case Packet_Hits:
                        sync.Hits = HashSetExt.FromBytes(child.Data, child.PayloadPos, child.PayloadSize);
                        break;
                }
            }

            return sync;
        }
    }

    public static class HashSetExt
    {
        public static byte[] ToBytes(this HashSet<int> set)
        {
            byte[] result = new byte[set.Count * 4];
            int i = 0;

            foreach (int id in set)
            {
                BitConverter.GetBytes(id).CopyTo(result, i * 4);
                i++;
            }

            return result;
        }

        public static HashSet<int> FromBytes(byte[] data, int pos, int size)
        {
            HashSet<int> result = new HashSet<int>();

            for (int i = pos; i < size; i += 4)
                result.Add(BitConverter.ToInt32(data, i));

            return result;
        }

                public static void Test()
        {
            HashSet<int> set = new HashSet<int>();
            set.Add(1); set.Add(22); set.Add(433); set.Add(766);

            var bytes = set.ToBytes();

            var checkSet = FromBytes(bytes, 0, bytes.Length);
        }
    }

    public class CryptPadding : G2Packet
    {
        public byte[] Filler;


        public override byte[] Encode(G2Protocol protocol)
        {
            lock (protocol.WriteSection)
            {
                protocol.WritePacket(null, PacketType.Padding, Filler);
                return protocol.WriteFinish();
            }
        }

        public static CryptPadding Decode(G2ReceivedPacket packet)
        {
            CryptPadding padding = new CryptPadding();
            return padding;
        }
    }
}