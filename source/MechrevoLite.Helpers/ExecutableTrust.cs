using System;
using System.Collections.Generic;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Windows.Forms;

namespace MechrevoLite.Helpers;

internal static class ExecutableTrust
{
	internal readonly record struct AccessRuleSnapshot(string IdentitySid, FileSystemRights Rights, AccessControlType Type, bool InheritOnly);

	private static readonly string[] TrustedSids = new string[5] { "S-1-5-32-544", "S-1-5-18", "S-1-5-19", "S-1-5-20", "S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464" };

	private const FileSystemRights ImageReplacingRights = FileSystemRights.Write | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.Delete | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;

	internal static bool GrantsImageReplacementToNonAdministrators(IEnumerable<AccessRuleSnapshot> rules)
	{
		return GrantsImageReplacementToNonAdministrators(rules, out string? _);
	}

	/// <summary>
	/// 2026-10-09（exportperm）：除标准 TrustedSids 外，还接受"当前进程自己那个账户"。
	///
	/// 背景（实测）：安装脚本 <c>installer-src\files\GCU\Install-Gcu.ps1</c> 的 Grant-AppDirectoryAcl
	/// 会对 <c>{app}</c>（= 安装目录，默认 C:\Program Files\L-Mechrevo）写下一条
	/// "安装者账户: Modify" 的 ACE（它的日志原文："ACL granted (Modify): &lt;用户&gt; -&gt; C:\Program Files\L-Mechrevo"），
	/// 目的是让非提权实例能就地写配置/日志。旧判定把这**任何**非 TrustedSids 的账户都算成"非管理员"，
	/// 于是默认安装位置也被判成"允许非管理员创建或删除文件"，SYSTEM 充电上限开机任务被拒注册，
	/// 界面上弹出「充电上限 · 安装位置」提示（用户报的"默认安装位置也提示充电权限"）。
	///
	/// 该账户本来就是管理员：本程序 manifest 是 requireAdministrator，只有管理员身份才能把这条
	/// ACE 写进 Program Files；而管理员对 Program Files 下的任何文件本来就有改写权（继承自
	/// C:\Program Files 的 BA:Modify），所以接受它不降低安全性。真正的普通用户
	/// （BUILTIN\Users / Authenticated Users / 其他账户）依旧会被拒绝并如实提示。
	/// </summary>
	internal static bool GrantsImageReplacementToNonAdministrators(IEnumerable<AccessRuleSnapshot> rules, out string? offendingIdentity)
	{
		offendingIdentity = null;
		foreach (AccessRuleSnapshot rule in rules)
		{
			if (rule.Type != AccessControlType.Allow || rule.InheritOnly || (rule.Rights & ImageReplacingRights) == 0)
			{
				continue;
			}
			if (IsTrustedIdentity(rule.IdentitySid) || IsCurrentOperatorIdentity(rule.IdentitySid))
			{
				continue;
			}
			offendingIdentity = rule.IdentitySid;
			return true;
		}
		return false;
	}

	/// <summary>目录/文件上是否存在"只发给当前账户"的改写权（用于把通过校验的原因说准，不再一律说"仅管理员可写"）。</summary>
	internal static bool HasCurrentOperatorWriteRule(IEnumerable<AccessRuleSnapshot> rules)
	{
		foreach (AccessRuleSnapshot rule in rules)
		{
			if (rule.Type == AccessControlType.Allow && !rule.InheritOnly && (rule.Rights & ImageReplacingRights) != 0 && IsCurrentOperatorIdentity(rule.IdentitySid))
			{
				return true;
			}
		}
		return false;
	}

	internal static bool IsTrustedIdentity(string? sid)
	{
		if (!string.IsNullOrWhiteSpace(sid))
		{
			return Array.Exists(TrustedSids, (string trusted) => string.Equals(trusted, sid, StringComparison.OrdinalIgnoreCase));
		}
		return false;
	}

