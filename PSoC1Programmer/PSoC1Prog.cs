using System.IO.Ports;

namespace PSoC1Programmer;

/// <summary>
/// Communicates with the Arduino-based PSoC 1 programmer over serial.
/// Ported from programmer.py PSoC1Prog class.
/// </summary>
public class PSoC1Prog : IDisposable
{
    private readonly SerialPort _serial;

    public static readonly Dictionary<int, string> DeviceIds = new()
    {
        [9] = "CY8C27143",
        [10] = "CY8C27243",
        [11] = "CY8C27443",
        [12] = "CY8C27543",
        [13] = "CY8C27643",
        [50] = "CY8C24123A",
        [51] = "CY8C24223A",
        [52] = "CY8C24423A",
        [2225] = "CY8C23533",
        [2224] = "CY8C23433",
        [2226] = "CY8C23033",
        [23] = "CY8C21123",
        [24] = "CY8C21223",
        [25] = "CY8C21323",
        [54] = "CY8C21234",
        [2103] = "CY8C21312",
        [55] = "CY8C21334",
        [56] = "CY8C21434",
        [2112] = "CY8C21512",
        [64] = "CY8C21534",
        [73] = "CY8C21634",
        [1848] = "CY8CTMG110_32LTXI",
        [1849] = "CY8CTMG110_00PVXI",
        [1592] = "CY8CTST110_32LTXI",
        [1593] = "CY8CTST110_00PVXI",
    };

    public PSoC1Prog(string portName)
    {
        _serial = new SerialPort(portName, 9600)
        {
            ReadTimeout = 2000,
            WriteTimeout = 2000,
        };
        _serial.Open();

        // Send newline and wait for prompt
        _serial.WriteLine("");
        _serial.ReadTo("> ");

        // Self-test: write 64 random bytes, read back, compare
        var buf1 = new byte[64];
        Random.Shared.NextBytes(buf1);
        WriteBlock(buf1);
        var buf2 = ReadBlock();
        if (!buf1.AsSpan().SequenceEqual(buf2))
        {
            _serial.Close();
            throw new InvalidOperationException(
                $"Programmer self-test failed!\nSent: {Convert.ToHexString(buf1)}\nRecv: {Convert.ToHexString(buf2)}");
        }
    }

    /// <summary>
    /// Read exactly <paramref name="count"/> bytes from serial, looping until all are received.
    /// </summary>
    private void ReadExactly(byte[] buffer, int offset, int count)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = _serial.Read(buffer, offset + totalRead, count - totalRead);
            if (read == 0)
                throw new InvalidOperationException(
                    $"Serial timeout: expected {count} bytes, got {totalRead}");
            totalRead += read;
        }
    }

    private void Wait()
    {
        var buf = new byte[4];
        ReadExactly(buf, 0, 4);
        if (buf[0] != '\r' || buf[1] != '\n' || buf[2] != '>' || buf[3] != ' ')
            throw new InvalidOperationException(
                $"Programmer returned unexpected result: {Convert.ToHexString(buf)}");
    }

    public void Reinitialise()
    {
        _serial.Write("i");
        Wait();
    }

    public void ResetDevice()
    {
        _serial.Write("a");
        Wait();
    }

    public int GetDeviceId()
    {
        _serial.Write("D");
        var res = new byte[2];
        ReadExactly(res, 0, 2);
        Wait();
        return (res[1] << 8) | res[0];
    }

    public int GetFirmwareId()
    {
        _serial.Write("F");
        var res = new byte[2];
        ReadExactly(res, 0, 2);
        Wait();
        return (res[1] << 8) | res[0];
    }

    public void EraseMemory()
    {
        _serial.Write("e");
        Wait();
    }

    public byte[] ReadChecksum()
    {
        _serial.Write("C");
        var res = new byte[2];
        ReadExactly(res, 0, 2);
        Wait();
        return new byte[] { res[1], res[0] };
    }

    public byte[] ReadMemory(int n = 64, int offset = 0x80)
    {
        var blocks = new List<byte>();
        for (int i = 0; i < n; i++)
        {
            _serial.Write("r");
            _serial.Write(new byte[] { (byte)i, (byte)offset }, 0, 2);
            Wait();
            var block = ReadBlock();
            blocks.AddRange(block);
            OnProgress?.Invoke($"Đọc 0x{i:X2} [{(i + 1) * 100.0 / n:F2}%]");
        }
        return blocks.ToArray();
    }

    private void WriteBlock(byte[] data)
    {
        if (data.Length != 64)
            throw new ArgumentException("Data is not 64 bytes");
        _serial.Write("t");
        _serial.Write(data, 0, 64);
        Wait();
    }

    private byte[] ReadBlock()
    {
        _serial.Write("s");
        var blk = new byte[64];
        ReadExactly(blk, 0, 64);
        Wait();
        return blk;
    }

    public void WriteProgram(byte[] data, Action<string>? progress = null)
    {
        EraseMemory();
        int blocks = data.Length / 64;
        for (int i = 0; i < blocks; i++)
        {
            var block = data[(i * 64)..((i + 1) * 64)];
            WriteBlock(block);
            _serial.Write("w");
            _serial.Write(new byte[] { (byte)i }, 0, 1);
            Wait();
            progress?.Invoke($"Ghi 0x{i:X2} [{(i + 1) * 100.0 / blocks:F2}%]");
        }
    }

    public void WriteSecure(byte[] data)
    {
        WriteBlock(data);
        _serial.Write("x");
        Wait();
    }

    public string GetDeviceName()
    {
        int id = GetDeviceId();
        return DeviceIds.GetValueOrDefault(id, $"Unknown (0x{id:X4})");
    }

    /// <summary>
    /// Flash a .hex file to the device.
    /// </summary>
    public void Flash(string hexPath, bool verifyRead, Action<string>? logger)
    {
        logger?.Invoke($"Đang đọc file: {hexPath}");
        var hex = new IntelHex(hexPath);

        logger?.Invoke("Đang xoá bộ nhớ...");
        EraseMemory();

        logger?.Invoke("Đang ghi chương trình...");
        WriteProgram(hex.Program, logger);

        logger?.Invoke("Đang ghi secure...");
        WriteSecure(hex.Secure);

        logger?.Invoke("Đang kiểm tra checksum...");
        var csum = ReadChecksum();
        if (!csum.AsSpan().SequenceEqual(hex.Checksum))
            throw new InvalidOperationException(
                $"Checksum mismatch! Device={Convert.ToHexString(csum)} File={Convert.ToHexString(hex.Checksum)}");

        if (verifyRead)
        {
            logger?.Invoke("Đang đọc lại để xác minh...");
            var memData = ReadMemory();
            if (!memData.AsSpan().SequenceEqual(hex.Program))
                throw new InvalidOperationException("Chương trình ghi vào không khớp với dữ liệu trên thiết bị!");
        }

        logger?.Invoke("Thành công! ✓");
    }

    public event Action<string>? OnProgress;

    public void Dispose()
    {
        _serial?.Dispose();
    }
}
