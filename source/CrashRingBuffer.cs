using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

internal sealed class CrashRingBuffer
{
	private readonly object _lock = new object();

	private readonly Queue<string> _lines = new Queue<string>();

	private readonly int _maxBytes;

	private long _bytes;

	internal int ByteCount
	{
		get
		{
			lock (_lock)
			{
				return (int)_bytes;
			}
		}
	}

	internal int LineCount
	{
		get
		{
			lock (_lock)
			{
				return _lines.Count;
			}
		}
	}

	internal CrashRingBuffer(int maxBytes)
	{
		_maxBytes = Math.Max(1, maxBytes);
	}

	internal void Append(string line)
	{
		lock (_lock)
		{
			_lines.Enqueue(line);
			_bytes += SizeOf(line);
			while (_bytes > _maxBytes && _lines.Count > 1)
			{
				_bytes -= SizeOf(_lines.Dequeue());
			}
		}
	}

	internal string Snapshot()
	{
		lock (_lock)
		{
			return string.Join(Environment.NewLine, _lines);
		}
	}

	internal void FlushTo(string path)
	{
		FlushTo(path, 0);
	}

	/// <summary>
	/// 2026-10-12 logfix2（用户原话「改log」）：环形缓冲从 64 KB 提到 4 MiB 之后，
	/// 一次崩溃就往 crash.txt 灌 4 MiB 会让**不轮转的** crash.txt 被崩溃循环撑爆。
	/// 这里只追加**尾部** maxBytes 字节（按整行对齐；maxBytes ≤ 0 或缓冲未超限时写全部）。
	/// </summary>
	internal void FlushTo(string path, int maxBytes)
	{
		lock (_lock)
		{
			if (_lines.Count != 0)
			{
				string directoryName = Path.GetDirectoryName(path);
				if (!string.IsNullOrEmpty(directoryName))
				{
					Directory.CreateDirectory(directoryName);
				}
				string text;
				if (maxBytes <= 0 || _bytes <= maxBytes)
				{
					text = string.Join(Environment.NewLine, _lines);
				}
				else
				{
					string[] array = _lines.ToArray();
					int num = 0;
					int num2 = array.Length;
					while (num2 > 1)
					{
						int num3 = SizeOf(array[num2 - 1]);
						if (num + num3 > maxBytes)
						{
							break;
						}
						num += num3;
						num2--;
					}
					text = string.Join(Environment.NewLine, array, num2, array.Length - num2);
				}
				File.AppendAllText(path, text + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			}
		}
	}

	private static int SizeOf(string line)
	{
		return Encoding.UTF8.GetByteCount(line) + Environment.NewLine.Length;
	}
}
