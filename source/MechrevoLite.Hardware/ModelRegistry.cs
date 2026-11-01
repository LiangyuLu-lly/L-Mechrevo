using System;
using System.Collections.Generic;
using System.Linq;
using Probe;

namespace MechrevoLite.Hardware;

public static class ModelRegistry
{
	private sealed class DelegateReadTransport(Func<int, int> read) : IEcReadTransport
	{
		public int ReadByte(int address)
		{
			return read(address);
		}
	}

	public const int ProjectByteAddress = 1856;

	public const int BiosProjectByteAddress = 1868;

	private const int SystemIdAddress = 1110;

	private const int GpuModuleIdAddress = 2002;

	private const int ModuleIdAddress = 2003;

	private const int RomIdAddress = 1905;

	private const int RomId2Address = 1906;

	private const int AdapterWattAddress = 1183;

	private const int SixBitIdAddress = 1994;

	private static readonly IReadOnlyDictionary<int, string> ProjectIdNames = new Dictionary<int, string>
	{
		[0] = "None",
		[1] = "GI",
		[2] = "GJ",
		[3] = "GK",
		[4] = "GICN",
		[5] = "GJCN",
		[6] = "GK5CN_X",
		[7] = "GK7CN_S",
		[8] = "GK7CPCS_GK5CQ7Z",
		[9] = "PF",
		[10] = "GK5CP_4X_5X_6X",
		[11] = "IDP",
		[12] = "IDY_6Y",
		[13] = "IDY_7Y",
		[14] = "PF4MU_PF4MN_PF5MU",
		[15] = "CML_Gaming",
		[16] = "GK7NXXR",
		[17] = "GM5MU1Y",
		[18] = "PH4TRX1",
		[19] = "PH4TUX1",
		[20] = "PH4TQx1",
		[21] = "PH6TRX1",
		[22] = "PH6TQxx",
		[23] = "PHxAxxx",
		[24] = "PHxPxxx",
		[5889] = "PH4ARxx",
		[5890] = "PH4AUxx",
		[5891] = "PH4AXxx",
		[5892] = "PH6AQxx",
		[5893] = "PH6ARxx",
		[5894] = "PH6AGxx",
		[5895] = "PH4AUxf",
		[6145] = "PH4PRxx",
		[6146] = "PH4PUxx",
		[6147] = "PH4PGx1",
		[6148] = "PH4PGx2",
		[6149] = "PH6PRxx",
		[6150] = "PH6PGEx",
		[6151] = "PH6PG0x",
		[6152] = "PH6PG3x",
		[6153] = "PH6PG7x",
		[6154] = "PH4AQE3",
		[6155] = "PH6PG0x150W",
		[6156] = "PH6PG3x150W",
		[6157] = "PH6PG7x150W"
	};

	private static readonly IReadOnlyDictionary<int, string> BiosProjectByteNames = new Dictionary<int, string>
	{
		[0] = "IDR",
		[1] = "IDX",
		[2] = "IDV",
		[3] = "IDO",
		[4] = "IDP",
		[5] = "IDS",
		[6] = "IDY",
		[7] = "IDW",
		[8] = "IDM",
		[9] = "IDE",
		[10] = "IDN",
		[11] = "IDZ",
		[12] = "IDXQ",
		[13] = "IDQ",
		[14] = "IDO3",
		[15] = "ID1",
		[16] = "ID2"
	};

	public static ModelIdentity Read(IEcReadTransport transport)
	{
		ArgumentNullException.ThrowIfNull(transport, "transport");
		try
		{
			int num = transport.ReadByte(1856);
			if (num < 0)
			{
				return ModelIdentity.Unknown;
			}
			int num2 = num & 0xFF;
			string biosProjectId = ReadBiosProjectIdName(transport);
			int key = ExpandProjectId(num2, transport);
			if (!ProjectIdNames.TryGetValue(key, out string value))
			{
				return new ModelIdentity("Unknown", num2, biosProjectId, ModelSource.Unknown);
			}
			return new ModelIdentity(value, num2, biosProjectId, ModelSource.Ec);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("ModelRegistry read failed: " + ex.Message);
			return ModelIdentity.Unknown;
		}
	}

