using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;

public class CalibrationHost : IDisposable
{
    private AnonymousPipeServerStream _pipeServer;
    private StreamWriter _pipeWriter;
    private Process _child;
    private readonly List<(double X, double Y)> _rawSamples = new();
    private readonly int _expectedPoints = 5; // TL, TR, BR, BL, CENTER
    private readonly int _screenW;
    private readonly int _screenH;

    public CalibrationHost(int screenWidth, int screenHeight)
    {
        _screenW = screenWidth;
        _screenH = screenHeight;
    }

    public void Start(string calibratorExePath)
    {
        _pipeServer = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
        var clientHandle = _pipeServer.GetClientHandleAsString();

        var psi = new ProcessStartInfo
        {
            FileName = calibratorExePath,
            Arguments = clientHandle,
            UseShellExecute = false
        };

        _child = Process.Start(psi);
        _pipeServer.DisposeLocalCopyOfClientHandle();
        _pipeWriter = new StreamWriter(_pipeServer, Encoding.ASCII) { AutoFlush = true };

        // At this point the calibrator UI shows the first corner.
    }

    /// Call this when a valid shot is detected and the RAW coordinates (0..max sensor) are available
    public void OnTriggerRaw(double rawX, double rawY)
    {
        if (_rawSamples.Count >= _expectedPoints) return;

        _rawSamples.Add((rawX, rawY));

        // Advance the target in the EXE (any byte will do)
        _pipeWriter.Write('x');

        if (_rawSamples.Count == _expectedPoints)
        {
            FinishAndSave();
        }
    }

    private void FinishAndSave()
    {
        // 1) Destination points on screen (in pixels). Adjust the margins to leave a "safe area".
        var dst = new (double X, double Y)[]
        {
            (0, 0),                  // TL
            (_screenW - 1, 0),       // TR
            (_screenW - 1, _screenH - 1), // BR
            (0, _screenH - 1),       // BL
            (_screenW / 2.0, _screenH / 2.0) // CENTER
        };

        // 2) Compute the RAW->SCREEN homography from the 4 corners; the center is used to validate/refine
        var H = Homography.Solve(
            new (double X, double Y)[]{ _rawSamples[0], _rawSamples[1], _rawSamples[2], _rawSamples[3] },
            new (double X, double Y)[]{ dst[0],        dst[1],        dst[2],        dst[3] }
        );

        // (Optional) check the error at the center point:
        var centerMapped = Homography.Apply(H, _rawSamples[4].X, _rawSamples[4].Y);
        // The error could be measured and, if high, the user asked to repeat.

        // 3) Save to a simple .txt (3x3 matrix and screen size)
        SaveCalibrationTxt("calibration.txt", H, _screenW, _screenH);

        // 4) Close the calibrator UI
        try { _child?.CloseMainWindow(); } catch {}
        Dispose();
    }

    private static void SaveCalibrationTxt(string path, double[] H, int w, int h)
    {
        // Simple format; if the "classic" TXT shows up later, this can be adapted to that exact format.
        // H: h00 h01 h02; h10 h11 h12; h20 h21 h22
        var sb = new StringBuilder();
        sb.AppendLine("# GunCon3 Calibration (RAW->SCREEN Homography)");
        sb.AppendLine($"screen_width={w}");
        sb.AppendLine($"screen_height={h}");
        sb.AppendLine($"H={H[0]:R},{H[1]:R},{H[2]:R},{H[3]:R},{H[4]:R},{H[5]:R},{H[6]:R},{H[7]:R},{H[8]:R}");
        File.WriteAllText(path, sb.ToString());
    }

    public void Dispose()
    {
        try { _pipeWriter?.Dispose(); } catch {}
        try { _pipeServer?.Dispose(); } catch {}
        try { if (_child != null && !_child.HasExited) _child.Kill(); } catch {}
    }
}
