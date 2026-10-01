using System.Buffers.Binary;
using System.Net;
using System.Text;

namespace ModbusTcpTroubleshooter.App;

public sealed record NeighborAdvertisement(string Protocol, string SourceMac, string Chassis, string Port,
    string Name, string Description, string ManagementIp, int Ttl, DateTimeOffset SeenAt);

public static class NeighborDiscovery
{
    public static NeighborAdvertisement? Parse(ReadOnlySpan<byte> frame, DateTimeOffset at)
    {
        if (frame.Length < 14) return null;
        var mac = Convert.ToHexString(frame.Slice(6, 6));
        var offset = 14;
        var type = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(12, 2));
        for (var tags = 0; type is 0x8100 or 0x88a8 && tags < 2; tags++)
        {
            if (frame.Length < offset + 4) return null;
            type = BinaryPrimitives.ReadUInt16BigEndian(frame.Slice(offset + 2, 2));
            offset += 4;
        }
        if (type == 0x88cc) return ParseLldp(frame[offset..], mac, at);
        if (type is >= 12 and <= 1500 && frame.Length >= offset + 12
            && frame.Slice(offset, 8).SequenceEqual(new byte[] { 0xaa, 0xaa, 3, 0, 0, 0x0c, 0x20, 0 }))
            return ParseCdp(frame.Slice(offset + 8, Math.Min(type - 8, frame.Length - offset - 8)), mac, at);
        return null;
    }

    private static NeighborAdvertisement? ParseLldp(ReadOnlySpan<byte> data, string mac, DateTimeOffset at)
    {
        var chassis = ""; var port = ""; var name = ""; var description = ""; var ip = "";
        var ttl = -1; var index = 0; var mandatory = 0;
        while (index + 2 <= data.Length)
        {
            var header = BinaryPrimitives.ReadUInt16BigEndian(data[index..]);
            var type = header >> 9; var length = header & 511;
            index += 2;
            if (index + length > data.Length) return null;
            var value = data.Slice(index, length); index += length;
            if (mandatory < 3 && type != mandatory + 1) return null;
            if (type == 0) return length == 0 && mandatory == 3
                ? new("LLDP", mac, chassis, port, name, description, ip, ttl, at) : null;
            switch (type)
            {
                case 1 when length >= 2: chassis = Identifier(value, true); mandatory++; break;
                case 2 when length >= 2: port = Identifier(value, false); mandatory++; break;
                case 3 when length == 2: ttl = BinaryPrimitives.ReadUInt16BigEndian(value); mandatory++; break;
                case 5: name = Text(value); break;
                case 6: description = Text(value); break;
                case 8 when length >= 6 && value[0] == 5 && value[1] == 1:
                    ip = new IPAddress(value.Slice(2, 4)).ToString(); break;
            }
        }
        return null;
    }

    private static NeighborAdvertisement? ParseCdp(ReadOnlySpan<byte> data, string mac, DateTimeOffset at)
    {
        if (data.Length < 4 || data[0] is not (1 or 2)) return null;
        // CDP checksum is verified before trusting advertised identity.
        uint sum = 0;
        for (var i = 0; i + 1 < data.Length; i += 2) sum += BinaryPrimitives.ReadUInt16BigEndian(data[i..]);
        var valid = FoldChecksum(sum + (data.Length % 2 != 0 ? (uint)data[^1] << 8 : 0));
        // Cisco CDP handles an odd trailing octet differently from the Internet checksum.
        if (!valid && data.Length % 2 != 0)
            valid = FoldChecksum(sum + (data[^1] < 128 ? data[^1] : 0xff00u + data[^1] - 1));
        if (!valid) return null;
        var name = ""; var port = ""; var description = "";
        var index = 4;
        while (index + 4 <= data.Length)
        {
            var type = BinaryPrimitives.ReadUInt16BigEndian(data[index..]);
            var length = BinaryPrimitives.ReadUInt16BigEndian(data[(index + 2)..]);
            if (length < 4 || index + length > data.Length) return null;
            var value = data.Slice(index + 4, length - 4); index += length;
            if (type == 1) name = Text(value);
            if (type == 3) port = Text(value);
            if (type == 6) description = Text(value);
        }
        return index == data.Length && name.Length > 0 && port.Length > 0
            ? new("CDP", mac, name, port, name, description, "", data[1], at) : null;
    }

    private static string Identifier(ReadOnlySpan<byte> value, bool chassis)
    {
        if (value[0] == (chassis ? 4 : 3) && value.Length == 7) return Convert.ToHexString(value[1..]);
        if (value[0] == (chassis ? 5 : 4) && value.Length == 6 && value[1] == 1) return new IPAddress(value.Slice(2, 4)).ToString();
        return Text(value[1..]);
    }
    private static bool FoldChecksum(uint sum)
    {
        while (sum >> 16 != 0) sum = (sum & 65535) + (sum >> 16);
        return sum == 65535;
    }
    private static string Text(ReadOnlySpan<byte> value) => new(Encoding.UTF8.GetString(value[..Math.Min(256, value.Length)])
        .Select(c => char.IsControl(c) ? ' ' : c).ToArray());
}
