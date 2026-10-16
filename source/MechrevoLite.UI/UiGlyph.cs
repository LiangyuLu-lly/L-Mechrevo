using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace MechrevoLite.UI;

internal static class UiGlyph
{
	internal enum Kind
	{
		Gauge,
		VideoCard,
		Battery,
		Display,
		Toggles,
		Droplet,
		Refresh,
		Contrast,
		Console,
		Info,
		Overlay,
		Heart,
		Close,
		Gear,
		Chevrons,
		Package,
		// 2026-10-11（快捷磁贴行）：磁贴专用图标（图标在上、文字在下）。
		// 都是"只看形状就能认"的线性画法，与上面既有的 Kind 同一套笔宽/圆角/内边距口径。
		Fan,
		Camera,
		Microphone,
		MicrophoneMute,
		Keyboard,
		Moon,
		Plus,
		// 2026-10-12（用户原话「还有快捷开关的WiFi和蓝牙能有自己的图标吗」）：WiFi / 蓝牙各有**自己的**字形。
		// 与上面同一套口径：单色（颜色由调用方给，跟随深浅主题与开/关态）、矢量可缩放、
		// pen 线宽 = max(1, size/12)、圆头端点 —— ⛔ 不引入位图/图标字体/第二个笔宽。
		Wifi,
		Bluetooth,
		// 2026-10-12（用户要求「更新的图标也重新做一下」）：winupdate 磁贴改用专属更新图标
		// （圆环+向下箭头，Windows 更新语义），不再复用刷新（Refresh）。
		Update,
		// 2026-10-12（用户原话「触摸板能不能有专属图标」）：触摸板磁贴改用**专属**字形
		// （圆角矩形＝触控板面 + 下部一条横分隔 + 该带内竖分成两格＝左右键）。
		// 与既有磁贴字形同一套口径：pen 线宽 = max(1, size/12)、圆头圆角、0..1 归一化、单色跟随主题。
		// ⚠️ `touchpadtoggle`（触摸板开关）本轮**未改**，仍走 default = Toggles。
		Touchpad
	}

