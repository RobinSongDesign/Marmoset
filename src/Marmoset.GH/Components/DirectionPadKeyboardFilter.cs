using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Marmoset.Components
{
    internal sealed class DirectionPadKeyboardFilter : IDisposable
    {
        private const int WhKeyboardLl = 13;
        private const int WmKeyDown = 0x0100;
        private const int WmKeyUp = 0x0101;
        private const int WmSysKeyDown = 0x0104;
        private const int WmSysKeyUp = 0x0105;

        private readonly DirectionPadComponent _owner;
        private readonly LowLevelKeyboardProc _proc;
        private IntPtr _hookId;

        public DirectionPadKeyboardFilter(DirectionPadComponent owner)
        {
            _owner = owner;
            _proc = HookCallback;
            _hookId = SetWindowsHookEx(WhKeyboardLl, _proc, IntPtr.Zero, 0);
        }

        public void Dispose()
        {
            if (_hookId == IntPtr.Zero)
                return;

            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode < 0 || !IsSelected())
                return CallNextHookEx(_hookId, nCode, wParam, lParam);

            int message = wParam.ToInt32();
            if (message != WmKeyDown && message != WmKeyUp && message != WmSysKeyDown && message != WmSysKeyUp)
                return CallNextHookEx(_hookId, nCode, wParam, lParam);

            int virtualKey = Marshal.ReadInt32(lParam);
            var key = (Keys)virtualKey;

            if (!TryMapKey(key, out int direction))
                return CallNextHookEx(_hookId, nCode, wParam, lParam);

            if (message == WmKeyDown || message == WmSysKeyDown)
            {
                _owner.SetPressedDirection(direction);
                _owner.SetDirection(direction);
            }
            else
            {
                _owner.SetPressedDirection(-1);
                _owner.SetDirection(-1);
            }

            return new IntPtr(1);
        }

        private bool IsSelected()
        {
            return _owner.Attributes != null && _owner.Attributes.Selected;
        }

        private static bool TryMapKey(Keys key, out int direction)
        {
            switch (key)
            {
                case Keys.Up:
                case Keys.W:
                    direction = 0;
                    return true;

                case Keys.Down:
                case Keys.S:
                    direction = 1;
                    return true;

                case Keys.Left:
                case Keys.A:
                    direction = 2;
                    return true;

                case Keys.Right:
                case Keys.D:
                    direction = 3;
                    return true;

                case Keys.Space:
                    direction = 4;
                    return true;

                default:
                    direction = -1;
                    return false;
            }
        }

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    }
}
