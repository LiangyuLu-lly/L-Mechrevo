using System.Runtime.InteropServices;

namespace MechrevoLite.Helpers;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct BLENDFUNCTION
{
	public byte BlendOp;

	public byte BlendFlags;

	public byte SourceConstantAlpha;

	public byte AlphaFormat;
}
