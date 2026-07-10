namespace PSoC1Programmer;

/// <summary>
/// Parses Intel HEX files for PSoC 1 firmware (program, secure, checksum).
/// Ported from programmer.py Program class.
/// </summary>
public class IntelHex
{
    public byte[] Program { get; private set; } = Array.Empty<byte>();
    public byte[] Secure { get; private set; } = Array.Empty<byte>();
    public byte[] Checksum { get; private set; } = Array.Empty<byte>();

    public IntelHex(string filePath)
    {
        Parse(filePath);
    }

    private void Parse(string filePath)
    {
        var program = new List<byte>();
        byte[]? secure = null;
        byte[]? checksum = null;
        int mode = 0;

        var lines = File.ReadAllLines(filePath);
        for (int i = 0; i < lines.Length; i++)
        {
            var l = lines[i].Trim();
            if (!l.StartsWith(':') || l.Length < 11)
                continue;

            int size = Convert.ToInt32(l[1..3], 16);
            int addr = Convert.ToInt32(l[3..7], 16);
            int rtype = Convert.ToInt32(l[7..9], 16);

            if (l.Length != 11 + size * 2)
                throw new FormatException(
                    $"{filePath}:{i} invalid number of characters, expected {size * 2} bytes, found {l.Length - 11}");

            var data = Convert.FromHexString(l[9..^2]);
            int csumCalc = (-Sum(Convert.FromHexString(l[1..^2]))) & 0x0FF;
            int csumGiven = Convert.ToInt32(l[^2..], 16);
            if (csumCalc != csumGiven)
                throw new FormatException(
                    $"{filePath}:{i} checksum failed, given 0x{csumGiven:X2} calculated 0x{csumCalc:X2}");

            switch (rtype)
            {
                case 0: // Data record
                    if (mode == 0)
                    {
                        if (addr != program.Count)
                            throw new FormatException($"{filePath}:{i} expected address to be continuous at 0x{addr:X4}, program length 0x{program.Count:X4}");
                        program.AddRange(data);
                    }
                    else if (mode == 16)
                    {
                        if (size != 64)
                            throw new FormatException($"{filePath}:{i} expected data size was 64 bytes, found {size}");
                        secure = data;
                    }
                    else if (mode == 32)
                    {
                        checksum = data;
                    }
                    break;

                case 4: // Extended Linear Address record
                    mode = (data[0] << 8) | data[1];
                    break;

                case 1: // End of File record
                    break;
            }
        }

        Program = program.ToArray();
        Secure = secure ?? Array.Empty<byte>();
        Checksum = checksum ?? Array.Empty<byte>();
    }

    private static int Sum(byte[] data)
    {
        int sum = 0;
        foreach (var b in data)
            sum += b;
        return sum;
    }
}
