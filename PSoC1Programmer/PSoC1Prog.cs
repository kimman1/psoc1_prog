using System.Diagnostics;
using System.Globalization;

namespace PSoC1Programmer;

/// <summary>
/// Wraps the Python programmer.py script as a subprocess.
/// Parses stdout/stderr to return results to the GUI.
/// </summary>
public class PSoC1Prog : IDisposable
{
    private readonly string _port;
    private readonly string _scriptPath;

    public PSoC1Prog(string portName)
    {
        _port = portName;

        // Find programmer.py relative to this executable
        var asmDir = Path.GetDirectoryName(typeof(PSoC1Prog).Assembly.Location)
                     ?? Directory.GetCurrentDirectory();
        _scriptPath = Path.GetFullPath(Path.Combine(asmDir, "..", "..", "..", "..", "programmer.py"));

        if (!File.Exists(_scriptPath))
        {
            // Fallback: look in the repo root
            _scriptPath = Path.GetFullPath(Path.Combine(asmDir, "..", "..", "..", "..", "..", "programmer.py"));
        }
        if (!File.Exists(_scriptPath))
        {
            throw new FileNotFoundException(
                $"Không tìm thấy programmer.py. Đặt file bên cạnh exe hoặc trong thư mục gốc psoc1_prog.\nĐã tìm: {_scriptPath}");
        }
    }

    /// <summary>
    /// Run programmer.py with given args, capture stdout. Throws on non-zero exit.
    /// </summary>
    private string RunPython(string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "python",
            Arguments = $"\"{_scriptPath}\" {_port} {args}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };

        using var proc = new Process { StartInfo = psi };
        proc.Start();

        var stdout = proc.StandardOutput.ReadToEnd().Trim();
        var stderr = proc.StandardError.ReadToEnd().Trim();

        proc.WaitForExit();

        if (proc.ExitCode != 0)
        {
            var msg = stderr.Split('\n').LastOrDefault(l => l.Contains("Error") || l.Contains("error") || l.Contains("fail"))
                      ?? stderr.Replace("\r", "").Split('\n', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()
                      ?? $"Process exited with code {proc.ExitCode}";
            throw new InvalidOperationException(msg.Trim());
        }

        return stdout;
    }

    /// <summary>
    /// Run programmer.py with given args, capture both stdout and stderr in real-time.
    /// Each line of stderr (prefixed "PSoC1_Prog:") is forwarded to the logger.
    /// </summary>
    private (string stdout, int exitCode) RunPythonWithLogging(string args, Action<string>? logger)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "python",
            Arguments = $"\"{_scriptPath}\" {_port} {args}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
        };

        using var proc = new Process { StartInfo = psi };
        proc.Start();

        // Read stderr line by line (real-time)
        var stderr = Task.Run(() =>
        {
            var sb = new System.Text.StringBuilder();
            while (!proc.StandardError.EndOfStream)
            {
                var line = proc.StandardError.ReadLine();
                if (line is not null)
                {
                    sb.AppendLine(line);
                    logger?.Invoke(line);
                }
            }
            return sb.ToString();
        });

        var stdout = proc.StandardOutput.ReadToEnd().Trim();
        proc.WaitForExit();
        stderr.Wait();

        return (stdout, proc.ExitCode);
    }

    public void Reinitialise()
    {
        RunPython("--init True");
    }

    public void ResetDevice()
    {
        RunPython("reset --reset");
    }

    private static readonly Dictionary<string, int> NameToId = DeviceIds
        .ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.OrdinalIgnoreCase);

    public int GetDeviceId()
    {
        var name = GetDeviceName();
        return NameToId.GetValueOrDefault(name, 0);
    }

    public static Dictionary<int, string> DeviceIds => new()
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

    public int GetFirmwareId()
    {
        var output = RunPython("fw");
        if (int.TryParse(output, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var fw))
            return fw;
        return 0;
    }

    public void EraseMemory()
    {
        RunPythonWithLogging("erase", null);
    }

    public byte[] ReadChecksum()
    {
        var output = RunPython("checksum");
        // output is like "c47a"
        var hex = output.Trim();
        if (hex.Length >= 4)
            return Convert.FromHexString(hex.PadLeft(4, '0'));
        return Array.Empty<byte>();
    }

    public string GetDeviceName()
    {
        var (stdout, exitCode) = RunPythonWithLogging("device", null);
        if (exitCode != 0)
            return "Unknown";
        return stdout;
    }

    public byte[] ReadMemory(int n = 64, int offset = 0x80)
    {
        // Create temp file for output
        var tempFile = Path.GetTempFileName();
        try
        {
            RunPython($"read -o \"{tempFile}\" --offset 0x{offset:X2} --count {n}");
            return File.ReadAllBytes(tempFile);
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    public void WriteProgram(byte[] data, Action<string>? logger)
    {
        throw new NotSupportedException("Use Flash() which calls programmer.py directly");
    }

    public void WriteSecure(byte[] data)
    {
        throw new NotSupportedException("Use Flash() which calls programmer.py directly");
    }

    /// <summary>
    /// Flash a .hex file to the device by calling programmer.py.
    /// </summary>
    public void Flash(string hexPath, bool verifyRead, Action<string>? logger)
    {
        var args = $"flash -i \"{hexPath}\"";
        if (verifyRead)
            args += " --read";
        args += " --reset";

        var (stdout, exitCode) = RunPythonWithLogging(args, logger);
        if (exitCode != 0)
            throw new InvalidOperationException("Flash thất bại!");
    }

    public void Dispose()
    {
        // No managed resources (subprocess is cleaned up per-call)
    }
}
