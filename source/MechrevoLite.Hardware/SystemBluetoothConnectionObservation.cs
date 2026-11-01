namespace MechrevoLite.Hardware;

public readonly record struct SystemBluetoothConnectionObservation(bool IsConnected, string Name, string Address, bool MatchedKnownAddress);
