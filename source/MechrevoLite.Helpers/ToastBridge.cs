using System;

namespace MechrevoLite.Helpers;

/// <summary>
/// 通知桥：旧 WinForms ToastForm 与 WPF ToastService 共用的静态入口。
/// <para>业务代码调用 ToastForm.ShowNotice/ShowFailure 时，先转发到这里；WPF 设置回调后即可显示 WPF 轻提示。</para>
/// </summary>
public static class ToastBridge
{
    public static Action<string>? FailureCallback;
    public static Action<string>? NoticeCallback;

    public static void ShowFailure(string message)
    {
        try { FailureCallback?.Invoke(message); } catch { }
    }

    public static void ShowNotice(string message)
    {
        try { NoticeCallback?.Invoke(message); } catch { }
    }
}