	/// <summary>
	/// 这条 ACE 是否发给"当前进程正在跑的账户"，且当前进程确实带着管理员权限。
	/// 非管理员上下文里恒为 false —— 那种情况下仍然按"非管理员可写"拒绝，判定不放宽。
	/// </summary>
	internal static bool IsCurrentOperatorIdentity(string? sid)
	{
		if (string.IsNullOrWhiteSpace(sid) || !ProcessHelper.IsUserAdministrator())
		{
			return false;
		}
		try
		{
			using WindowsIdentity identity = WindowsIdentity.GetCurrent();
			SecurityIdentifier? user = identity.User;
			return user != null && string.Equals(user.Value, sid, StringComparison.OrdinalIgnoreCase);
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Can't read the current user SID: " + ex.Message);
			return false;
		}
	}

	/// <summary>SID → 可读账户名（翻译失败就原样返回 SID），只用于提示文案。</summary>
	internal static string DescribeIdentity(string? sid)
	{
		if (string.IsNullOrWhiteSpace(sid))
		{
			return "未知账户";
		}
		try
		{
			return new SecurityIdentifier(sid).Translate(typeof(NTAccount))?.Value ?? sid;
		}
		catch (Exception)
		{
			return sid;
		}
	}

	internal static bool IsCurrentImageInProtectedLocation(out string reason)
	{
		string text;
		try
		{
			text = Application.ExecutablePath.Trim();
			if (string.IsNullOrWhiteSpace(text))
			{
				reason = "无法确定当前程序镜像路径";
				return false;
			}
		}
		catch (Exception ex)
		{
			reason = "无法确定当前程序镜像路径：" + ex.Message;
			return false;
		}
		return IsProtectedLocation(text, out reason);
	}

	internal static bool IsProtectedLocation(string imagePath, out string reason)
	{
		string directoryName;
		try
		{
			directoryName = Path.GetDirectoryName(Path.GetFullPath(imagePath));
		}
		catch (Exception ex)
		{
			reason = "无法解析镜像路径 " + imagePath + "：" + ex.Message;
			return false;
		}
		if (string.IsNullOrWhiteSpace(directoryName))
		{
			reason = "无法解析镜像所在目录：" + imagePath;
			return false;
		}
		if (!TryReadDirectoryRules(directoryName, out List<AccessRuleSnapshot> rules, out string error))
		{
			reason = "无法读取目录权限 " + directoryName + "：" + error;
			return false;
		}
		bool operatorAccountGranted = HasCurrentOperatorWriteRule(rules);
		if (GrantsImageReplacementToNonAdministrators(rules, out string? offender))
		{
			reason = "目录 " + directoryName + " 允许普通用户账户（" + DescribeIdentity(offender) + "）创建或删除文件";
			return false;
		}
		if (File.Exists(imagePath))
		{
			if (!TryReadFileRules(imagePath, out List<AccessRuleSnapshot> rules2, out error))
			{
				reason = "无法读取文件权限 " + imagePath + "：" + error;
				return false;
			}
			operatorAccountGranted |= HasCurrentOperatorWriteRule(rules2);
			if (GrantsImageReplacementToNonAdministrators(rules2, out string? offender2))
			{
				reason = "文件 " + imagePath + " 允许普通用户账户（" + DescribeIdentity(offender2) + "）改写";
				return false;
			}
		}
		reason = (operatorAccountGranted
			? "目录 " + directoryName + " 仅管理员与当前账户（安装脚本授权）可写：" + DescribeIdentity(CurrentUserSidOrNull())
			: "目录 " + directoryName + " 仅管理员可写");
		return true;
	}

	private static string? CurrentUserSidOrNull()
	{
		try
		{
			using WindowsIdentity identity = WindowsIdentity.GetCurrent();
			return identity.User?.Value;
		}
		catch (Exception)
		{
			return null;
		}
	}

