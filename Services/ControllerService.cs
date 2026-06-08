using Windows.Gaming.Input;
using System.IO;
using System.Threading;

namespace RevoltLauncher.Services;

public interface IControllerService
{
    event EventHandler<ControllerButton> ButtonPressed;
    event EventHandler<ControllerButton> ButtonReleased;
    void Start();
    void Stop();
}

public enum ControllerButton
{
    DPadUp,
    DPadDown,
    DPadLeft,
    DPadRight,
    A,
    B,
    LeftShoulder,
    RightShoulder,
    XboxHome
}

public sealed class ControllerService : IControllerService, IDisposable
{
    private Gamepad? _activeGamepad;
    private CancellationTokenSource? _cts;
    private uint _lastButtons = 0;

    public event EventHandler<ControllerButton>? ButtonPressed;
    public event EventHandler<ControllerButton>? ButtonReleased;

    public ControllerService()
    {
        Gamepad.GamepadAdded += (s, e) => _activeGamepad = e;
        Gamepad.GamepadRemoved += (s, e) => _activeGamepad = Gamepad.Gamepads.FirstOrDefault();
    }

    public void Start()
    {
        if (_cts != null) return;
        _cts = new CancellationTokenSource();
        _activeGamepad = Gamepad.Gamepads.FirstOrDefault();
        Task.Run(() => PollingLoop(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    private async Task PollingLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var gamepad = _activeGamepad;
            if (gamepad == null)
            {
                _activeGamepad = Gamepad.Gamepads.FirstOrDefault();
                try { await Task.Delay(1000, token); } catch { break; }
                continue;
            }

            try
            {
                var reading = gamepad.GetCurrentReading();
                uint currentButtons = (uint)reading.Buttons;

                if (currentButtons != _lastButtons)
                {
                    CheckButton(currentButtons, (uint)GamepadButtons.DPadUp, ControllerButton.DPadUp);
                    CheckButton(currentButtons, (uint)GamepadButtons.DPadDown, ControllerButton.DPadDown);
                    CheckButton(currentButtons, (uint)GamepadButtons.DPadLeft, ControllerButton.DPadLeft);
                    CheckButton(currentButtons, (uint)GamepadButtons.DPadRight, ControllerButton.DPadRight);
                    CheckButton(currentButtons, (uint)GamepadButtons.A, ControllerButton.A);
                    CheckButton(currentButtons, (uint)GamepadButtons.B, ControllerButton.B);
                    CheckButton(currentButtons, (uint)GamepadButtons.LeftShoulder, ControllerButton.LeftShoulder);
                    CheckButton(currentButtons, (uint)GamepadButtons.RightShoulder, ControllerButton.RightShoulder);
                }

                _lastButtons = currentButtons;
            }
            catch
            {
                _activeGamepad = null;
            }

            try { await Task.Delay(32, token); } catch { break; }
        }
    }

    private void CheckButton(uint current, uint target, ControllerButton button)
    {
        if ((current & target) != 0 && (_lastButtons & target) == 0)
        {
            ButtonPressed?.Invoke(this, button);
        }
        else if ((current & target) == 0 && (_lastButtons & target) != 0)
        {
            ButtonReleased?.Invoke(this, button);
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
