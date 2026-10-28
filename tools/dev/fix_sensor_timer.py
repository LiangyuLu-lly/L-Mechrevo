# -*- coding: utf-8 -*-
p = r'D:\openLM\source\MechrevoLite\SettingsForm.cs'
t = open(p, encoding='utf-8').read()

# 1) _sensorTimer 初始化后常驻启动（UiAuditMode 下不启动，与其它定时器一致）
old1 = '''		_sensorTimer = new System.Timers.Timer(AppConfig.Get("sensor_timer", 1000));
		_sensorTimer.Elapsed += OnTimedEvent;
		InitQuickSwitchRefresh();'''
new1 = '''		_sensorTimer = new System.Timers.Timer(AppConfig.Get("sensor_timer", 1000));
		_sensorTimer.Elapsed += OnTimedEvent;
		// 传感器轮询常驻：不随主窗显示/隐藏启停（对齐 beta21「最小化时除传感器轮询外暂停显示回读」），
		// 避免每次打开主窗都重新进入读取周期
		if (!Program.UiAuditMode)
		{
			_sensorTimer.Start();
		}
		InitQuickSwitchRefresh();'''

# 2) VisibleChanged 不再绑定 _sensorTimer，只绑 3 秒显示回读
old2 = '''	private void SettingsForm_VisibleChanged(object? sender, EventArgs e)
	{
		if (!Program.UiAuditMode)
		{
			_sensorTimer.Enabled = Visible;
			if (!IsDisposed)
			{
				_displayStatusTimer.Enabled = Visible;
			}'''
new2 = '''	private void SettingsForm_VisibleChanged(object? sender, EventArgs e)
	{
		if (!Program.UiAuditMode)
		{
			// _sensorTimer 常驻，不随可见性启停（见构造函数注释）
			if (!IsDisposed)
			{
				_displayStatusTimer.Enabled = Visible;
			}'''

print('old1 命中:', t.count(old1))
print('old2 命中:', t.count(old2))
if t.count(old1) != 1 or t.count(old2) != 1:
    print('匹配数异常，中止')
    raise SystemExit(1)
t = t.replace(old1, new1).replace(old2, new2)
open(p, 'w', encoding='utf-8', newline='').write(t)
t2 = open(p, encoding='utf-8').read()
print('_sensorTimer.Start 出现:', t2.count('_sensorTimer.Start();'))
print('_sensorTimer.Enabled = Visible 残留:', t2.count('_sensorTimer.Enabled = Visible;'))
print('OK')