	internal static Bitmap Render(Kind kind, int size, Color color, int gap = 0)
	{
		int num = Math.Max(8, size);
		Bitmap bitmap = new Bitmap(num + Math.Max(0, gap), num);
		using Graphics graphics = Graphics.FromImage(bitmap);
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		float num2 = Math.Max(1f, (float)num / 12f);
		using Pen pen = new Pen(color, num2)
		{
			StartCap = LineCap.Round,
			EndCap = LineCap.Round,
			LineJoin = LineJoin.Round
		};
		using SolidBrush brush = new SolidBrush(color);
		float num3 = num2 * 1.6f;
		float num4 = (float)num - num3 * 2f;
		float num5 = num3;
		float num6 = num3;
		float num7 = num5 + num4 / 2f;
		float num8 = num6 + num4 / 2f;
		switch (kind)
		{
		case Kind.Gauge:
			graphics.DrawArc(pen, num5, num6, num4, num4, 200f, 140f);
			graphics.DrawLine(pen, num7, num8 + num4 * 0.18f, num5 + num4 * 0.74f, num6 + num4 * 0.3f);
			graphics.FillEllipse(brush, num7 - num2, num8 + num4 * 0.18f - num2, num2 * 2f, num2 * 2f);
			break;
		case Kind.VideoCard:
		{
			float num26 = num4 * 0.72f;
			float num27 = num8 - num26 / 2f;
			using (GraphicsPath path7 = RoundedRect(new RectangleF(num5, num27, num4, num26), num2 * 1.4f))
			{
				graphics.DrawPath(pen, path7);
				float num28 = num26 * 0.26f;
				graphics.DrawEllipse(pen, num7 - num28, num27 + num26 * 0.5f - num28, num28 * 2f, num28 * 2f);
				graphics.DrawLine(pen, num5 + num4 * 0.22f, num27 + num26 + num2 * 0.6f, num5 + num4 * 0.78f, num27 + num26 + num2 * 0.6f);
			}
			break;
		}
		case Kind.Battery:
		{
			float num25 = num4 * 0.56f;
			float y = num8 - num25 / 2f;
			using (GraphicsPath path6 = RoundedRect(new RectangleF(num5, y, num4 * 0.82f, num25), num2 * 1.2f))
			{
				graphics.DrawPath(pen, path6);
				graphics.DrawLine(pen, num5 + num4 * 0.82f + num2 * 0.5f, num8 - num25 * 0.16f, num5 + num4 * 0.82f + num2 * 0.5f, num8 + num25 * 0.16f);
				graphics.DrawLine(pen, num5 + num4 * 0.14f, num8, num5 + num4 * 0.5f, num8);
			}
			break;
		}
		case Kind.Display:
		{
			float num23 = num4 * 0.66f;
			float num24 = num8 - num23 * 0.72f;
			using (GraphicsPath path5 = RoundedRect(new RectangleF(num5, num24, num4, num23), num2 * 1.2f))
			{
				graphics.DrawPath(pen, path5);
				graphics.DrawLine(pen, num7, num24 + num23, num7, num24 + num23 + num4 * 0.16f);
				graphics.DrawLine(pen, num7 - num4 * 0.22f, num24 + num23 + num4 * 0.16f, num7 + num4 * 0.22f, num24 + num23 + num4 * 0.16f);
			}
			break;
		}
		case Kind.Toggles:
		{
			float num19 = num4 * 0.66f;
			float num20 = num4 * 0.3f;
			float num21 = num5 + num4;
			using (GraphicsPath path3 = RoundedRect(new RectangleF(num5, num6 + num4 * 0.04f, num19, num20), num20 / 2f))
			{
				graphics.DrawPath(pen, path3);
				float num22 = num20 * 0.66f;
				graphics.FillEllipse(brush, num5 + num19 - num22 - num20 * 0.17f, num6 + num4 * 0.04f + (num20 - num22) / 2f, num22, num22);
				using GraphicsPath path4 = RoundedRect(new RectangleF(num21 - num19, num6 + num4 * 0.56f, num19, num20), num20 / 2f);
				graphics.DrawPath(pen, path4);
				graphics.FillEllipse(brush, num21 - num19 + num20 * 0.17f, num6 + num4 * 0.56f + (num20 - num22) / 2f, num22, num22);
			}
			break;
		}
		case Kind.Droplet:
		{
			using (GraphicsPath graphicsPath5 = new GraphicsPath())
			{
				graphicsPath5.AddBezier(num7, num6 + num4 * 0.08f, num7 + num4 * 0.46f, num6 + num4 * 0.46f, num5 + num4 * 0.88f, num6 + num4 * 0.62f, num5 + num4 * 0.62f, num6 + num4 * 0.86f);
				graphicsPath5.AddBezier(num5 + num4 * 0.62f, num6 + num4 * 0.86f, num5 + num4 * 0.34f, num6 + num4 * 1.06f, num5 + num4 * 0.12f, num6 + num4 * 0.66f, num5 + num4 * 0.38f, num6 + num4 * 0.44f);
				graphicsPath5.AddBezier(num5 + num4 * 0.38f, num6 + num4 * 0.44f, num7 - num4 * 0.08f, num6 + num4 * 0.28f, num7 - num4 * 0.06f, num6 + num4 * 0.16f, num7, num6 + num4 * 0.08f);
				graphicsPath5.CloseFigure();
				graphics.DrawPath(pen, graphicsPath5);
			}
			break;
		}
		case Kind.Refresh:
			// 2026-10-12 r6（用户原话「一个是更新的箭头应该顺着圈的方向」）：缺口仍在右下
			//（弧从 75° 起、顺时针扫 300° ⇒ 终点 15°，3 点钟方向顺时针计），但箭头**不再是"斜着岔出去"的 V**：
			// 箭头尖＝圆弧末端的那个点，箭头轴线＝**该点的切线**（GDI+ 弧角顺时针 ⇒ 行进方向在屏幕坐标里是
			// (sin a, cos a)）；两翼从箭头尖沿"切线反向 ±38°"张开 ⇒ 观感是箭头**顺着圈跑**、贴着圆弧指出去。
			{
				float refreshR = num4 * 0.5f;
				graphics.DrawArc(pen, num5, num6, num4, num4, 75f, 300f);
				double refreshEnd = 15.0 * Math.PI / 180.0;
				double refreshTangent = Math.Atan2(Math.Cos(refreshEnd), Math.Sin(refreshEnd));
				float refreshTipX = num7 + refreshR * (float)Math.Cos(refreshEnd);
				float refreshTipY = num8 + refreshR * (float)Math.Sin(refreshEnd);
				float refreshWing = refreshR * 0.46f;
				for (int i = -1; i <= 1; i += 2)
				{
					double refreshBarb = refreshTangent + (double)i * (38.0 * Math.PI / 180.0) + Math.PI;
					graphics.DrawLine(pen, refreshTipX, refreshTipY, refreshTipX + refreshWing * (float)Math.Cos(refreshBarb), refreshTipY + refreshWing * (float)Math.Sin(refreshBarb));
				}
				break;
			}
		case Kind.Overlay:
			graphics.DrawEllipse(pen, num5, num6, num4, num4);
			graphics.DrawEllipse(pen, num7 - num4 * 0.17f, num8 - num4 * 0.17f, num4 * 0.34f, num4 * 0.34f);
			break;
		case Kind.Heart:
		{
			using (GraphicsPath graphicsPath4 = new GraphicsPath())
			{
				float num17 = num6 + num4 * 0.06f;
				float num18 = num6 + num4 * 0.96f;
				graphicsPath4.AddBezier(num7, num18, num5 + num4 * 0.06f, num17 + num4 * 0.62f, num5 + num4 * 0.02f, num17, num5 + num4 * 0.27f, num17);
				graphicsPath4.AddBezier(num5 + num4 * 0.27f, num17, num5 + num4 * 0.44f, num17, num7, num17 + num4 * 0.16f, num7, num17 + num4 * 0.34f);
				graphicsPath4.AddBezier(num7, num17 + num4 * 0.34f, num7, num17 + num4 * 0.16f, num5 + num4 * 0.56f, num17, num5 + num4 * 0.73f, num17);
				graphicsPath4.AddBezier(num5 + num4 * 0.73f, num17, num5 + num4 * 0.98f, num17, num5 + num4 * 0.94f, num17 + num4 * 0.62f, num7, num18);
				graphicsPath4.CloseFigure();
				graphics.DrawPath(pen, graphicsPath4);
			}
			break;
		}
		case Kind.Close:
			graphics.DrawLine(pen, num5 + num4 * 0.18f, num6 + num4 * 0.18f, num5 + num4 * 0.82f, num6 + num4 * 0.82f);
			graphics.DrawLine(pen, num5 + num4 * 0.82f, num6 + num4 * 0.18f, num5 + num4 * 0.18f, num6 + num4 * 0.82f);
			break;
		case Kind.Gear:
		{
			graphics.DrawEllipse(pen, num7 - num4 * 0.2f, num8 - num4 * 0.2f, num4 * 0.4f, num4 * 0.4f);
			float num14 = num4 * 0.33f;
			float num15 = num4 * 0.5f;
			for (int i = 0; i < 8; i++)
			{
				double num16 = Math.PI / 4.0 * (double)i;
				graphics.DrawLine(pen, num7 + (float)(Math.Cos(num16) * (double)num14), num8 + (float)(Math.Sin(num16) * (double)num14), num7 + (float)(Math.Cos(num16) * (double)num15), num8 + (float)(Math.Sin(num16) * (double)num15));
			}
			break;
		}
		case Kind.Chevrons:
		{
			using (GraphicsPath graphicsPath2 = new GraphicsPath())
			{
				graphicsPath2.AddLines(new PointF[3]
				{
					new PointF(num5 + num4 * 0.36f, num6 + num4 * 0.1f),
					new PointF(num5 + num4 * 0.08f, num8),
					new PointF(num5 + num4 * 0.36f, num6 + num4 * 0.9f)
				});
				graphics.DrawPath(pen, graphicsPath2);
				using GraphicsPath graphicsPath3 = new GraphicsPath();
				graphicsPath3.AddLines(new PointF[3]
				{
					new PointF(num5 + num4 * 0.64f, num6 + num4 * 0.1f),
					new PointF(num5 + num4 * 0.92f, num8),
					new PointF(num5 + num4 * 0.64f, num6 + num4 * 0.9f)
				});
				graphics.DrawPath(pen, graphicsPath3);
			}
			break;
		}
		case Kind.Contrast:
		{
			graphics.DrawEllipse(pen, num5, num6, num4, num4);
			using (GraphicsPath graphicsPath = new GraphicsPath())
			{
				graphicsPath.AddArc(num5, num6, num4, num4, -90f, 180f);
				graphicsPath.CloseFigure();
				using SolidBrush brush2 = new SolidBrush(Color.FromArgb(90, color));
				graphics.FillPath(brush2, graphicsPath);
			}
			break;
		}
		case Kind.Console:
		{
			float num11 = num4 * 0.74f;
			float num12 = num8 - num11 / 2f;
			using (GraphicsPath path2 = RoundedRect(new RectangleF(num5, num12, num4, num11), num2 * 1.4f))
			{
				graphics.DrawPath(pen, path2);
				float num13 = num12 + num11 * 0.32f;
				graphics.DrawLine(pen, num5 + num4 * 0.12f, num13, num5 + num4 * 0.84f, num13);
				graphics.DrawLine(pen, num5 + num4 * 0.2f, num12 + num11 * 0.62f, num5 + num4 * 0.36f, num12 + num11 * 0.62f);
				graphics.DrawLine(pen, num5 + num4 * 0.46f, num12 + num11 * 0.62f, num5 + num4 * 0.7f, num12 + num11 * 0.62f);
			}
			break;
		}
		case Kind.Package:
		{
			float num9 = num4 * 0.72f;
			float num10 = num8 - num9 / 2f;
			using (GraphicsPath path = RoundedRect(new RectangleF(num5, num10, num4, num9), num2 * 1.4f))
			{
				graphics.DrawPath(pen, path);
				graphics.DrawLine(pen, num5, num10 + num9 * 0.3f, num5 + num4, num10 + num9 * 0.3f);
				graphics.DrawLine(pen, num7, num10 + num9 * 0.3f, num7, num10 + num9);
				graphics.DrawLine(pen, num7 - num4 * 0.16f, num10 + num9 * 0.44f, num7 + num4 * 0.16f, num10 + num9 * 0.44f);
			}
			break;
		}
		case Kind.Fan:
		case Kind.Camera:
		case Kind.Microphone:
		case Kind.MicrophoneMute:
		case Kind.Keyboard:
		case Kind.Moon:
		case Kind.Plus:
		case Kind.Wifi:
		case Kind.Bluetooth:
		case Kind.Update:
		case Kind.Touchpad:
			DrawTileGlyph(graphics, pen, brush, kind, num2, num3, num4, num5, num6, num7, num8);
			break;
		default:
			graphics.DrawEllipse(pen, num5, num6, num4, num4);
			graphics.FillEllipse(brush, num7 - num2, num6 + num4 * 0.24f - num2, num2 * 2f, num2 * 2f);
			graphics.DrawLine(pen, num7, num6 + num4 * 0.42f, num7, num6 + num4 * 0.76f);
			break;
		}
		return bitmap;
	}

