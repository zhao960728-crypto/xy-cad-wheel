using System.Runtime.InteropServices;
using System.Windows.Forms;
using XiaoYang.AutoCADPlugin;

internal static class SideButtonRegression
{
    internal static void Run()
    {
        var expectedOffset = IntPtr.Size == 8 ? 32 : 20;
        Check(MouseWheelTrigger.ExtendedMouseDataOffset == expectedOffset, "MOUSEHOOKSTRUCTEX ABI offset");
        var memory = Marshal.AllocHGlobal(expectedOffset + 8);
        try
        {
            Marshal.WriteInt32(memory, expectedOffset, 0x0002beef);
            Check(MouseWheelTrigger.ReadExtendedMouseData(memory) == 0x0002beef, "native mouseData read");
        }
        finally { Marshal.FreeHGlobal(memory); }
        foreach (var side in new[] { 1u, 2u })
        {
            var mode = side == 1 ? TriggerModes.XButton1Hold : TriggerModes.XButton2Hold;
            var expected = side == 1 ? MouseButtons.XButton1 : MouseButtons.XButton2;
            Check(TriggerModes.Normalize(mode) == mode, "side mode persisted");
            Check(MouseWheelTrigger.TryGetTriggerButton(0x20b, side << 16 | 0xbeef, mode, out var button, out var modifier), "selected side down rejected");
            Check(modifier == 0 && MouseWheelTrigger.ToMouseButtons(button) == expected, "side button mapping");
            Check(MouseWheelTrigger.IsTriggerUp(0x20c, side << 16 | 0xabcd, button), "selected side up rejected");
            Check(!MouseWheelTrigger.IsTriggerUp(0x20c, (3 - side) << 16, button), "other side up completed gesture");
            Check(!MouseWheelTrigger.IsTriggerUp(0x205, 0, button), "right up completed side gesture");
            Check(!MouseWheelTrigger.TryGetTriggerButton(0x20b, (3 - side) << 16, mode, out _, out _), "other side down intercepted");
            Check(!MouseWheelTrigger.TryGetTriggerButton(0x20b, 3u << 16, mode, out _, out _), "combined ambiguous side event intercepted");
            Check(!MouseWheelTrigger.TryGetTriggerButton(0x204, 0, mode, out _, out _), "right down intercepted");
            Check(MouseWheelTrigger.ShouldBypassForActiveCommand(mode, true), "busy CAD side event intercepted");
            MouseWheelTrigger.GetReplayParameters(button, out var down, out var up, out var data);
            Check(down == 0x80 && up == 0x100 && data == side, "side replay flags or unshifted mouseData");
        }
        MouseWheelTrigger.GetReplayParameters(MouseWheelTrigger.TriggerButton.Right, out var rightDown, out var rightUp, out var rightData);
        Check(rightDown == 8 && rightUp == 16 && rightData == 0, "right replay regressed");
        MouseWheelTrigger.GetReplayParameters(MouseWheelTrigger.TriggerButton.Middle, out var middleDown, out var middleUp, out var middleData);
        Check(middleDown == 32 && middleUp == 64 && middleData == 0, "middle replay regressed");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
