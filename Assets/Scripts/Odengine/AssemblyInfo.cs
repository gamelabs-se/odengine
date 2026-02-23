using System.Runtime.CompilerServices;

// Allow the EditMode test assembly to access internal Odengine types (e.g. SnapshotData.Fields).
[assembly: InternalsVisibleTo("Odengine.Tests")]
