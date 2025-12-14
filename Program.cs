using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Runtime.InteropServices;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace CaM_Fishing
{
  [SuppressMessage("ReSharper", "RedundantAssignment")]
  internal class Program
  {
    // AutoIt DLL import for mouse automation
    [DllImport("AutoItX3.dll", SetLastError = true, CharSet = CharSet.Auto)]
#pragma warning disable SYSLIB1054
    private static extern int AU3_MouseClick([MarshalAs(UnmanagedType.LPWStr)] string button, int x, int y, int clicks, int speed);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
#pragma warning restore SYSLIB1054

    private const int VkF = 0x46;
    private const int SmCxscreen = 0;
    private const int SmCyscreen = 1;

    // === Config ===
    private const double NoFishTimeout = 1;
    private const double SessionWaitTime = 9;
    // how long until it detects that rod is retracted
    private const double RecoveryTimeout = 24;

    private static bool _botActive = true;
    private static double _lastToggleTime;
    private static double _lastFishTime;
    private static bool _waitingForNextSession;
    private static bool _recoveryTriggered;

    private static Mat? _tplWhite;
    private static Mat? _tplGold;
    private static int _screenWidth;
    private static int _screenHeight;

    private static void Main()
    {
      Console.WriteLine("Climb a mountain fishing bot :: Toggle F");
      Console.WriteLine("Starting in 8 seconds...");

      // Initialize timing
      _lastFishTime = GetCurrentTimeSeconds();

      // === Load templates ===
      _tplWhite = Cv2.ImRead("fish_white.png");
      _tplGold = Cv2.ImRead("fish_gold.png");

      if (_tplWhite.Empty() || _tplGold.Empty())
      {
        Console.WriteLine("Error: Could not load fish templates");
        return;
      }

      _screenWidth = GetSystemMetrics(SmCxscreen);
      _screenHeight = GetSystemMetrics(SmCyscreen);

      Thread.Sleep(8000);

      while (true)
      {
        var now = GetCurrentTimeSeconds();

        CheckToggleKey();

        if (!_botActive)
        {
          Thread.Sleep(50);
          continue;
        }

        // === After session ends, wait without doing anything
        if (_waitingForNextSession)
        {
          if (!_recoveryTriggered && now - _lastFishTime >= RecoveryTimeout)
          {
            // Accidents can happen
            ClickMouse(_screenWidth / 2, _screenHeight / 2);
            _recoveryTriggered = true;
            _lastFishTime = now;
            Thread.Sleep((int)(SessionWaitTime * 1000));
            _waitingForNextSession = false;
            continue;
          }

          if (now - _lastFishTime >= SessionWaitTime)
          {
            _waitingForNextSession = false;
            _recoveryTriggered = false;
          } else
          {
            Thread.Sleep(50);
            continue;
          }
        }

        // === Capture screen & convert to gray
        using var screenshot = CaptureScreen();
        using var frame = screenshot.ToMat();
        using var gray = new Mat();
        Cv2.CvtColor(frame, gray, ColorConversionCodes.BGR2GRAY);

        var fishClicked = false;

        // === Match gold fish (first priority)
        using (var tplGoldGray = new Mat())
        using (var resG = new Mat())
        {
          Cv2.CvtColor(_tplGold, tplGoldGray, ColorConversionCodes.BGR2GRAY);
          Cv2.MatchTemplate(gray, tplGoldGray, resG, TemplateMatchModes.CCoeffNormed);

          Cv2.MinMaxLoc(resG, out _, out var maxvG, out _, out var maxlocG);

          if (maxvG >= 0.88) // high threshold to avoid faded icons
          {
            var targetX = maxlocG.X + _tplGold.Width / 2;
            var targetY = maxlocG.Y + _tplGold.Height / 2;

            ClickMouse(targetX, targetY);
            _lastFishTime = now;
            fishClicked = true;
            continue;
          }
        }

        // === Match white fish (second priority)
        using (var tplWhiteGray = new Mat())
        using (var resW = new Mat())
        {
          Cv2.CvtColor(_tplWhite, tplWhiteGray, ColorConversionCodes.BGR2GRAY);
          Cv2.MatchTemplate(gray, tplWhiteGray, resW, TemplateMatchModes.CCoeffNormed);

          Cv2.MinMaxLoc(resW, out _, out var maxvW, out _, out var maxlocW);

          if (maxvW >= 0.88)
          {
            var targetX = maxlocW.X + _tplWhite.Width / 2;
            var targetY = maxlocW.Y + _tplWhite.Height / 2;
            ClickMouse(targetX, targetY);
            _lastFishTime = now;
            fishClicked = true;
            continue;
          }
        }

        // === No strong matches → likely end of session
        if (!fishClicked && now - _lastFishTime >= NoFishTimeout)
        {
          _waitingForNextSession = true;
        }

        Thread.Sleep(2); // faster frame rate (but still safe)
      }
    }

    private static void CheckToggleKey()
    {
      if ((GetAsyncKeyState(VkF) & 0x8000) == 0)
        return;

      var now = GetCurrentTimeSeconds();

      if (!(now - _lastToggleTime > 0.5)) // debounce keypresses
        return;

      _botActive = !_botActive;
      _lastToggleTime = now;

      Console.Beep(1000, 250); // 0.25 seconds
      Console.WriteLine($"Bot {(_botActive ? "activated" : "paused")}");
    }

    private static void ClickMouse(int x, int y)
    {
      var result = AU3_MouseClick("left", x, y, 1, -1);
      if (result != 0)
        return;
      Console.ForegroundColor = ConsoleColor.Yellow;
      Console.WriteLine("Warning: Mouse click may have failed");
      Console.ResetColor();
    }

    private static double GetCurrentTimeSeconds()
    {
      return DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
    }

    private static Bitmap CaptureScreen()
    {
      var bounds = new Rectangle(0, 0, _screenWidth, _screenHeight);
      var bitmap = new Bitmap(bounds.Width, bounds.Height);
      using var graphics = Graphics.FromImage(bitmap);
      graphics.CopyFromScreen(0, 0, 0, 0, bounds.Size);
      return bitmap;
    }
  }
}
