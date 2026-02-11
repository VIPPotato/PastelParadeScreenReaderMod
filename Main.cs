using MelonLoader;

[assembly: MelonInfo(typeof(PastelParadeAccess.Main), "PastelParadeAccess", "1.1.0", "Assistant")]
[assembly: MelonGame("PastelParadeProject", "PastelParade")]

namespace PastelParadeAccess;

/// <summary>
/// Mod entrypoint partial declaration.
/// Runtime logic is split across additional partial files.
/// </summary>
public partial class Main
{
}
