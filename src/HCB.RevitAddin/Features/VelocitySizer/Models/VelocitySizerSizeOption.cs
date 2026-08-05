namespace HCB.RevitAddin.Features.VelocitySizer.Models;

public sealed class VelocitySizerSizeOption
{
    public required string DisplayName { get; init; }

    public double NominalDiameterInternal { get; init; }

    public double NominalDiameterMillimeters { get; init; }

    public double WidthInternal { get; init; }

    public double HeightInternal { get; init; }

    public double WidthMillimeters { get; init; }

    public double HeightMillimeters { get; init; }

    public required double AreaSquareMeters { get; init; }

    public double VelocityMetersPerSecond { get; init; }

    // rozmiar odczytany z modelu (np. nietypowy) - zawsze dostepny niezaleznie od filtrow
    public bool IsCurrentSize { get; init; }
}
