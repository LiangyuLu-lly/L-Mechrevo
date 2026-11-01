using System;
using System.Collections.Generic;
using System.Diagnostics;

public static class NvidiaSmi
{
	public static int GetDefaultMaxGPUPower(int? reportedMaximum = null)
	{
		return reportedMaximum ?? (-1);
	}

	internal static IReadOnlyList<(int Pid, string Name)>? QueryComputeApplications()
	{
		string text = RunNvidiaSmiCommand("--query-compute-apps=pid,process_name --format=csv,noheader");
		if (string.IsNullOrWhiteSpace(text))
		{
			return Array.Empty<(int, string)>();
		}
		List<(int, string)> list = new List<(int, string)>();
		string[] array = text.Split('\n');
		for (int i = 0; i < array.Length; i++)
		{
			string text2 = array[i].Trim();
			if (text2.Length == 0)
			{
				continue;
			}
			int num = text2.IndexOf(", ", StringComparison.Ordinal);
			if (num > 0 && int.TryParse(text2.AsSpan(0, num).Trim(), out var result))
			{
				string text3 = text2.Substring(num + 2).Trim();
				if (text3.Length > 0)
				{
					list.Add((result, text3));
				}
			}
		}
		return list;
	}

	private static string RunNvidiaSmiCommand(string arguments = "-i 0 -q")
	{
		ProcessStartInfo startInfo = new ProcessStartInfo
		{
			FileName = "nvidia-smi",
			Arguments = arguments,
			RedirectStandardOutput = true,
			UseShellExecute = false,
			CreateNoWindow = true
		};
		try
		{
			using Process process = new Process
			{
				StartInfo = startInfo
			};
			process.Start();
			string result = process.StandardOutput.ReadToEnd();
			process.WaitForExit();
			return result;
		}
		catch (Exception)
		{
		}
		return "";
	}
}