	/// <summary>
	/// 2026-10-11（快捷磁贴行）：磁贴图标。几何与既有 Kind 同一口径
	/// （<c>pen</c> 线宽 = max(1, size/12)、端点圆头、内边距 num3 = pen*1.6）。
	/// </summary>
	private static void DrawTileGlyph(Graphics graphics, Pen pen, SolidBrush brush, Kind kind, float num2, float num3, float num4, float num5, float num6, float num7, float num8)
	{
		switch (kind)
		{
		case Kind.Fan:
		{
			// 扇叶：一个轮毂 + 三片扇形弧（旋转对称，读起来就是"风扇/强冷"）
			float radius = num4 * 0.46f;
			float hub = num4 * 0.1f;
			graphics.DrawEllipse(pen, num7 - hub, num8 - hub, hub * 2f, hub * 2f);
			for (int i = 0; i < 3; i++)
			{
				graphics.DrawArc(pen, num7 - radius, num8 - radius, radius * 2f, radius * 2f, (float)(i * 120 + 25), 76f);
			}
			break;
		}
		case Kind.Camera:
		{
			float bodyH = num4 * 0.6f;
			float bodyY = num8 - bodyH / 2f;
			using (GraphicsPath path2 = RoundedRect(new RectangleF(num5, bodyY, num4, bodyH), num2 * 1.2f))
			{
				graphics.DrawPath(pen, path2);
			}
			// 取景器凸起
			graphics.DrawLine(pen, num5 + num4 * 0.2f, bodyY, num5 + num4 * 0.32f, bodyY - num4 * 0.12f);
			graphics.DrawLine(pen, num5 + num4 * 0.32f, bodyY - num4 * 0.12f, num5 + num4 * 0.6f, bodyY - num4 * 0.12f);
			graphics.DrawLine(pen, num5 + num4 * 0.6f, bodyY - num4 * 0.12f, num5 + num4 * 0.72f, bodyY);
			// 镜头
			float lens = bodyH * 0.46f;
			graphics.DrawEllipse(pen, num7 - lens / 2f, bodyY + bodyH / 2f - lens / 2f, lens, lens);
			break;
		}
		case Kind.Microphone:
		case Kind.MicrophoneMute:
		{
			float bodyW = num4 * 0.42f;
			float bodyX = num7 - bodyW / 2f;
			float bodyY2 = num6 + num4 * 0.04f;
			float bodyH2 = num4 * 0.56f;
			using (GraphicsPath path3 = RoundedRect(new RectangleF(bodyX, bodyY2, bodyW, bodyH2), bodyW / 2f))
			{
				graphics.DrawPath(pen, path3);
			}
			// 拾音支架（U 形 + 立杆 + 底座）
			graphics.DrawArc(pen, num5 + num4 * 0.14f, bodyY2 + bodyH2 * 0.5f, num4 * 0.72f, num4 * 0.54f, 0f, 180f);
			graphics.DrawLine(pen, num7, bodyY2 + bodyH2 * 0.5f + num4 * 0.27f, num7, num6 + num4 * 0.94f);
			graphics.DrawLine(pen, num7 - num4 * 0.17f, num6 + num4 * 0.94f, num7 + num4 * 0.17f, num6 + num4 * 0.94f);
			if (kind == Kind.MicrophoneMute)
			{
				graphics.DrawLine(pen, num5 + num4 * 0.08f, num6 + num4 * 0.9f, num5 + num4 * 0.92f, num6 + num4 * 0.1f);
			}
			break;
		}
		case Kind.Keyboard:
		{
			float kbY = num6 + num4 * 0.24f;
			float kbH = num4 * 0.52f;
			using (GraphicsPath path4 = RoundedRect(new RectangleF(num5, kbY, num4, kbH), num2 * 1.1f))
			{
				graphics.DrawPath(pen, path4);
			}
			for (int row = 0; row < 2; row++)
			{
				for (int col = 0; col < 4; col++)
				{
					float dot = Math.Max(num2 * 0.9f, 1.4f);
					graphics.FillEllipse(brush, num5 + num4 * (0.14f + 0.24f * (float)col), kbY + kbH * (0.22f + 0.4f * (float)row), dot, dot);
				}
			}
			break;
		}
		case Kind.Moon:
		{
			// 月牙＝外圆减去一个偏移的内圆（Region 布尔差，避免手写贝塞尔）
			using GraphicsPath outer = new GraphicsPath();
			outer.AddEllipse(num5, num6, num4, num4);
			using GraphicsPath inner = new GraphicsPath();
			inner.AddEllipse(num5 + num4 * 0.34f, num6 - num4 * 0.14f, num4 * 0.96f, num4 * 0.96f);
			using Region crescent = new Region(outer);
			crescent.Exclude(inner);
			graphics.FillRegion(brush, crescent);
			break;
		}
		case Kind.Plus:
			graphics.DrawLine(pen, num7, num6 + num4 * 0.14f, num7, num6 + num4 * 0.86f);
			graphics.DrawLine(pen, num5 + num4 * 0.14f, num8, num5 + num4 * 0.86f, num8);
			break;
		case Kind.Wifi:
		{
			// 「WiFi」＝信号扇：三层**上凸弧** + 底部圆点（只看形状就能认）。
			// 弧心在下方中线（num7, num6 + 0.82·num4），半径 0.24 / 0.40 / 0.56·num4，
			// 扫 210°→+120°（GDI+ 角度：0°＝右、90°＝下 ⇒ 这一段正是"彩虹"那截，270°＝正上方）。
			float wifiX = num7;
			float wifiY = num6 + num4 * 0.82f;
			for (int ring = 0; ring < 3; ring++)
			{
				float radius = num4 * (0.24f + 0.16f * (float)ring);
				graphics.DrawArc(pen, wifiX - radius, wifiY - radius, radius * 2f, radius * 2f, 210f, 120f);
			}
			float wifiDot = Math.Max(num2 * 1.2f, num4 * 0.11f);
			graphics.FillEllipse(brush, wifiX - wifiDot / 2f, wifiY - wifiDot / 2f, wifiDot, wifiDot);
			break;
		}
		case Kind.Bluetooth:
		{
			// 2026-10-12 r6（用户原话「蓝牙应当有两条线突出来的 而不是单纯的B形」）：改成**标准蓝牙符文**。
			// 结构（与 Material「bluetooth」标准符文同一套比例）：
			//   ① 竖杆：x = 0.50·num4，从 0.02 竖到 0.98 —— 符文的轴；
			//   ② 右侧上下两个**三角环**：竖杆上端 →（右上折点 0.96 / 0.30）→ 中线右侧 0.66 / 0.50；
			//      竖杆下端 →（右下折点 0.96 / 0.70）→ 同一个中线点 ⇒ 竖杆 + 这两段折线围出两个三角环；
			//   ③ **两条探出的斜线**：从中线左侧 0.44 / 0.50 斜到左上 0.26 / 0.32 与左下 0.26 / 0.68 ——
			//      它们越过竖杆、穿过环臂、探到环外一点，于是左右两侧都露出线头（经典交错形），
			//      而不是字母 B 那种"两段折角都收在竖杆上"的闭合轮廓。
			//      2026-10-12 r6b（用户原话「凸出来的可以短一点吗 感觉太夸张了」）：外伸量由 0.46·num4
			//      缩到 **0.24·num4（≈原来的 1/2）**；16px 下仍留 ≈2px 线头（看得见、⛔ 不抢主体）。
			// 16px（笔宽 1.33px、内容框 ≈11.7px）下：竖杆 + 右上/右下折点 + 左上线头各自都还占 2px 左右，
			// 轮廓仍是"左右都有分叉"，不会糊成实心块（放大图见 docs\screenshots\）。
			float btStemX = num5 + num4 * 0.5f;
			float btApexX = num5 + num4 * 0.96f;
			float btJoinX = num5 + num4 * 0.66f;
			float btTop = num6 + num4 * 0.02f;
			float btBottom = num6 + num4 * 0.98f;
			float btMid = num6 + num4 * 0.5f;
			float btApexUp = num6 + num4 * 0.3f;
			float btApexDown = num6 + num4 * 0.7f;
			graphics.DrawLine(pen, btStemX, btTop, btStemX, btBottom);
			graphics.DrawLines(pen, new PointF[3]
			{
				new PointF(btStemX, btTop),
				new PointF(btApexX, btApexUp),
				new PointF(btJoinX, btMid)
			});
			graphics.DrawLines(pen, new PointF[3]
			{
				new PointF(btStemX, btBottom),
				new PointF(btApexX, btApexDown),
				new PointF(btJoinX, btMid)
			});
			graphics.DrawLine(pen, num5 + num4 * 0.44f, btMid, num5 + num4 * 0.26f, num6 + num4 * 0.32f);
			graphics.DrawLine(pen, num5 + num4 * 0.44f, btMid, num5 + num4 * 0.26f, num6 + num4 * 0.68f);
			break;
		}
		case Kind.Update:
		{
			// 「更新」＝圆环 + 中心向下箭头（Windows 更新经典语义：有更新可下）。
			// 圆环贴近内容框（半径 0.44·num4），箭头从环内 0.28 竖到 0.62，两翼收拢到箭头尖 0.80。
			float updateRing = num4 * 0.44f;
			graphics.DrawEllipse(pen, num7 - updateRing, num8 - updateRing, updateRing * 2f, updateRing * 2f);
			graphics.DrawLine(pen, num7, num6 + num4 * 0.28f, num7, num6 + num4 * 0.62f);
			graphics.DrawLine(pen, num7 - num4 * 0.14f, num6 + num4 * 0.6f, num7, num6 + num4 * 0.8f);
			graphics.DrawLine(pen, num7 + num4 * 0.14f, num6 + num4 * 0.6f, num7, num6 + num4 * 0.8f);
			break;
		}
		case Kind.Touchpad:
		{
			// 「触摸板」＝圆角矩形（触控板面）+ 下部一条**横分隔** + 该带内**竖分成两格**（左右键）。
			// 几何（0..1 归一化到内容框 num4，原点 num5/num6）：
			//   ① 板面：整宽 × 0.84 高、垂直居中（宽 > 高 ⇒ 一眼是"触控板"而不是"方框"），圆角 = num2*1.2；
			//   ② 横分隔：y = 板顶 + 0.64·板高 ⇒ 下带（按键带）高 = 0.36·板高；
			//   ③ 竖分隔：x = num7（中线），从横分隔竖到板底 —— 与①的底边、②的横线**同笔宽同心线**，
			//      圆头端点恰好落在那两条边的中心线上（既不外溢、也不留缝）。
			// 16px（num4≈11.7、笔宽 1.33）下：板面 11.7×9.85、下带高 3.55 ⇒ 每格内空 ≈4.5×2.2 px，
			// 圆角矩形 + 横分隔 + 两格按键三者都在，⛔ 不糊成一团、也不只剩一个空框。
			float tpW = num4;
			float tpH = num4 * 0.84f;
			float tpTop = num8 - tpH / 2f;
			float tpBottom = tpTop + tpH;
			float tpSplit = tpTop + tpH * 0.64f;
			using (GraphicsPath pathTouchpad = RoundedRect(new RectangleF(num5, tpTop, tpW, tpH), num2 * 1.2f))
			{
				graphics.DrawPath(pen, pathTouchpad);
			}
			graphics.DrawLine(pen, num5, tpSplit, num5 + tpW, tpSplit);
			graphics.DrawLine(pen, num7, tpSplit, num7, tpBottom);
			break;
		}
		default:
			break;
		}
	}

	private static GraphicsPath RoundedRect(RectangleF rect, float radius)
	{
		float num = Math.Min(radius * 2f, Math.Min(rect.Width, rect.Height));
		GraphicsPath graphicsPath = new GraphicsPath();
		graphicsPath.AddArc(rect.X, rect.Y, num, num, 180f, 90f);
		graphicsPath.AddArc(rect.Right - num, rect.Y, num, num, 270f, 90f);
		graphicsPath.AddArc(rect.Right - num, rect.Bottom - num, num, num, 0f, 90f);
		graphicsPath.AddArc(rect.X, rect.Bottom - num, num, num, 90f, 90f);
		graphicsPath.CloseFigure();
		return graphicsPath;
	}
}
