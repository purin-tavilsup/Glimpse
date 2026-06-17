using System.Globalization;

namespace Glimpse.Avalonia;

/// <summary>Pins invariant culture for a render so stub dates/numbers are portable; restores on dispose.</summary>
internal readonly struct CultureScope : IDisposable
{
    private readonly CultureInfo _previousCulture;
    private readonly CultureInfo _previousUiCulture;

    private CultureScope(CultureInfo previousCulture, CultureInfo previousUiCulture)
    {
        _previousCulture = previousCulture;
        _previousUiCulture = previousUiCulture;
    }

    public static CultureScope Invariant()
    {
        var scope = new CultureScope(CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        return scope;
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _previousCulture;
        CultureInfo.CurrentUICulture = _previousUiCulture;
    }
}
