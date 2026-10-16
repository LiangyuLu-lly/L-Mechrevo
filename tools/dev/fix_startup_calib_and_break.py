# -*- coding: utf-8 -*-
# 1) iss: {break} 经 {cm:} 展开不递归 -> 加 ExpandMsg 辅助函数统一替换为真换行
# 2) Program.cs: 开机自动应用校色，calib_mode==999 时经 IccHelper.ApplyIcc（不走 GCU）
import io

# ---------- iss ----------
ip = r'D:\openLM\installer-src\install_script.iss'
s = io.open(ip, encoding='utf-8-sig').read()

helper = """// {cm:} 展开不递归处理 {break}，这里统一替换为真实换行。
function ExpandMsg(Const Name: String): String;
var
  S: String;
begin
  S := ExpandConstant('{cm:' + Name + '}');
  StringChange(S, '{break}', #13#10);
  Result := S;
end;

"""
anchor = "[Code]"
idx = s.find(anchor)
assert idx >= 0, 'anchor [Code] not found'
# 在 [Code] 后插入 helper（紧跟 [Code] 行的下一行）
pos = s.find('\n', idx) + 1
s = s[:pos] + helper + s[pos:]

calls = [
    ("ExpandConstant('{cm:RuntimeRequired}')", "ExpandMsg('RuntimeRequired')"),
    ("ExpandConstant('{cm:RuntimeFailed}')", "ExpandMsg('RuntimeFailed')"),
    ("ExpandConstant('{cm:RuntimeDeclined}')", "ExpandMsg('RuntimeDeclined')"),
    ("ExpandConstant('{cm:OfficialConsolePrompt}')", "ExpandMsg('OfficialConsolePrompt')"),
]
for old, new in calls:
    c = s.count(old)
    s = s.replace(old, new)
    print('iss call %s matched: %d' % (old.split(':')[1][1:-1], c))

io.open(ip, 'w', encoding='utf-8-sig').write(s)
print('iss written')

# ---------- Program.cs ----------
pp = r'D:\openLM\source\MechrevoLite\Program.cs'
t = io.open(pp, encoding='utf-8-sig').read()

old_p = """				if (AppConfig.Is("calib_apply_on_startup") && AppConfig.Get("calib_mode", 0) > 0)
				{
					try
					{
						int calibMode = AppConfig.Get("calib_mode", 0);
						bool calibApplied = await service.SetColorCalibration(calibMode);
						Logger.WriteLine($"校色开机应用: mode={calibMode} applied={calibApplied}");
					}
					catch (Exception ex)
					{
						Logger.WriteLine("校色开机应用 fail: " + ex.Message);
					}
				}"""

new_p = """				if (AppConfig.Is("calib_apply_on_startup") && AppConfig.Get("calib_mode", 0) > 0)
				{
					try
					{
						int calibMode = AppConfig.Get("calib_mode", 0);
						if (calibMode == 999)
						{
							// 自定义 ICC：经 Windows 颜色管理应用，不走 GCU
							string? iccName = AppConfig.GetString("calib_custom_icc");
							string iccPath = string.IsNullOrEmpty(iccName) ? "" : Path.Combine(IccHelper.CustomIccDir, iccName);
							bool iccApplied = !string.IsNullOrEmpty(iccPath) && File.Exists(iccPath) && await Task.Run(() => IccHelper.ApplyIcc(iccPath));
							Logger.WriteLine($"校色开机应用(自定义 ICC): file={iccName ?? "(空)"} applied={iccApplied}");
						}
						else
						{
							bool calibApplied = await service.SetColorCalibration(calibMode);
							Logger.WriteLine($"校色开机应用: mode={calibMode} applied={calibApplied}");
						}
					}
					catch (Exception ex)
					{
						Logger.WriteLine("校色开机应用 fail: " + ex.Message);
					}
				}"""

c = t.count(old_p)
t = t.replace(old_p, new_p)
io.open(pp, 'w', encoding='utf-8-sig').write(t)
print('Program.cs patch matched:', c)
