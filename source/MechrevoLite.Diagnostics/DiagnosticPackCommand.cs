using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MechrevoLite.Diagnostics;

internal static class DiagnosticPackCommand
{
	internal const string DestinationOverrideVariable = "LMECHREVO_DIAGNOSTIC_PACK_PATH";

	internal static async Task<string?> RunAsync(Form owner, Control? trigger)
	{
		string text = ResolveDestination(owner);
		if (text == null)
		{
			return null;
		}
		if (trigger != null && !trigger.IsDisposed)
		{
			trigger.Enabled = false;
		}
		try
		{
			DiagnosticPackInputs inputs = DiagnosticPackCapture.Capture();
			DiagnosticPackResult diagnosticPackResult = await DiagnosticPackRunner.ExportAsync(text, inputs);
			ShowSuccess(owner, diagnosticPackResult);
			return diagnosticPackResult.ZipPath;
		}
		catch (Exception ex)
		{
			Logger.WriteLine("诊断包导出失败: " + ex);
			MessageBox.Show(owner, "导出诊断包失败：\n" + ex.Message, "L-Mechrevo", MessageBoxButtons.OK, MessageBoxIcon.Hand);
			return null;
		}
		finally
		{
			if (trigger != null && !trigger.IsDisposed)
			{
				trigger.Enabled = true;
			}
		}
	}

	private static string? ResolveDestination(Form owner)
	{
		string environmentVariable = Environment.GetEnvironmentVariable("LMECHREVO_DIAGNOSTIC_PACK_PATH");
		if (!string.IsNullOrWhiteSpace(environmentVariable))
		{
			return environmentVariable;
		}
		using SaveFileDialog saveFileDialog = new SaveFileDialog
		{
			Title = "导出诊断包",
			Filter = "Zip 压缩包 (*.zip)|*.zip",
			DefaultExt = "zip",
			AddExtension = true,
			OverwritePrompt = true,
			RestoreDirectory = true,
			FileName = DiagnosticPackExporter.BuildFileName(Program.ReleaseVersion, DateTime.Now),
			InitialDirectory = ResolveDefaultDirectory()
		};
		return (saveFileDialog.ShowDialog(owner) == DialogResult.OK) ? saveFileDialog.FileName : null;
	}

	private static string ResolveDefaultDirectory()
	{
		string folderPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
		if (Directory.Exists(folderPath))
		{
			return folderPath;
		}
		string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
		if (Directory.Exists(text))
		{
			return text;
		}
		string folderPath2 = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
		if (Directory.Exists(folderPath2))
		{
			return folderPath2;
		}
		return AppContext.BaseDirectory;
	}

	private static void ShowSuccess(Form owner, DiagnosticPackResult result)
	{
		if (MessageBox.Show(owner, $"诊断包已导出：\n{result.ZipPath}\n\n大小 {FormatSize(result.SizeBytes)}，共 {result.Entries.Count} 个条目。\n\n" + "是否打开所在文件夹？", "L-Mechrevo", MessageBoxButtons.YesNo, MessageBoxIcon.Asterisk) == DialogResult.Yes)
		{
			RevealInExplorer(result.ZipPath);
		}
	}

	internal static void RevealInExplorer(string path)
	{
		try
		{
			Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"")
			{
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			Logger.WriteLine("打开所在文件夹失败: " + ex.Message);
		}
	}

	private static string FormatSize(long bytes)
	{
		if (bytes < 1048576)
		{
			if (bytes < 1024)
			{
				return $"{bytes} B";
			}
			return $"{(double)bytes / 1024.0:F0} KB";
		}
		return $"{(double)bytes / 1024.0 / 1024.0:F1} MB";
	}
}
