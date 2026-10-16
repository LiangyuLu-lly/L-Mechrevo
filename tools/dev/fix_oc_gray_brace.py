# -*- coding: utf-8 -*-
p = r'D:\openLM\source\MechrevoLite\CustomModeForm.cs'
t = open(p, encoding='utf-8').read()
old = '''				_syncing = false;
			}
		}
		SetOptionsEnabled();
	}
	catch (Exception ex)
	{
		Logger.WriteLine("Performance mode editor refresh failed: " + ex.Message);
	}
}'''
new = '''				_syncing = false;
			}
			SetOptionsEnabled();
		}
		catch (Exception ex)
		{
			Logger.WriteLine("Performance mode editor refresh failed: " + ex.Message);
		}
}'''
n = t.count(old)
print('match:', n)
if n == 1:
    open(p, 'w', encoding='utf-8', newline='').write(t.replace(old, new))
    print('OK')
else:
    print('ABORT')