	internal static bool IsKnownProjectName(string name)
	{
		return ProjectIdNames.Values.Contains(name, StringComparer.Ordinal);
	}

	internal static string? ProjectIdNameFor(int expandedProjectId)
	{
		if (!ProjectIdNames.TryGetValue(expandedProjectId, out string value))
		{
			return null;
		}
		return value;
	}

	internal static int ExpandProjectIdForTest(int projectId, Func<int, int> readByte)
	{
		return ExpandProjectId(projectId, new DelegateReadTransport(readByte));
	}

	private static string ReadBiosProjectIdName(IEcReadTransport transport)
	{
		int num = transport.ReadByte(1868);
		if (num < 0)
		{
			return "Unknown";
		}
		int num2 = (((ReadByte(transport, 1994) & 1) != 0) ? 63 : 15);
		if (!BiosProjectByteNames.TryGetValue(num & num2, out string value))
		{
			return "NA";
		}
		return value;
	}

	private static int ExpandProjectId(int projectId, IEcReadTransport transport)
	{
		if (projectId != 23 && projectId != 24)
		{
			return projectId;
		}
		int num = ReadByte(transport, 1110);
		int num2 = ReadByte(transport, 2002);
		int num3 = ReadByte(transport, 2003);
		int num4 = ReadByte(transport, 1905);
		int num5 = ReadByte(transport, 1906);
		int raw = ReadByte(transport, 1183);
		int num6 = num & 0x80;
		int num7 = num2 & 0x1F;
		int num8 = num3 & 1;
		int num9 = num3 & 0xF0;
		int num10 = num4 & 1;
		int num11 = num5;
		int num12 = AdapterWattFor(raw);
		if (projectId == 23)
		{
			if (num6 == 0 && num10 == 0)
			{
				switch (num8)
				{
				case 0:
					return 5889;
				case 1:
					if (num11 != 3)
					{
						return 5890;
					}
					return 5895;
				}
			}
			else
			{
				if (num6 == 128 && num10 == 0)
				{
					return 5891;
				}
				if (num6 == 0 && num10 == 1)
				{
					if (num8 == 0)
					{
						return 5893;
					}
				}
				else if (num6 == 128 && num10 == 1)
				{
					if (num9 == 48 || num9 == 112 || num9 == 160)
					{
						return 5894;
					}
					return 5892;
				}
			}
		}
		else if (num6 == 0 && num10 == 0)
		{
			switch (num8)
			{
			case 0:
				return 6145;
			case 1:
				return 6146;
			}
		}
		else
		{
			if (num6 == 128 && num10 == 0)
			{
				return num11 switch
				{
					1 => 6147, 
					4 => 6154, 
					_ => 6148, 
				};
			}
			if (num6 == 0 && num10 == 1)
			{
				return 6149;
			}
			if (num6 == 128 && num10 == 1)
			{
				switch (num7)
				{
				case 8:
					return 6150;
				case 16:
				case 17:
				case 21:
					if (num12 == 150)
					{
						return 6155;
					}
					return 6151;
				case 12:
					if (num12 == 150)
					{
						return 6156;
					}
					return 6152;
				case 18:
					if (num12 == 150)
					{
						return 6157;
					}
					return 6153;
				}
			}
		}
		return projectId;
	}

	private static int AdapterWattFor(int raw)
	{
		return (raw & 0x78) switch
		{
			0 => 330, 
			8 => 230, 
			16 => 180, 
			24 => 150, 
			32 => 120, 
			40 => 90, 
			48 => 65, 
			56 => 40, 
			64 => 280, 
			_ => 150, 
		};
	}

	private static int ReadByte(IEcReadTransport transport, int address)
	{
		int num = transport.ReadByte(address);
		if (num >= 0)
		{
			return num & 0xFF;
		}
		return 0;
	}
}
