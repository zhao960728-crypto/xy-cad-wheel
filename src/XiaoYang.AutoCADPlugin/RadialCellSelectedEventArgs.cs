using System;

namespace XiaoYang.AutoCADPlugin;

internal sealed class RadialCellSelectedEventArgs : EventArgs
{
	public int RingIndex { get; }

	public int Slot { get; }

	public RadialCellSelectedEventArgs(int ringIndex, int slot)
	{
		RingIndex = ringIndex;
		Slot = slot;
	}
}
