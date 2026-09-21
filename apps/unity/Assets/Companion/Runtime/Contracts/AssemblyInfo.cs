using System.Runtime.CompilerServices;

// Only the audio validator can create a validated PCM owner.
[assembly: InternalsVisibleTo("Companion.Audio")]
