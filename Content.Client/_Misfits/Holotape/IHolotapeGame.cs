using System;
namespace Content.Client._Misfits.Holotape;
/// <summary>Common window integration for transient, client-only holotape games.</summary>
public interface IHolotapeGame
{
    event Action? OnQuit;
    event Action<string>? OnStatus;
    void Restart();
}
