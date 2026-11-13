using System;

namespace Freezia.Core;

public enum AppLanguage
{
    Thai,
    English,
}

/// <summary>Current UI language. Windows that stay open listen to <see cref="Changed"/> and re-apply their text.</summary>
public static class Loc
{
    public static AppLanguage Language { get; private set; } = AppLanguage.Thai;

    public static event Action? Changed;

    public static void Set(AppLanguage language)
    {
        if (language == Language) return;
        Language = language;
        Changed?.Invoke();
    }
}
