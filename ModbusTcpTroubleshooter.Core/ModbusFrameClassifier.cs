namespace ModbusTcpTroubleshooter.Core;

public static class ModbusFrameClassifier
{
    // Identifies a complete supported ADU, never a service merely by its TCP port.
    public static string Classify(ReadOnlySpan<byte> payload)
    {
        if (payload.Length < 8 || payload[2] != 0 || payload[3] != 0) return "";
        var length = (payload[4] << 8) | payload[5];
        if (length is < 2 or > 254 || length + 6 > payload.Length) return "";
        var pdu = payload.Slice(7, length - 1);
        var fc = pdu[0];
        if ((fc & 0x80) != 0)
            return pdu.Length == 2 && pdu[1] is >= 1 and <= 11 ? "Exception" : "";
        if (fc is 1 or 2 or 3 or 4)
        {
            if (pdu.Length == 5)
            {
                var quantity = (pdu[3] << 8) | pdu[4];
                if (quantity > 0 && quantity <= (fc <= 2 ? 2000 : 125)) return "Request";
            }
            if (pdu.Length >= 3 && pdu[1] > 0 && pdu[1] + 2 == pdu.Length
                && (fc <= 2 || pdu[1] % 2 == 0)) return "Response";
        }
        if (fc is 5 or 6 && pdu.Length == 5) return "Echo";
        if (fc is 15 or 16)
        {
            if (pdu.Length == 5) return "Response";
            if (pdu.Length >= 7 && pdu[5] + 6 == pdu.Length)
            {
                var quantity = (pdu[3] << 8) | pdu[4];
                var bytes = fc == 15 ? (quantity + 7) / 8 : quantity * 2;
                if (quantity > 0 && quantity <= (fc == 15 ? 1968 : 123) && bytes == pdu[5]) return "Request";
            }
        }
        return "";
    }
}