	/// <summary>
	/// 把当前程序所在目录收紧为"仅管理员可写"：SYSTEM 与 Administrators 完全控制，
	/// Users 仅读取与执行（与 Program Files 的有效权限一致）。用于安装目录不满足保护的机器上
	/// 一键修好"充电上限以 SYSTEM 注册开机任务"的前置条件。
	/// 返回 true 时已用 IsProtectedLocation 复核通过；目录内文件靠继承自动生效。
	/// 需要管理员权限（调用方负责确认）。
	/// </summary>
	internal static bool TryHardenImageDirectory(out string message)
	{
		string imagePath;
		try
		{
			imagePath = Application.ExecutablePath.Trim();
		}
		catch (Exception ex)
		{
			message = "无法确定当前程序镜像路径：" + ex.Message;
			return false;
		}
		string directory;
		try
		{
			directory = Path.GetDirectoryName(Path.GetFullPath(imagePath));
		}
		catch (Exception ex2)
		{
			message = "无法解析镜像所在目录：" + ex2.Message;
			return false;
		}
		if (string.IsNullOrWhiteSpace(directory))
		{
			message = "无法解析镜像所在目录：" + imagePath;
			return false;
		}
		try
		{
			DirectoryInfo directoryInfo = new DirectoryInfo(directory);
			if (!directoryInfo.Exists)
			{
				message = "目录不存在：" + directory;
				return false;
			}
			DirectorySecurity accessControl = directoryInfo.GetAccessControl(AccessControlSections.Access);
			// 1) 断开继承且不保留继承项 —— 必须去掉从盘符根继承来的"Authenticated Users: 修改"
			accessControl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
			// 2) 清空全部显式项，避免残留"非管理员可写"的旧 ACE
			foreach (FileSystemAccessRule rule in accessControl.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier)))
			{
				accessControl.RemoveAccessRuleSpecific(rule);
			}
			// 3) 只装标准三项（与 Program Files 的有效权限一致）
			InheritanceFlags inheritanceFlags = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
			accessControl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier("S-1-5-18"), FileSystemRights.FullControl, inheritanceFlags, PropagationFlags.None, AccessControlType.Allow));
			accessControl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier("S-1-5-32-544"), FileSystemRights.FullControl, inheritanceFlags, PropagationFlags.None, AccessControlType.Allow));
			accessControl.AddAccessRule(new FileSystemAccessRule(new SecurityIdentifier("S-1-5-32-545"), FileSystemRights.ReadAndExecute, inheritanceFlags, PropagationFlags.None, AccessControlType.Allow));
			directoryInfo.SetAccessControl(accessControl);
		}
		catch (Exception ex3)
		{
			message = "写入目录权限失败：" + ex3.Message;
			return false;
		}
		// 4) 复核
		if (!IsProtectedLocation(imagePath, out string reason))
		{
			message = "收紧后仍未通过校验：" + reason;
			return false;
		}
		message = reason;
		return true;
	}

	private static bool TryReadDirectoryRules(string directory, out List<AccessRuleSnapshot> rules, out string error)
	{
		try
		{
			DirectorySecurity accessControl = new DirectoryInfo(directory).GetAccessControl(AccessControlSections.Access);
			rules = Snapshot(accessControl.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)));
			error = "";
			return true;
		}
		catch (Exception ex)
		{
			rules = new List<AccessRuleSnapshot>();
			error = ex.Message;
			return false;
		}
	}

	private static bool TryReadFileRules(string path, out List<AccessRuleSnapshot> rules, out string error)
	{
		try
		{
			FileSecurity accessControl = new FileInfo(path).GetAccessControl(AccessControlSections.Access);
			rules = Snapshot(accessControl.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier)));
			error = "";
			return true;
		}
		catch (Exception ex)
		{
			rules = new List<AccessRuleSnapshot>();
			error = ex.Message;
			return false;
		}
	}

	private static List<AccessRuleSnapshot> Snapshot(AuthorizationRuleCollection collection)
	{
		List<AccessRuleSnapshot> list = new List<AccessRuleSnapshot>(collection.Count);
		foreach (AuthorizationRule item in collection)
		{
			if (item is FileSystemAccessRule fileSystemAccessRule)
			{
				list.Add(new AccessRuleSnapshot(fileSystemAccessRule.IdentityReference.Value, fileSystemAccessRule.FileSystemRights, fileSystemAccessRule.AccessControlType, (fileSystemAccessRule.PropagationFlags & PropagationFlags.InheritOnly) != 0));
			}
		}
		return list;
	}
}
